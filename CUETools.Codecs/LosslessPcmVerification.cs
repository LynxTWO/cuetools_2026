using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CUETools.Codecs
{
    public delegate void LosslessOutputOperation();
    public delegate IAudioSource LosslessAudioSourceFactory();
    public delegate IAudioSource LosslessAudioStreamSourceFactory(
        string path,
        Stream input);

    /// <summary>
    /// Owns an unpredictable same-directory work file and publishes it only after the caller has
    /// successfully finalized and verified it. An existing requested destination is never opened
    /// or truncated by the encoder before that point.
    /// </summary>
    public sealed class LosslessFileOutputTransaction
    {
        private readonly string requestedPath;
        private readonly string workPath;
        private readonly bool destinationExistedAtStart;
        private bool published;

        public LosslessFileOutputTransaction(string requestedPath)
        {
            if (String.IsNullOrEmpty(requestedPath))
                throw new InvalidOperationException("Lossless output requires a file path.");

            this.requestedPath = Path.GetFullPath(requestedPath);
            this.destinationExistedAtStart = File.Exists(this.requestedPath);
            string directory = Path.GetDirectoryName(this.requestedPath);
            string extension = Path.GetExtension(this.requestedPath);
            string name = Path.GetFileNameWithoutExtension(this.requestedPath);
            this.workPath = Path.Combine(
                directory,
                "." + name + ".cuetools-lossless-" + Guid.NewGuid().ToString("N") + extension);
        }

        public string RequestedPath
        {
            get { return requestedPath; }
        }

        public string WorkPath
        {
            get { return workPath; }
        }

        public bool Published
        {
            get { return published; }
        }

        public void Complete(LosslessOutputOperation finalizeAndVerify)
        {
            if (finalizeAndVerify == null)
                throw new ArgumentNullException("finalizeAndVerify");
            if (published)
                return;

            try
            {
                finalizeAndVerify();
                if (!File.Exists(workPath))
                    throw new InvalidDataException("Lossless encoder produced no output.");
                if (new FileInfo(workPath).Length == 0)
                    throw new InvalidDataException("Lossless encoder produced an empty output.");

                Publish();
            }
            catch (Exception operationFailure)
            {
                Exception cleanupFailure = TryCleanupWork();
                if (cleanupFailure != null)
                {
                    throw new IOException(
                        "Lossless finalization, verification, or publication failed. Primary failure: " +
                        operationFailure.Message +
                        " The owned work file also could not be removed: " +
                        cleanupFailure.Message,
                        operationFailure);
                }
                throw;
            }
        }

        public void CleanupWork()
        {
            Exception failure = TryCleanupWork();
            if (failure != null)
                throw new IOException("Owned lossless work-file cleanup failed.", failure);
        }

        private void Publish()
        {
            if (destinationExistedAtStart)
            {
                File.Replace(workPath, requestedPath, null);
            }
            else
            {
                // Move is intentionally create-only. If another writer creates the requested
                // path after this transaction begins, publication fails and preserves its bytes.
                File.Move(workPath, requestedPath);
            }
            published = true;
        }

        private Exception TryCleanupWork()
        {
            try
            {
                if (File.Exists(workPath))
                    File.Delete(workPath);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }

    /// <summary>
    /// Builds a bounded-memory fingerprint of the exact interleaved PCM bytes accepted by an
    /// encoder. Container hashes are not sufficient here: verification must decode the file that
    /// will be published and compare that decoded audio with the encoder input.
    /// </summary>
    public sealed class LosslessPcmFingerprint : IDisposable
    {
        private static readonly byte[] Empty = new byte[0];
        private HashAlgorithm hash = SHA256.Create();
        private bool complete;

        public void Append(AudioBuffer buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            Append(buffer.Bytes, buffer.ByteLength);
        }

        public void Append(byte[] bytes, int byteCount)
        {
            if (complete)
                throw new InvalidOperationException("PCM fingerprint is already complete.");
            if (bytes == null)
                throw new ArgumentNullException("bytes");
            if (byteCount < 0 || byteCount > bytes.Length)
                throw new ArgumentOutOfRangeException("byteCount");

            if (byteCount != 0)
                hash.TransformBlock(bytes, 0, byteCount, bytes, 0);
        }

        public byte[] Complete()
        {
            if (!complete)
            {
                hash.TransformFinalBlock(Empty, 0, 0);
                complete = true;
            }

            return (byte[])hash.Hash.Clone();
        }

        public void Dispose()
        {
            if (hash != null)
            {
                hash.Clear();
                hash = null;
            }
        }
    }

    /// <summary>
    /// Non-persisted evidence that one constrained output file decoded to the exact PCM supplied
    /// to its encoder. The PCM and encoded-file SHA-256 values remain private; callers can carry
    /// the proof to another root and revalidate the exact encoded bytes without learning either
    /// digest.
    /// </summary>
    public sealed class LosslessOutputProof
    {
        private const int Sha256Length = 32;
        private readonly string relativePath;
        private readonly byte[] pcmDigest;
        private readonly byte[] encodedDigest;
        private readonly int bitsPerSample;
        private readonly int channelCount;
        private readonly int sampleRate;
        private readonly AudioPCMConfig.SpeakerConfig channelMask;
        private readonly long sampleCount;
        private readonly long encodedLength;

        private LosslessOutputProof(
            string relativePath,
            AudioPCMConfig pcm,
            long sampleCount,
            byte[] pcmDigest,
            long encodedLength,
            byte[] encodedDigest)
        {
            this.relativePath = relativePath;
            bitsPerSample = pcm.BitsPerSample;
            channelCount = pcm.ChannelCount;
            sampleRate = pcm.SampleRate;
            channelMask = pcm.ChannelMask;
            this.sampleCount = sampleCount;
            this.pcmDigest = (byte[])pcmDigest.Clone();
            this.encodedLength = encodedLength;
            this.encodedDigest = (byte[])encodedDigest.Clone();
        }

        /// <summary>
        /// A validated, non-rooted path with no dot segments. It is the proof's only public file
        /// identity; the absolute staging path and both digests are intentionally not exposed.
        /// </summary>
        public string RelativePath
        {
            get { return relativePath; }
        }

        public int BitsPerSample
        {
            get { return bitsPerSample; }
        }

        public int ChannelCount
        {
            get { return channelCount; }
        }

        public int SampleRate
        {
            get { return sampleRate; }
        }

        public AudioPCMConfig.SpeakerConfig ChannelMask
        {
            get { return channelMask; }
        }

        public long SampleCount
        {
            get { return sampleCount; }
        }

        public long EncodedLength
        {
            get { return encodedLength; }
        }

        /// <summary>
        /// Decodes and fingerprints the same already-open file handle that is hashed for the
        /// encoded-byte identity. FileShare.Read permits another decoder reader but denies writers
        /// and deleters for the entire binding operation.
        /// </summary>
        public static LosslessOutputProof CreateVerified(
            string outputRoot,
            string encodedPath,
            string codecName,
            AudioPCMConfig expectedPcm,
            long expectedSampleCount,
            byte[] expectedPcmDigest,
            LosslessAudioStreamSourceFactory decoderFactory)
        {
            if (expectedPcm == null)
                throw new ArgumentNullException("expectedPcm");
            if (expectedSampleCount < 0)
                throw new ArgumentOutOfRangeException("expectedSampleCount");
            RequireSha256(expectedPcmDigest, "expectedPcmDigest");
            if (decoderFactory == null)
                throw new ArgumentNullException("decoderFactory");

            string relative = GetConstrainedRelativePath(
                outputRoot,
                encodedPath);
            string path = ResolveExistingFile(outputRoot, relative);
            FileStream lease = OpenReadLease(path);
            try
            {
                // Hash first, then rewind and hand this exact handle to the decoder. The lease
                // remains open throughout, so no writable or delete-sharing handle can enter
                // between the encoded-byte identity and the PCM comparison.
                long length = RequireNonemptyLength(lease, path);
                byte[] fileDigest = ComputeSha256(lease);
                lease.Position = 0;

                LosslessPcmVerifier.Verify(
                    codecName,
                    expectedPcm,
                    expectedSampleCount,
                    expectedPcmDigest,
                    delegate
                    {
                        return decoderFactory(path, lease);
                    });

                return new LosslessOutputProof(
                    relative,
                    expectedPcm,
                    expectedSampleCount,
                    expectedPcmDigest,
                    length,
                    fileDigest);
            }
            finally
            {
                lease.Close();
            }
        }

        /// <summary>
        /// Resolves this proof beneath another existing root without permitting rooted paths,
        /// traversal, alternate data streams, or an existing reparse-point component.
        /// </summary>
        public string GetConstrainedPath(string rootDirectory)
        {
            return ResolvePath(rootDirectory, relativePath, false);
        }

        /// <summary>
        /// Opens an exact-byte-validated read lease at another root. The returned stream is
        /// read-only and denies write/delete sharing until the caller disposes it, allowing a copy
        /// operation to keep the already-proved source bytes frozen.
        /// </summary>
        public FileStream OpenVerifiedReadLease(string rootDirectory)
        {
            string path = ResolveExistingFile(rootDirectory, relativePath);
            FileStream lease = OpenReadLease(path);
            try
            {
                long actualLength = RequireNonemptyLength(lease, path);
                if (actualLength != encodedLength)
                    throw new InvalidDataException(
                        "Lossless output proof failed: encoded file length changed.");

                byte[] actualDigest = ComputeSha256(lease);
                if (!SameBytes(encodedDigest, actualDigest))
                    throw new InvalidDataException(
                        "Lossless output proof failed: encoded file bytes changed.");

                lease.Position = 0;
                return lease;
            }
            catch
            {
                lease.Close();
                throw;
            }
        }

        public void VerifyFile(string rootDirectory)
        {
            using (FileStream lease = OpenVerifiedReadLease(rootDirectory))
            {
                // Opening the lease performs the complete exact-byte check.
            }
        }

        /// <summary>
        /// Repeats the PCM check at another root without exposing the stored PCM digest. Exact
        /// encoded-byte validation happens first, and the decoder then consumes that same frozen
        /// file handle.
        /// </summary>
        public void VerifyDecodedFile(
            string rootDirectory,
            LosslessAudioStreamSourceFactory decoderFactory)
        {
            if (decoderFactory == null)
                throw new ArgumentNullException("decoderFactory");

            using (FileStream lease =
                OpenVerifiedReadLease(rootDirectory))
            {
                string path = lease.Name;
                LosslessPcmVerifier.Verify(
                    "Proved lossless output",
                    new AudioPCMConfig(
                        bitsPerSample,
                        channelCount,
                        sampleRate,
                        channelMask),
                    sampleCount,
                    pcmDigest,
                    delegate
                    {
                        return decoderFactory(path, lease);
                    });
            }
        }

        private static FileStream OpenReadLease(string path)
        {
            FileStream lease = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                0x10000,
                FileOptions.SequentialScan);
            try
            {
                RequireOpenedWindowsPath(lease, path);
                return lease;
            }
            catch
            {
                lease.Close();
                throw;
            }
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(
            IntPtr handle,
            StringBuilder path,
            uint pathLength,
            uint flags);

        /// <summary>
        /// On Windows, bind lexical containment to the file that was actually opened. Component
        /// checks alone have a race where a directory can become a junction between inspection and
        /// open; the final handle path reveals that redirection.
        /// </summary>
        private static void RequireOpenedWindowsPath(
            FileStream lease,
            string expectedPath)
        {
            if (Path.DirectorySeparatorChar != '\\')
                return;

            try
            {
                uint capacity = 512;
                while (capacity <= 32768)
                {
                    StringBuilder buffer =
                        new StringBuilder((int)capacity);
                    uint length = GetFinalPathNameByHandle(
                        lease.SafeFileHandle.DangerousGetHandle(),
                        buffer,
                        capacity,
                        0);
                    if (length == 0)
                    {
                        throw new IOException(
                            "Could not resolve the opened lossless output path.",
                            new System.ComponentModel.Win32Exception(
                                Marshal.GetLastWin32Error()));
                    }
                    if (length < capacity)
                    {
                        string actual = NormalizeWindowsFinalPath(
                            buffer.ToString());
                        string expected = Path.GetFullPath(expectedPath);
                        if (!String.Equals(
                            actual,
                            expected,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                "Lossless output handle escaped its declared path.");
                        }
                        return;
                    }
                    capacity = length + 1;
                }
                throw new PathTooLongException(
                    "The opened lossless output path is too long to validate.");
            }
            catch (EntryPointNotFoundException)
            {
                // GetFinalPathNameByHandle is unavailable only on pre-Vista Windows. Preserve the
                // legacy runtime behavior there; current release targets always execute this check.
            }
        }

        private static string NormalizeWindowsFinalPath(
            string path)
        {
            const string UncPrefix = @"\\?\UNC\";
            const string DevicePrefix = @"\\?\";
            if (path.StartsWith(
                UncPrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + path.Substring(
                    UncPrefix.Length);
            }
            if (path.StartsWith(
                DevicePrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(DevicePrefix.Length);
            }
            return path;
        }

        private static long RequireNonemptyLength(FileStream lease, string path)
        {
            long length = lease.Length;
            if (length <= 0)
                throw new InvalidDataException(
                    "Lossless output proof cannot bind an empty file: " +
                    Path.GetFileName(path));
            return length;
        }

        private static byte[] ComputeSha256(Stream input)
        {
            HashAlgorithm hash = SHA256.Create();
            try
            {
                return hash.ComputeHash(input);
            }
            finally
            {
                hash.Clear();
            }
        }

        private static string GetConstrainedRelativePath(
            string rootDirectory,
            string path)
        {
            if (String.IsNullOrEmpty(path))
                throw new ArgumentException(
                    "An encoded output path is required.",
                    "path");

            string root = NormalizeRoot(rootDirectory);
            string fullPath = Path.GetFullPath(path);
            string rootPrefix = AddDirectorySeparator(root);
            StringComparison comparison = PathComparison;
            if (!fullPath.StartsWith(rootPrefix, comparison))
                throw new InvalidDataException(
                    "Lossless output is outside its declared root.");

            string relative = fullPath.Substring(rootPrefix.Length);
            ValidateRelativePath(relative);
            return NormalizeRelativePath(relative);
        }

        private static string ResolveExistingFile(
            string rootDirectory,
            string relative)
        {
            string path = ResolvePath(rootDirectory, relative, true);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "The proved lossless output does not exist.",
                    path);
            return path;
        }

        private static string ResolvePath(
            string rootDirectory,
            string relative,
            bool requireEveryComponent)
        {
            ValidateRelativePath(relative);
            string root = NormalizeRoot(rootDirectory);
            RequireDirectory(root);
            RequireNotReparsePoint(root);

            string[] segments = SplitRelativePath(relative);
            string current = root;
            bool componentsExist = true;
            for (int i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                if (!componentsExist)
                    continue;

                componentsExist = File.Exists(current) ||
                    Directory.Exists(current);
                if (!componentsExist)
                {
                    if (requireEveryComponent)
                        throw new FileNotFoundException(
                            "A lossless output path component does not exist.",
                            current);
                    continue;
                }
                RequireNotReparsePoint(current);
            }

            string fullPath = Path.GetFullPath(current);
            string prefix = AddDirectorySeparator(root);
            if (!fullPath.StartsWith(prefix, PathComparison))
                throw new InvalidDataException(
                    "Lossless output proof path escaped its declared root.");
            return fullPath;
        }

        private static string NormalizeRoot(string rootDirectory)
        {
            if (String.IsNullOrEmpty(rootDirectory))
                throw new ArgumentException(
                    "A lossless output root is required.",
                    "rootDirectory");

            string root = Path.GetFullPath(rootDirectory);
            string volumeRoot = Path.GetPathRoot(root);
            while (root.Length > volumeRoot.Length &&
                IsDirectorySeparator(root[root.Length - 1]))
            {
                root = root.Substring(0, root.Length - 1);
            }
            return root;
        }

        private static string AddDirectorySeparator(string path)
        {
            if (IsDirectorySeparator(path[path.Length - 1]))
                return path;
            return path + Path.DirectorySeparatorChar;
        }

        private static string NormalizeRelativePath(string relative)
        {
            string[] segments = SplitRelativePath(relative);
            return String.Join(
                Path.DirectorySeparatorChar.ToString(),
                segments);
        }

        private static string[] SplitRelativePath(string relative)
        {
            return relative.Split(
                new char[] { '\\', '/' },
                StringSplitOptions.None);
        }

        private static void ValidateRelativePath(string relative)
        {
            if (String.IsNullOrEmpty(relative) ||
                Path.IsPathRooted(relative))
            {
                throw new InvalidDataException(
                    "Lossless output proof requires a non-rooted relative path.");
            }

            string[] segments = SplitRelativePath(relative);
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0 ||
                    segment == "." ||
                    segment == ".." ||
                    segment.IndexOf(':') >= 0 ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    throw new InvalidDataException(
                        "Lossless output proof contains an invalid path segment.");
                }
            }
        }

        private static void RequireDirectory(string path)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(
                    "Lossless output proof root does not exist: " + path);
        }

        private static void RequireNotReparsePoint(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Lossless output proof paths cannot contain reparse points.");
        }

        private static void RequireSha256(byte[] digest, string parameterName)
        {
            if (digest == null)
                throw new ArgumentNullException(parameterName);
            if (digest.Length != Sha256Length)
                throw new ArgumentException(
                    "A SHA-256 digest must contain exactly 32 bytes.",
                    parameterName);
        }

        private static bool SameBytes(byte[] expected, byte[] actual)
        {
            if (expected == null ||
                actual == null ||
                expected.Length != actual.Length)
            {
                return false;
            }

            int difference = 0;
            for (int i = 0; i < expected.Length; i++)
                difference |= expected[i] ^ actual[i];
            return difference == 0;
        }

        private static bool IsDirectorySeparator(char value)
        {
            return value == Path.DirectorySeparatorChar ||
                value == Path.AltDirectorySeparatorChar;
        }

        private static StringComparison PathComparison
        {
            get
            {
                return Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            }
        }
    }

    /// <summary>
    /// Verifies a lossless encoder output by decoding the completed output and comparing its PCM
    /// format, sample count, and streaming fingerprint with the PCM supplied to the encoder.
    /// </summary>
    public static class LosslessPcmVerifier
    {
        public static void Verify(
            string codecName,
            AudioPCMConfig expectedPcm,
            long expectedSampleCount,
            byte[] expectedDigest,
            LosslessAudioSourceFactory decoderFactory)
        {
            if (String.IsNullOrEmpty(codecName))
                throw new ArgumentException("A codec name is required.", "codecName");
            if (expectedPcm == null)
                throw new ArgumentNullException("expectedPcm");
            if (expectedSampleCount < 0)
                throw new ArgumentOutOfRangeException("expectedSampleCount");
            if (expectedDigest == null)
                throw new ArgumentNullException("expectedDigest");
            if (decoderFactory == null)
                throw new ArgumentNullException("decoderFactory");

            IAudioSource decoder = null;
            var actualFingerprint = new LosslessPcmFingerprint();
            try
            {
                decoder = decoderFactory();
                if (decoder == null)
                    throw new InvalidDataException(codecName + " verification could not open the encoded output.");

                RequireSamePcm(codecName, expectedPcm, decoder.PCM);
                if (decoder.Length >= 0 && decoder.Length != expectedSampleCount)
                {
                    throw new InvalidDataException(
                        String.Format(
                            "{0} verification sample-count mismatch: encoded {1}, container reports {2}.",
                            codecName,
                            expectedSampleCount,
                            decoder.Length));
                }

                var buffer = new AudioBuffer(decoder, 0x10000);
                long actualSampleCount = 0;
                while (true)
                {
                    int read = decoder.Read(buffer, -1);
                    if (read == 0)
                        break;
                    if (read < 0 || read != buffer.Length)
                        throw new InvalidDataException(codecName + " decoder returned an invalid sample count.");

                    actualFingerprint.Append(buffer);
                    actualSampleCount += read;
                    if (actualSampleCount > expectedSampleCount)
                        throw new InvalidDataException(codecName + " verification decoded more samples than were encoded.");
                }

                if (actualSampleCount != expectedSampleCount)
                {
                    throw new InvalidDataException(
                        String.Format(
                            "{0} verification sample-count mismatch: encoded {1}, decoded {2}.",
                            codecName,
                            expectedSampleCount,
                            actualSampleCount));
                }

                if (!SameBytes(expectedDigest, actualFingerprint.Complete()))
                    throw new InvalidDataException(codecName + " verification failed: decoded PCM differs from the encoder input.");
            }
            finally
            {
                try
                {
                    if (decoder != null)
                        decoder.Close();
                }
                finally
                {
                    actualFingerprint.Dispose();
                }
            }
        }

        private static void RequireSamePcm(string codecName, AudioPCMConfig expected, AudioPCMConfig actual)
        {
            if (actual == null ||
                expected.BitsPerSample != actual.BitsPerSample ||
                expected.ChannelCount != actual.ChannelCount ||
                expected.SampleRate != actual.SampleRate ||
                expected.ChannelMask != actual.ChannelMask)
            {
                throw new InvalidDataException(
                    codecName + " verification failed: decoded PCM format differs from the encoder input.");
            }
        }

        private static bool SameBytes(byte[] expected, byte[] actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length)
                return false;

            int difference = 0;
            for (int i = 0; i < expected.Length; i++)
                difference |= expected[i] ^ actual[i];
            return difference == 0;
        }
    }
}
