using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using CUETools.Codecs;
using CUETools.Codecs.WMA;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WmaAudioEncoder = CUETools.Codecs.WMA.AudioEncoder;

namespace CUETools.TestCodecs
{
    [TestClass]
    public class WmaLosslessVerificationTest
    {
        private static readonly AudioPCMConfig Cd = new AudioPCMConfig(16, 2, 44100);

        [TestMethod]
        public void LosslessVerificationShipsEnabled()
        {
            var settings = new LosslessEncoderSettings { PCM = Cd };

            Assert.IsTrue(settings.DoVerify);
            Assert.IsNull(
                TypeDescriptor.GetProperties(new LossyEncoderSettings()).Find("DoVerify", false),
                "Lossy WMA must not acquire a meaningless verification control.");
        }

        [TestMethod]
        public void FinalSampleCountIsEnforcedEvenWhenPcmVerificationIsDisabled()
        {
            WmaAudioEncoder.ValidateExpectedSampleCount(true, 1000, 1000);
            WmaAudioEncoder.ValidateExpectedSampleCount(false, 1000, 999);
            Assert.ThrowsException<InvalidDataException>(
                delegate
                {
                    WmaAudioEncoder.ValidateExpectedSampleCount(true, 1000, 999);
                });
        }

        [TestMethod]
        public void MatchingPcmFormatCountAndDigestPass()
        {
            const int samples = 20000;
            byte[] pcm = CreatePcm(samples);
            string path = CreateNonemptyOutput();
            var source = new FakeAudioSource(Cd, pcm, 4093);
            try
            {
                WmaLosslessVerification.Verify(
                    path,
                    Cd,
                    samples,
                    Fingerprint(pcm),
                    delegate { return source; });

                Assert.IsTrue(source.Closed, "Verification must close the decoder.");
                Assert.IsTrue(File.Exists(path), "A verified output must be retained.");
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        [TestMethod]
        public void DigestMismatchFailsAndRemovesOutput()
        {
            const int samples = 1000;
            byte[] expected = CreatePcm(samples);
            byte[] decoded = (byte[])expected.Clone();
            decoded[decoded.Length / 2] ^= 0x40;
            string path = NewOutputPath();
            WmaOutputTransaction transaction = CreateTransactionWithOutput(path);

            Assert.ThrowsException<InvalidDataException>(
                delegate
                {
                    transaction.Complete(
                        true,
                        delegate
                        {
                            WmaLosslessVerification.Verify(
                                transaction.WorkPath,
                                Cd,
                                samples,
                                Fingerprint(expected),
                                delegate { return new FakeAudioSource(Cd, decoded, 211); });
                        });
                });

            Assert.IsFalse(File.Exists(path), "A PCM mismatch must not leave a normal WMA output.");
            Assert.IsFalse(File.Exists(transaction.WorkPath));
        }

        [TestMethod]
        public void SampleCountMismatchFailsAndRemovesOutput()
        {
            const int samples = 1000;
            byte[] expected = CreatePcm(samples);
            string path = NewOutputPath();
            WmaOutputTransaction transaction = CreateTransactionWithOutput(path);

            Assert.ThrowsException<InvalidDataException>(
                delegate
                {
                    transaction.Complete(
                        true,
                        delegate
                        {
                            WmaLosslessVerification.Verify(
                                transaction.WorkPath,
                                Cd,
                                samples + 1,
                                Fingerprint(expected),
                                delegate { return new FakeAudioSource(Cd, expected, 333); });
                        });
                });

            Assert.IsFalse(File.Exists(path), "A truncated output must not remain publishable.");
            Assert.IsFalse(File.Exists(transaction.WorkPath));
        }

        [TestMethod]
        public void PcmFormatMismatchFailsAndRemovesOutput()
        {
            const int samples = 1000;
            byte[] expected = CreatePcm(samples);
            string path = NewOutputPath();
            WmaOutputTransaction transaction = CreateTransactionWithOutput(path);
            var wrongRate = new AudioPCMConfig(16, 2, 48000);

            Assert.ThrowsException<InvalidDataException>(
                delegate
                {
                    transaction.Complete(
                        true,
                        delegate
                        {
                            WmaLosslessVerification.Verify(
                                transaction.WorkPath,
                                Cd,
                                samples,
                                Fingerprint(expected),
                                delegate { return new FakeAudioSource(wrongRate, expected, 100); });
                        });
                });

            Assert.IsFalse(File.Exists(path), "A format mismatch must not remain publishable.");
            Assert.IsFalse(File.Exists(transaction.WorkPath));
        }

        [TestMethod]
        public void DecoderFailureRemovesOutput()
        {
            const int samples = 100;
            byte[] expected = CreatePcm(samples);
            string path = NewOutputPath();
            WmaOutputTransaction transaction = CreateTransactionWithOutput(path);

            Assert.ThrowsException<IOException>(
                delegate
                {
                    transaction.Complete(
                        true,
                        delegate
                        {
                            WmaLosslessVerification.Verify(
                                transaction.WorkPath,
                                Cd,
                                samples,
                                Fingerprint(expected),
                                delegate { throw new IOException("decoder failed"); });
                        });
                });

            Assert.IsFalse(File.Exists(path), "A decoder failure must not leave an unverified WMA.");
            Assert.IsFalse(File.Exists(transaction.WorkPath));
        }

        [TestMethod]
        public void FinalizationFailureRemovesOutput()
        {
            string path = NewOutputPath();
            WmaOutputTransaction transaction = CreateTransactionWithOutput(path);

            Assert.ThrowsException<InvalidOperationException>(
                delegate
                {
                    transaction.Complete(
                        true,
                        delegate { throw new InvalidOperationException("EndWriting failed"); });
                });

            Assert.IsFalse(File.Exists(path), "A finalization failure must remove its partial output.");
            Assert.IsFalse(File.Exists(transaction.WorkPath));
        }

        [TestMethod]
        public void FinalizationFailureLeavesPreexistingRequestedBytesUnchanged()
        {
            string path = CreateNonemptyOutput();
            byte[] original = File.ReadAllBytes(path);
            WmaOutputTransaction transaction = CreateTransactionWithOutput(path);

            Assert.ThrowsException<InvalidOperationException>(
                delegate
                {
                    transaction.Complete(
                        true,
                        delegate { throw new InvalidOperationException("EndWriting failed"); });
                });

            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.IsFalse(File.Exists(transaction.WorkPath));
            DeleteIfPresent(path);
        }

        [TestMethod]
        public void SuccessfulTransactionAtomicallyReplacesPreexistingOutput()
        {
            string path = CreateNonemptyOutput();
            WmaOutputTransaction transaction = CreateTransactionWithOutput(
                path,
                new byte[] { 5, 6, 7, 8 });

            try
            {
                transaction.Complete(true, delegate { });

                CollectionAssert.AreEqual(
                    new byte[] { 5, 6, 7, 8 },
                    File.ReadAllBytes(path));
                Assert.IsTrue(transaction.Published);
                Assert.IsFalse(File.Exists(transaction.WorkPath));
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        [TestMethod]
        public void TransactionDoesNotReplaceDestinationCreatedAfterStart()
        {
            string path = NewOutputPath();
            byte[] competitor = new byte[] { 9, 8, 7, 6 };
            WmaOutputTransaction transaction = CreateTransactionWithOutput(
                path,
                new byte[] { 5, 6, 7, 8 });

            try
            {
                Assert.ThrowsException<IOException>(
                    delegate
                    {
                        transaction.Complete(
                            true,
                            delegate { File.WriteAllBytes(path, competitor); });
                    });

                CollectionAssert.AreEqual(competitor, File.ReadAllBytes(path));
                Assert.IsFalse(transaction.Published);
                Assert.IsFalse(File.Exists(transaction.WorkPath));
            }
            finally
            {
                DeleteIfPresent(transaction.WorkPath);
                DeleteIfPresent(path);
            }
        }

        [TestMethod]
        public void BlockedWorkCleanupIsLoudButRequestedPathRemainsUntouched()
        {
            string path = CreateNonemptyOutput();
            byte[] original = File.ReadAllBytes(path);
            WmaOutputTransaction transaction = CreateTransactionWithOutput(path);
            try
            {
                using (new FileStream(
                    transaction.WorkPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                    IOException exception = Assert.ThrowsException<IOException>(
                        delegate
                        {
                            transaction.Complete(
                                true,
                                delegate
                                {
                                    throw new InvalidOperationException(
                                        "verification failed");
                                });
                        });
                    StringAssert.Contains(exception.ToString(), "verification failed");
                    StringAssert.Contains(exception.ToString(), "cleanup failed");
                    CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
                    Assert.IsTrue(File.Exists(transaction.WorkPath));
                }

                transaction.CleanupWork();
                Assert.IsFalse(File.Exists(transaction.WorkPath));
            }
            finally
            {
                DeleteIfPresent(transaction.WorkPath);
                DeleteIfPresent(path);
            }
        }

        [TestMethod]
        public void DeleteCleanupWorksAfterOutputIsClosed()
        {
            string path = CreateNonemptyOutput();

            WmaOutputSafety.RemoveOrQuarantine(path);

            Assert.IsFalse(File.Exists(path), "Delete must remove an output even after Close.");
        }

        [TestMethod]
        public void RealLosslessRoundTripVerifiesWhenWindowsCodecIsAvailable()
        {
            var settings = new LosslessEncoderSettings { PCM = Cd };
            string unavailableReason;
            if (!LosslessCodecIsAvailable(settings, out unavailableReason))
                Assert.Inconclusive("Windows Media Lossless runtime is unavailable: " + unavailableReason);

            const int sampleCount = 11025;
            string path = Path.Combine(
                Path.GetTempPath(),
                "cuetools-wma-verify-" + Guid.NewGuid().ToString("N") + ".wma");
            WmaAudioEncoder encoder = null;
            try
            {
                encoder = new WmaAudioEncoder(settings, path);
                encoder.FinalSampleCount = sampleCount;

                var buffer = new AudioBuffer(Cd, sampleCount);
                buffer.Prepare(sampleCount);
                int[,] pcm = buffer.Samples;
                for (int i = 0; i < sampleCount; i++)
                {
                    pcm[i, 0] = (i * 7919 % 65536) - 32768;
                    pcm[i, 1] = (i * 3571 % 65536) - 32768;
                }

                encoder.Write(buffer);
                encoder.Close();

                Assert.IsTrue(File.Exists(path));
                Assert.IsTrue(new FileInfo(path).Length > 0);

                // This is the state the old implementation mishandled: Close set closed=true, so
                // Delete silently kept the file. The corrected lifecycle must still remove it.
                encoder.Delete();
                Assert.IsFalse(File.Exists(path));
            }
            finally
            {
                if (encoder != null)
                {
                    try { encoder.Delete(); }
                    catch { }
                }
                DeleteIfPresent(path);
            }
        }

        private static bool LosslessCodecIsAvailable(
            LosslessEncoderSettings settings,
            out string reason)
        {
            object writer = null;
            try
            {
                // SupportedModes is a UI choice list, not an availability signal. A valid codec
                // with one compatible format has no mode label and therefore returns an empty
                // string. Acquire the actual configured writer instead.
                writer = settings.GetWriter();
                reason = null;
                return true;
            }
            catch (NotSupportedException ex) when (
                String.Equals(ex.Message, "codec/format not found", StringComparison.Ordinal))
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                if (writer != null && Marshal.IsComObject(writer))
                    Marshal.ReleaseComObject(writer);
            }
        }

        private static byte[] CreatePcm(int samples)
        {
            byte[] data = new byte[samples * Cd.BlockAlign];
            var random = new Random(123456);
            random.NextBytes(data);
            return data;
        }

        private static byte[] Fingerprint(byte[] pcm)
        {
            using (var fingerprint = new PcmFingerprint())
            {
                int split = (pcm.Length / 3 / Cd.BlockAlign) * Cd.BlockAlign;
                fingerprint.Append(pcm, split);

                byte[] remainder = new byte[pcm.Length - split];
                Buffer.BlockCopy(pcm, split, remainder, 0, remainder.Length);
                fingerprint.Append(remainder, remainder.Length);
                return fingerprint.Complete();
            }
        }

        private static string CreateNonemptyOutput()
        {
            string path = NewOutputPath();
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            return path;
        }

        private static string NewOutputPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "cuetools-wma-safety-" + Guid.NewGuid().ToString("N") + ".wma");
        }

        private static WmaOutputTransaction CreateTransactionWithOutput(string path)
        {
            return CreateTransactionWithOutput(path, new byte[] { 1, 2, 3, 4 });
        }

        private static WmaOutputTransaction CreateTransactionWithOutput(
            string path,
            byte[] bytes)
        {
            var transaction = new WmaOutputTransaction(path);
            File.WriteAllBytes(transaction.WorkPath, bytes);
            return transaction;
        }

        private static void DeleteIfPresent(string path)
        {
            try
            {
                if (path != null && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private sealed class FakeAudioSource : IAudioSource
        {
            private readonly AudioPCMConfig pcm;
            private readonly byte[] bytes;
            private readonly int maxChunkSamples;
            private long position;

            internal FakeAudioSource(
                AudioPCMConfig pcm,
                byte[] bytes,
                int maxChunkSamples)
            {
                this.pcm = pcm;
                this.bytes = bytes;
                this.maxChunkSamples = maxChunkSamples;
            }

            internal bool Closed { get; private set; }
            public IAudioDecoderSettings Settings { get { return null; } }
            public AudioPCMConfig PCM { get { return pcm; } }
            public string Path { get { return null; } }
            public TimeSpan Duration { get { return TimeSpan.FromSeconds((double)Length / PCM.SampleRate); } }
            public long Length { get { return bytes.Length / pcm.BlockAlign; } }
            public long Remaining { get { return Length - position; } }

            public long Position
            {
                get { return position; }
                set
                {
                    if (value < position || value > Length)
                        throw new NotSupportedException();
                    position = value;
                }
            }

            public int Read(AudioBuffer buffer, int maxLength)
            {
                int wanted = (int)Math.Min(Remaining, maxChunkSamples);
                if (maxLength >= 0)
                    wanted = Math.Min(wanted, maxLength);

                buffer.Prepare(wanted);
                if (wanted != 0)
                {
                    Buffer.BlockCopy(
                        bytes,
                        (int)position * pcm.BlockAlign,
                        buffer.Bytes,
                        0,
                        wanted * pcm.BlockAlign);
                    position += wanted;
                }
                return wanted;
            }

            public void Close()
            {
                Closed = true;
            }
        }
    }
}
