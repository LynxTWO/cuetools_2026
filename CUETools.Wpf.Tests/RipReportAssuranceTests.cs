using System;
using System.IO;
using System.Linq;
using CUETools.Codecs;
using CUETools.Processor;
using CUETools.Wpf.Models;
using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class RipReportAssuranceTests
    {
        private static RipReport IndependentReadReport(
            int opticalReadsUsed = 2,
            int minimumAgreeingReads = 2) => new RipReport
        {
            Mode = "Test & Copy (2 reads)",
            Album = "Test album",
            TrackCount = 2,
            ArConfidence = 0,
            ArTotal = 0,
            CtdbConfidence = 0,
            CtdbTotal = 0,
            OpticalReadsUsed = opticalReadsUsed,
            MinimumAgreeingReads = minimumAgreeingReads,
            Status = $"verified after {opticalReadsUsed} reads; "
                + $"at least {minimumAgreeingReads} agreed per track"
        };

        [TestMethod]
        public void IndependentReadsAreVerifiedWithoutClaimingDatabaseConfirmation()
        {
            RipReport report = IndependentReadReport();

            Assert.IsFalse(report.DatabaseConfirmed);
            Assert.IsFalse(report.Confirmed);
            Assert.IsTrue(report.IndependentReadsVerified);
            Assert.IsTrue(report.Verified);
            StringAssert.Contains(report.BuildLogBody(),
                "verified after 2 optical reads; every track agreed across at least 2 reads");
        }

        [TestMethod]
        public void ReportPageNamesIndependentReadAssuranceAndChecksumHonestly()
        {
            var store = new ReportStore();
            var viewModel = new ReportViewModel(store);

            store.Publish(IndependentReadReport());

            Assert.IsTrue(viewModel.Confirmed);
            Assert.AreEqual("Verified by independent reads", viewModel.Headline);
            StringAssert.Contains(viewModel.IntegrityLine, "not a signature");
            StringAssert.Contains(viewModel.IntegrityLine,
                "verified after 2 optical reads; every track agreed across at least 2 reads");
            Assert.IsFalse(viewModel.IntegrityLine.Contains("self-check OK", StringComparison.Ordinal));
        }

        [TestMethod]
        public void BrokenReportObserverDoesNotBlockPublicationOrOtherObservers()
        {
            var store = new ReportStore();
            bool healthyObserverCalled = false;
            store.Changed += (_, _) => throw new InvalidOperationException("test observer failure");
            store.Changed += (_, _) => healthyObserverCalled = true;
            RipReport report = IndependentReadReport();

            store.Publish(report);

            Assert.AreSame(report, store.Current);
            Assert.IsTrue(healthyObserverCalled);
        }

        [TestMethod]
        public void HistoryDoesNotCallIndependentReadAgreementUnconfirmed()
        {
            string dir = Path.Combine(Path.GetTempPath(), "history-assurance-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "history.json.gz");
            try
            {
                var history = new HistoryStore(new FakeLog(), path);
                history.Add(IndependentReadReport());

                RecentRip recent = history.Recent(1).Single();
                StringAssert.Contains(recent.Result,
                    "verified after 2 optical reads; at least 2 agreed per track");
                Assert.IsFalse(recent.Result.Contains("not confirmed", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        [TestMethod]
        public void ThreeReadRecoveryDoesNotClaimThreeWayAgreement()
        {
            RipReport report = IndependentReadReport(3, 2);

            string body = report.BuildLogBody();
            StringAssert.Contains(body,
                "verified after 3 optical reads; every track agreed across at least 2 reads");
            Assert.IsFalse(body.Contains("3 agreeing", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void LosslessOutputWarningIsSeparateFromAccurateReadEvidence()
        {
            var report = new RipReport
            {
                Mode = "Rip",
                Album = "Test album",
                OutputDir = @"C:\archive\album",
                FileCount = 1,
                Format = "wav",
                Accurate = true,
                ArConfidence = 10,
                ArTotal = 10,
                OutputVerificationKnown = true,
                LosslessOutput = true,
                OutputVerificationPerformed = false,
                OutputVerificationDetail =
                    "not performed (this encoder exposes no trusted output-verification contract)",
                Status = "accurate"
            };
            var store = new ReportStore();
            var viewModel = new ReportViewModel(store);

            store.Publish(report);

            Assert.IsTrue(
                report.DatabaseConfirmed,
                "Read evidence remains true even when output assurance is weaker.");
            StringAssert.Contains(report.BuildLogBody(),
                "Output verify : not performed");
            StringAssert.Contains(viewModel.IntegrityLine,
                "WARNING: final lossless output was not independently decoded and compared");
        }

        [TestMethod]
        public void OutputAssuranceRecognizesEnabledDisabledLegacyAndLossyContracts()
        {
            var flake = new CUETools.Codecs.Flake.EncoderSettings();
            flake.DoVerify = true;
            OutputVerificationAssurance enabled =
                OutputVerificationAssuranceEvaluator.Evaluate(flake, lossy: false);
            Assert.IsTrue(enabled.Known);
            Assert.IsTrue(enabled.Lossless);
            Assert.IsTrue(enabled.Performed);
            StringAssert.Contains(enabled.Detail, "after metadata finalization");
            StringAssert.Contains(enabled.Detail, "PCM delivered to the encoder");

            flake.DoVerify = false;
            OutputVerificationAssurance disabled =
                OutputVerificationAssuranceEvaluator.Evaluate(flake, lossy: false);
            Assert.IsFalse(disabled.Performed);
            StringAssert.Contains(disabled.Detail, "disabled");

            var legacy = JsonConvert.DeserializeObject<
                CUETools.Codecs.CommandLine.EncoderSettings>(
                "{\"Name\":\"custom.exe\",\"Extension\":\"custom\","
                + "\"Lossless\":true,\"Path\":\"custom.exe\"}");
            Assert.IsNotNull(legacy);
            OutputVerificationAssurance legacyAssurance =
                OutputVerificationAssuranceEvaluator.Evaluate(legacy, lossy: false);
            Assert.IsFalse(legacyAssurance.Performed);
            StringAssert.Contains(legacyAssurance.Detail, "legacy external encoder");

            OutputVerificationAssurance lossy =
                OutputVerificationAssuranceEvaluator.Evaluate(flake, lossy: true);
            Assert.IsFalse(lossy.Lossless);
            Assert.IsFalse(lossy.Performed);
            StringAssert.Contains(lossy.Detail, "not applicable");

            var shapedLikeFlake = new UntrustedFlakeSettings
            {
                DoVerify = true
            };
            OutputVerificationAssurance unknownSubclass =
                OutputVerificationAssuranceEvaluator.Evaluate(
                    shapedLikeFlake,
                    lossy: false);
            Assert.IsFalse(
                unknownSubclass.Performed,
                "an approved plugin subclass must not inherit a bundled assurance claim");
            StringAssert.Contains(
                unknownSubclass.Detail,
                "no trusted output-verification contract");
        }

        [TestMethod]
        public void FinalOutputReceiptRejectsPcmChangedAfterEncoderClose()
        {
            var pcm = new AudioPCMConfig(16, 2, 44100);
            byte[] encodedPcm =
            {
                0x01, 0x02, 0x03, 0x04,
                0x05, 0x06, 0x07, 0x08
            };
            string root = Path.Combine(
                Path.GetTempPath(),
                "final-output-receipt-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "receipt-test.flac");
            Directory.CreateDirectory(root);
            try
            {
                // The proof binds both the independently decoded PCM and the exact final
                // container bytes. This small unit seam uses deterministic fake PCM decoding.
                File.WriteAllBytes(path, encodedPcm);
                var receipt = new FinalizedLosslessOutputReceipt(
                    new ReceiptAudioDest(pcm, path),
                    pcm,
                    expectedSampleCount: 2);
                receipt.Write(new AudioBuffer(pcm, encodedPcm, 2));
                receipt.Close();

                LosslessOutputProof proof = receipt.Verify(
                    root,
                    (_, _) => new ReceiptAudioSource(pcm, encodedPcm));
                proof.VerifyFile(root);
                Assert.AreEqual("receipt-test.flac", proof.RelativePath);
                Assert.AreEqual(2L, proof.SampleCount);

                // A later finalization bug that changes audio is caught by the independent
                // post-close decode, even though the encoder's earlier self-check completed.
                byte[] changedPcm = (byte[])encodedPcm.Clone();
                changedPcm[changedPcm.Length - 1] ^= 0x01;
                Assert.ThrowsException<InvalidDataException>(
                    () => receipt.Verify(
                        root,
                        (_, _) => new ReceiptAudioSource(pcm, changedPcm)));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [TestMethod]
        public void FinalOutputReceiptWriteAllocatesNothingPerBufferAfterWarmup()
        {
            var pcm = new AudioPCMConfig(16, 2, 44100);
            var bytes = new byte[4096];
            var buffer = new AudioBuffer(
                pcm,
                bytes,
                bytes.Length / pcm.BlockAlign);
            _ = buffer.Bytes; // Materialize the reusable buffer outside the measured loop.
            var receipt = new FinalizedLosslessOutputReceipt(
                new ReceiptAudioDest(pcm),
                pcm,
                expectedSampleCount: 1);

            receipt.Write(buffer); // JIT and initialize the hash implementation.
            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++)
                receipt.Write(buffer);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            receipt.Delete();

            Assert.AreEqual(
                0L,
                allocated,
                "The final-output PCM receipt allocated inside its per-buffer write loop.");
        }

        [TestMethod]
        public void RecentHistoryWarnsWhenLosslessOutputWasNotVerified()
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "history-output-assurance-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "history.json.gz");
            try
            {
                var history = new HistoryStore(new FakeLog(), path);
                history.Add(new RipReport
                {
                    Mode = "Rip",
                    Album = "Test album",
                    TrackCount = 1,
                    OutputVerificationKnown = true,
                    LosslessOutput = true,
                    OutputVerificationPerformed = false
                });

                StringAssert.Contains(
                    history.Recent(1).Single().Result,
                    "WARNING: encoded output not verified");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        private sealed class FakeLog : IDiagnosticLog
        {
            public string LogPath => "unused.log";
            public void Info(string category, string message) { }
            public void Warn(string category, string message) { }
            public void Error(string category, string message, Exception ex = null) { }
            public void Redact(params string[] sensitive) { }
        }

        private sealed class UntrustedFlakeSettings :
            CUETools.Codecs.Flake.EncoderSettings
        {
        }

        private sealed class ReceiptAudioDest : IAudioDest
        {
            internal ReceiptAudioDest(
                AudioPCMConfig pcm,
                string path = "receipt-test.flac")
            {
                Settings = new CUETools.Codecs.WAV.EncoderSettings(pcm);
                Path = path;
            }

            public IAudioEncoderSettings Settings { get; }
            public string Path { get; }
            public long FinalSampleCount { set { } }
            public void Write(AudioBuffer buffer) { }
            public void Close() { }
            public void Delete() { }
        }

        private sealed class ReceiptAudioSource : IAudioSource
        {
            private readonly byte[] _bytes;
            private long _position;

            internal ReceiptAudioSource(AudioPCMConfig pcm, byte[] bytes)
            {
                PCM = pcm;
                _bytes = (byte[])bytes.Clone();
                Settings = new CUETools.Codecs.WAV.DecoderSettings();
            }

            public IAudioDecoderSettings Settings { get; }
            public AudioPCMConfig PCM { get; }
            public string Path => "receipt-test.flac";
            public TimeSpan Duration =>
                TimeSpan.FromSeconds((double)Length / PCM.SampleRate);
            public long Length => _bytes.Length / PCM.BlockAlign;
            public long Position
            {
                get => _position;
                set => _position = value;
            }
            public long Remaining => Length - _position;

            public int Read(AudioBuffer buffer, int maxLength)
            {
                int count = (int)Math.Min(
                    Remaining,
                    maxLength < 0 ? Remaining : maxLength);
                if (count == 0)
                    return 0;

                int byteOffset = checked((int)_position * PCM.BlockAlign);
                byte[] chunk = new byte[count * PCM.BlockAlign];
                Buffer.BlockCopy(
                    _bytes,
                    byteOffset,
                    chunk,
                    0,
                    chunk.Length);
                buffer.Prepare(new AudioBuffer(PCM, chunk, count), 0, count);
                _position += count;
                return count;
            }

            public void Close() { }
        }
    }
}
