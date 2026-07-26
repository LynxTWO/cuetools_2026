using System;
using System.IO;
using System.Security.Cryptography;

namespace CUETools.Codecs.WMA
{
    internal delegate IAudioSource WmaAudioSourceFactory(string path);
    internal delegate void WmaOutputOperation();

    /// <summary>
    /// A streaming fingerprint of the exact interleaved PCM bytes accepted by the WMA writer.
    /// Sample count and PCM format are checked separately, so neither truncation nor a format
    /// reinterpretation can be hidden by comparing only a container-level property.
    /// </summary>
    internal sealed class PcmFingerprint : IDisposable
    {
        private static readonly byte[] Empty = new byte[0];
        private HashAlgorithm hash = SHA256.Create();
        private bool complete;

        internal void Append(byte[] bytes, int byteCount)
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

        internal byte[] Complete()
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

    internal static class WmaLosslessVerification
    {
        internal static void Verify(
            string path,
            AudioPCMConfig expectedPcm,
            long expectedSampleCount,
            byte[] expectedDigest)
        {
            Verify(path, expectedPcm, expectedSampleCount, expectedDigest, OpenDecoder);
        }

        internal static void Verify(
            string path,
            AudioPCMConfig expectedPcm,
            long expectedSampleCount,
            byte[] expectedDigest,
            WmaAudioSourceFactory decoderFactory)
        {
            if (String.IsNullOrEmpty(path))
                throw new InvalidOperationException("WMA verification requires a file output.");
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new InvalidDataException("WMA encoder produced no output.");
            if (expectedPcm == null)
                throw new ArgumentNullException("expectedPcm");
            if (expectedSampleCount < 0)
                throw new ArgumentOutOfRangeException("expectedSampleCount");
            if (expectedDigest == null)
                throw new ArgumentNullException("expectedDigest");
            if (decoderFactory == null)
                throw new ArgumentNullException("decoderFactory");

            IAudioSource decoder = null;
            var actualFingerprint = new PcmFingerprint();
            try
            {
                decoder = decoderFactory(path);
                RequireSamePcm(expectedPcm, decoder.PCM);

                var buffer = new AudioBuffer(decoder, 0x10000);
                long actualSampleCount = 0;
                int read;
                while ((read = decoder.Read(buffer, -1)) != 0)
                {
                    actualFingerprint.Append(buffer.Bytes, buffer.ByteLength);
                    actualSampleCount = checked(actualSampleCount + read);
                }

                if (actualSampleCount != expectedSampleCount)
                    throw new InvalidDataException(
                        String.Format(
                            "WMA verification sample-count mismatch: encoded {0}, decoded {1}.",
                            expectedSampleCount,
                            actualSampleCount));

                if (!SameBytes(expectedDigest, actualFingerprint.Complete()))
                    throw new InvalidDataException(
                        "WMA verification failed: decoded PCM differs from the encoder input.");
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

        private static IAudioSource OpenDecoder(string path)
        {
            return new AudioDecoder(new DecoderSettings(), path, null);
        }

        private static void RequireSamePcm(AudioPCMConfig expected, AudioPCMConfig actual)
        {
            if (actual == null ||
                expected.BitsPerSample != actual.BitsPerSample ||
                expected.ChannelCount != actual.ChannelCount ||
                expected.SampleRate != actual.SampleRate ||
                expected.ChannelMask != actual.ChannelMask)
            {
                throw new InvalidDataException(
                    "WMA verification failed: decoded PCM format differs from the encoder input.");
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

    internal interface IWmaFileOperations
    {
        bool Exists(string path);
        long Length(string path);
        void Delete(string path);
        void Move(string sourcePath, string destinationPath);
        void Replace(string sourcePath, string destinationPath);
    }

    internal sealed class SystemWmaFileOperations : IWmaFileOperations
    {
        public bool Exists(string path)
        {
            return File.Exists(path);
        }

        public long Length(string path)
        {
            return new FileInfo(path).Length;
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }

        public void Move(string sourcePath, string destinationPath)
        {
            File.Move(sourcePath, destinationPath);
        }

        public void Replace(string sourcePath, string destinationPath)
        {
            File.Replace(sourcePath, destinationPath, null);
        }
    }

    /// <summary>
    /// Owns an unpredictable, same-directory WMA work file. The requested path is not changed until
    /// finalization and (for lossless output) PCM verification have both succeeded.
    /// </summary>
    internal sealed class WmaOutputTransaction
    {
        private readonly string requestedPath;
        private readonly string workPath;
        private readonly IWmaFileOperations fileOperations;
        private readonly bool destinationExistedAtStart;
        private bool published;

        internal WmaOutputTransaction(string requestedPath)
            : this(requestedPath, new SystemWmaFileOperations())
        {
        }

        internal WmaOutputTransaction(
            string requestedPath,
            IWmaFileOperations fileOperations)
        {
            if (String.IsNullOrEmpty(requestedPath))
                throw new InvalidOperationException("WMA output requires a file path.");
            if (fileOperations == null)
                throw new ArgumentNullException("fileOperations");

            this.requestedPath = Path.GetFullPath(requestedPath);
            this.fileOperations = fileOperations;
            this.destinationExistedAtStart =
                fileOperations.Exists(this.requestedPath);
            string directory = Path.GetDirectoryName(this.requestedPath);
            string extension = Path.GetExtension(this.requestedPath);
            string name = Path.GetFileNameWithoutExtension(this.requestedPath);
            this.workPath = Path.Combine(
                directory,
                "." + name + ".cuetools-wma-" +
                Guid.NewGuid().ToString("N") + extension);
        }

        internal string RequestedPath
        {
            get { return requestedPath; }
        }

        internal string WorkPath
        {
            get { return workPath; }
        }

        internal bool Published
        {
            get { return published; }
        }

        internal void Complete(
            bool outputWasRequested,
            WmaOutputOperation finalizeAndVerify)
        {
            if (finalizeAndVerify == null)
                throw new ArgumentNullException("finalizeAndVerify");

            try
            {
                finalizeAndVerify();
                if (!outputWasRequested)
                    return;

                if (!fileOperations.Exists(workPath))
                    throw new InvalidDataException("WMA encoder produced no output.");
                if (fileOperations.Length(workPath) == 0)
                    throw new InvalidDataException("WMA encoder produced an empty output.");

                Publish();
            }
            catch (Exception operationFailure)
            {
                Exception cleanupFailure = TryCleanupWork();
                if (cleanupFailure != null)
                {
                    throw new IOException(
                        "WMA finalization, verification, or publication failed. Primary failure: " +
                        operationFailure.Message +
                        " The owned work file also could not be removed: " +
                        cleanupFailure.Message,
                        operationFailure);
                }
                throw;
            }
        }

        private void Publish()
        {
            if (destinationExistedAtStart)
            {
                fileOperations.Replace(workPath, requestedPath);
            }
            else
            {
                // Move is intentionally create-only. A destination created after construction
                // belongs to the competing writer and must never be replaced by this transaction.
                fileOperations.Move(workPath, requestedPath);
            }
            published = true;
        }

        internal void CleanupWork()
        {
            Exception failure = TryCleanupWork();
            if (failure != null)
                throw new IOException(
                    "Owned WMA work-file cleanup failed.",
                    failure);
        }

        private Exception TryCleanupWork()
        {
            try
            {
                if (fileOperations.Exists(workPath))
                    fileOperations.Delete(workPath);
                return null;
            }
            catch (Exception ex)
            {
                return new IOException(
                    "Owned WMA work-file cleanup failed: " + ex.Message,
                    ex);
            }
        }
    }

    internal static class WmaOutputSafety
    {
        internal static void RemoveOrQuarantine(string path)
        {
            if (String.IsNullOrEmpty(path))
                throw new InvalidOperationException("WMA cleanup requires a file output.");
            if (!File.Exists(path))
                return;

            try
            {
                File.Delete(path);
            }
            catch
            {
                // A delayed handle may deny deletion. Moving the already-published owned file away
                // from the requested extension keeps a Delete operation semantically honest.
            }

            if (File.Exists(path))
                File.Move(
                    path,
                    path + ".deleted-" + Guid.NewGuid().ToString("N"));
        }
    }
}
