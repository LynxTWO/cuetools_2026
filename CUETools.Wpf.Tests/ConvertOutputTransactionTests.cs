using System;
using System.IO;
using System.Linq;
using System.Text;
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class ConvertOutputTransactionTests
    {
        private string _root;
        private string _input;
        private string _audio;
        private string _output;
        private CUEConfig _config;
        private AppSettings _settings;
        private EncoderCatalog _catalog;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(),
                "cuetools-convert-transaction-" + Guid.NewGuid().ToString("N"));
            _output = Path.Combine(_root, "output");
            _audio = Path.Combine(_root, "source.wav");
            _input = Path.Combine(_root, "source.cue");
            Directory.CreateDirectory(_root);
            WritePcmWave(_audio);
            File.WriteAllText(_input,
                "FILE \"source.wav\" WAVE" + Environment.NewLine +
                "  TRACK 01 AUDIO" + Environment.NewLine +
                "    INDEX 01 00:00:00" + Environment.NewLine);
            _config = new CUEConfig();
            _settings = new AppSettings
            {
                // With no source tags this renders no common directory, exercising the input-name
                // fallback while keeping the final destination a single child of _output.
                NamingTemplate = "%tracknumber% - %title%",
            };
            _catalog = new EncoderCatalog(new FakeLog(), _settings,
                Path.Combine(_root, "encoders"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        [TestMethod]
        public void ConversionPublishesOnlyAfterEveryDeclaredOutputIsFinalized()
        {
            string capturedStage = "";
            var service = CreateService(cue =>
            {
                capturedStage = cue.OutputDir;
                Assert.IsTrue(Path.GetFileName(capturedStage)
                    .StartsWith(".cuetools-stage-", StringComparison.Ordinal));
                Assert.AreEqual(0, VisibleOutputDirectories().Length);
                WriteAllDeclaredOutputs(cue, empty: false);
                File.WriteAllText(cue.OutputPath, "cue");
                Assert.AreEqual(0, VisibleOutputDirectories().Length);
                return "Converted";
            });

            ConvertResult result = service.Convert(
                _input, "flac", _output, (_, _) => { });

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual("Converted", result.Status);
            Assert.AreEqual(1, result.FileCount);
            Assert.IsFalse(Directory.Exists(capturedStage));
            Assert.IsTrue(Directory.Exists(result.OutputDir));
            Assert.IsTrue(File.Exists(Path.Combine(result.OutputDir,
                AlbumOutputTransaction.CompletionMarkerName)));
            Assert.AreEqual(1,
                Directory.GetFiles(result.OutputDir, "*.flac",
                    SearchOption.AllDirectories).Length);
            Assert.IsTrue(
                File.Exists(Path.Combine(result.OutputDir, "source.cue")),
                "The human-facing cue did not carry the source album identity.");
        }

        [TestMethod]
        public void EngineFailureLeavesFinalDestinationAbsent()
        {
            var service = CreateService(cue =>
            {
                WriteAllDeclaredOutputs(cue, empty: false);
                throw new StopException();
            });

            ConvertResult result = service.Convert(
                _input, "flac", _output, (_, _) => { });

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(0, VisibleOutputDirectories().Length);
            Assert.AreEqual(0, StagingDirectories().Length);
        }

        [TestMethod]
        public void MissingDeclaredOutputIsNeverPublished()
        {
            var service = CreateService(_ => "Encoder returned success");

            ConvertResult result = service.Convert(
                _input, "flac", _output, (_, _) => { });

            Assert.IsFalse(result.Ok);
            StringAssert.Contains(result.Error, "missing");
            Assert.AreEqual(0, VisibleOutputDirectories().Length);
            Assert.AreEqual(0, StagingDirectories().Length);
        }

        [TestMethod]
        public void EmptyDeclaredOutputIsNeverPublished()
        {
            var service = CreateService(cue =>
            {
                WriteAllDeclaredOutputs(cue, empty: true);
                return "Encoder returned success";
            });

            ConvertResult result = service.Convert(
                _input, "flac", _output, (_, _) => { });

            Assert.IsFalse(result.Ok);
            StringAssert.Contains(result.Error, "empty");
            Assert.AreEqual(0, VisibleOutputDirectories().Length);
            Assert.AreEqual(0, StagingDirectories().Length);
        }

        [TestMethod]
        public void CompletionCallbackFailureCannotMisreportCommittedConversion()
        {
            var service = CreateService(cue =>
            {
                WriteAllDeclaredOutputs(cue, empty: false);
                return "Converted";
            });

            ConvertResult result = service.Convert(_input, "flac", _output,
                (progress, _) =>
                {
                    if (progress >= 1)
                        throw new InvalidOperationException("UI was closed");
                });

            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsTrue(Directory.Exists(result.OutputDir));
            Assert.IsTrue(File.Exists(Path.Combine(result.OutputDir,
                AlbumOutputTransaction.CompletionMarkerName)));
        }

        private ConvertService CreateService(Func<CUESheet, string> runEngine) =>
            new(_config, _catalog, _settings, runEngine);

        private string[] VisibleOutputDirectories() =>
            !Directory.Exists(_output)
                ? Array.Empty<string>()
                : Directory.GetDirectories(_output)
                    .Where(path => !Path.GetFileName(path).StartsWith(".",
                        StringComparison.Ordinal))
                    .ToArray();

        private string[] StagingDirectories() =>
            !Directory.Exists(_output)
                ? Array.Empty<string>()
                : Directory.GetDirectories(_output, ".cuetools-stage-*",
                    SearchOption.AllDirectories);

        private static void WriteAllDeclaredOutputs(CUESheet cue, bool empty)
        {
            Assert.IsNotNull(cue.DestPaths);
            Assert.IsTrue(cue.DestPaths.Length > 0);
            foreach (string path in cue.DestPaths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)
                    ?? throw new InvalidOperationException("Output has no parent."));
                File.WriteAllBytes(path, empty ? Array.Empty<byte>() : new byte[] { 1, 2, 3 });
            }
        }

        private static void WritePcmWave(string path)
        {
            const int sampleRate = 44100;
            const short channels = 2;
            const short bits = 16;
            const int frames = 588;
            int blockAlign = channels * bits / 8;
            int dataLength = frames * blockAlign;

            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new BinaryWriter(stream, Encoding.ASCII);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * blockAlign);
            writer.Write((short)blockAlign);
            writer.Write(bits);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            writer.Write(new byte[dataLength]);
        }

        private sealed class FakeLog : IDiagnosticLog
        {
            public string LogPath => "";
            public void Info(string category, string message) { }
            public void Warn(string category, string message) { }
            public void Error(string category, string message, Exception ex = null) { }
            public void Redact(params string[] sensitive) { }
        }
    }
}
