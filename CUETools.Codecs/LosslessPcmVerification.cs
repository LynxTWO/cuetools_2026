using System;
using System.IO;
using System.Security.Cryptography;

namespace CUETools.Codecs
{
    public delegate void LosslessOutputOperation();
    public delegate IAudioSource LosslessAudioSourceFactory();

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
