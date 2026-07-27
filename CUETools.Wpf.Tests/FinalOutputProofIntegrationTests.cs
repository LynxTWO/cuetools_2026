using System;
using System.IO;
using System.Linq;
using CUETools.Codecs;
using CUETools.Processor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public sealed class FinalOutputProofIntegrationTests
    {
        private const int CdFrameSamples = 588;
        private const int TotalFrames = 6;
        private const int TotalSamples = CdFrameSamples * TotalFrames;
        private string _root;

        [TestInitialize]
        public void Initialize()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "cuetools-final-proof-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, true);
            }
            catch
            {
                // A test assertion should remain the reported failure if a codec retained a
                // diagnostic handle. The lease tests themselves verify normal handle release.
            }
        }

        [DataTestMethod]
        [DataRow((int)CUEStyle.SingleFile)]
        [DataRow((int)CUEStyle.SingleFileWithCUE)]
        [DataRow((int)CUEStyle.GapsAppended)]
        [DataRow((int)CUEStyle.GapsPrepended)]
        [DataRow((int)CUEStyle.GapsLeftOut)]
        public void RealFlakeAndTagLibProduceCompleteProofsForEveryOutputStyle(
            int styleValue)
        {
            CUEStyle style = (CUEStyle)styleValue;
            CUESheet sheet = CreateSheet(style);
            try
            {
                sheet.Go();

                int expectedCount =
                    style == CUEStyle.SingleFile ||
                    style == CUEStyle.SingleFileWithCUE
                        ? 1
                        : style == CUEStyle.GapsAppended ? 3 : 2;
                Assert.IsTrue(sheet.FinalOutputVerifiedAfterMetadata);
                Assert.AreEqual(expectedCount, sheet.DestPaths.Length);
                Assert.AreEqual(expectedCount, sheet.FinalOutputProofs.Count);
                Assert.AreEqual(
                    expectedCount,
                    sheet.FinalOutputProofs
                        .Select(proof => proof.RelativePath)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count());

                for (int i = 0; i < expectedCount; i++)
                {
                    LosslessOutputProof proof = sheet.FinalOutputProofs[i];
                    string expectedPath = Path.GetFullPath(sheet.DestPaths[i]);
                    Assert.IsTrue(
                        String.Equals(
                            expectedPath,
                            Path.GetFullPath(
                                proof.GetConstrainedPath(
                                    sheet.OutputDir)),
                            StringComparison.OrdinalIgnoreCase),
                        "Proof/output path identity mismatch.");
                    Assert.IsFalse(Path.IsPathRooted(proof.RelativePath));
                    Assert.AreEqual(16, proof.BitsPerSample);
                    Assert.AreEqual(2, proof.ChannelCount);
                    Assert.AreEqual(44100, proof.SampleRate);
                    Assert.AreEqual(
                        new FileInfo(expectedPath).Length,
                        proof.EncodedLength);
                    proof.VerifyFile(sheet.OutputDir);
                }

                int firstTaggedOutput =
                    style == CUEStyle.GapsAppended ? 1 : 0;
                using (TagLib.File tagged =
                    TagLib.File.Create(
                        sheet.DestPaths[firstTaggedOutput]))
                {
                    Assert.AreEqual(
                        "Proof Album",
                        tagged.Tag.Album,
                        "The proof must be generated after the real TagLib save.");
                }

                // GapsAppended is the only style that publishes the one-frame HTOA in this
                // fixture; the disabled five-second threshold deliberately exercises that path.
                if (style == CUEStyle.GapsAppended)
                {
                    Assert.AreEqual(
                        CdFrameSamples,
                        sheet.FinalOutputProofs[0].SampleCount);
                    StringAssert.Contains(
                        sheet.FinalOutputProofs[0].RelativePath,
                        "HTOA");
                }
            }
            finally
            {
                sheet.Close();
            }
        }

        [TestMethod]
        public void PostTagLibAudioReplacementCannotAcquireAProof()
        {
            CUESheet sheet = CreateSheet(CUEStyle.SingleFile);
            bool observedSavedTag = false;
            sheet.FinalOutputPostMetadataTestHook = paths =>
            {
                using (TagLib.File tagged = TagLib.File.Create(paths[0]))
                    observedSavedTag = tagged.Tag.Album == "Proof Album";

                // Replace the tagged output with a separately valid, internally verified FLAC
                // of the same PCM shape but different samples. Container validity and matching
                // length cannot substitute for comparison with the actual encoder input.
                WriteFlac(paths[0], TotalSamples, seed: 7001);
            };
            try
            {
                Assert.ThrowsException<InvalidDataException>(
                    () => sheet.Go());
                Assert.IsTrue(
                    observedSavedTag,
                    "The mutation seam did not run after TagLib finalization.");
                Assert.IsFalse(sheet.FinalOutputVerifiedAfterMetadata);
                Assert.AreEqual(0, sheet.FinalOutputProofs.Count);
            }
            finally
            {
                sheet.Close();
            }
        }

        [TestMethod]
        public void OneBadTrackWithholdsTheOtherwiseCompletedProofSet()
        {
            CUESheet sheet = CreateSheet(CUEStyle.GapsPrepended);
            sheet.FinalOutputPostMetadataTestHook = paths =>
            {
                Assert.AreEqual(2, paths.Length);
                File.WriteAllBytes(paths[1], new byte[0]);
            };
            try
            {
                Assert.ThrowsException<InvalidDataException>(
                    () => sheet.Go());
                Assert.IsFalse(sheet.FinalOutputVerifiedAfterMetadata);
                Assert.AreEqual(
                    0,
                    sheet.FinalOutputProofs.Count,
                    "A proof for track one leaked before track two failed.");
            }
            finally
            {
                sheet.Close();
            }
        }

        [DataTestMethod]
        [DataRow("open")]
        [DataRow("read")]
        [DataRow("close")]
        public void DecoderLifecycleFailureWithholdsAllProofs(string phase)
        {
            CUESheet sheet = CreateSheet(CUEStyle.SingleFile);
            CUEConfig config = sheet.Config;
            sheet.FinalOutputDecoderFactoryOverride = (path, input) =>
            {
                if (phase == "open")
                    throw new IOException("Injected decoder-open failure.");

                IAudioSource decoder =
                    AudioReadWrite.GetAudioSource(path, input, config);
                return new FaultingAudioSource(decoder, phase);
            };
            try
            {
                Assert.ThrowsException<IOException>(
                    () => sheet.Go());
                Assert.IsFalse(sheet.FinalOutputVerifiedAfterMetadata);
                Assert.AreEqual(0, sheet.FinalOutputProofs.Count);
            }
            finally
            {
                sheet.Close();
            }
        }

        [TestMethod]
        public void EncoderFinalizationFailureCannotProduceAProof()
        {
            CUEConfig config = CreateConfig();
            config.formats["failproof"] = new CUEToolsFormat(
                "failproof",
                CUEToolsTagger.TagLibSharp,
                true,
                false,
                false,
                true,
                new AudioEncoderSettingsViewModel(
                    new FailingFinalizeEncoderSettings()),
                null,
                null);
            CUESheet sheet = CreateSheet(
                CUEStyle.SingleFile,
                config,
                "failproof");
            try
            {
                Assert.ThrowsException<IOException>(
                    () => sheet.Go());
                Assert.IsFalse(sheet.FinalOutputVerifiedAfterMetadata);
                Assert.AreEqual(0, sheet.FinalOutputProofs.Count);
            }
            finally
            {
                sheet.Close();
            }
        }

        [TestMethod]
        public void CompletedProofFreezesCopySourceAndRejectsLaterMutation()
        {
            CUESheet sheet = CreateSheet(CUEStyle.SingleFile);
            try
            {
                sheet.Go();
                LosslessOutputProof proof =
                    sheet.FinalOutputProofs.Single();
                string copyRoot = Path.Combine(_root, "relocated");
                Directory.CreateDirectory(copyRoot);
                string copyPath =
                    proof.GetConstrainedPath(copyRoot);
                string copyDirectory = Path.GetDirectoryName(copyPath);
                if (!Directory.Exists(copyDirectory))
                    Directory.CreateDirectory(copyDirectory);

                using (FileStream sourceLease =
                    proof.OpenVerifiedReadLease(sheet.OutputDir))
                {
                    Assert.ThrowsException<IOException>(() =>
                    {
                        using (FileStream ignored = new FileStream(
                            sourceLease.Name,
                            FileMode.Open,
                            FileAccess.Write,
                            FileShare.ReadWrite))
                        {
                        }
                    });
                    File.Copy(sourceLease.Name, copyPath, false);
                }

                proof.VerifyFile(copyRoot);
                using (FileStream mutation = new FileStream(
                    copyPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read))
                {
                    mutation.WriteByte(0x7f);
                }
                Assert.ThrowsException<InvalidDataException>(
                    () => proof.VerifyFile(copyRoot));
            }
            finally
            {
                sheet.Close();
            }
        }

        private CUESheet CreateSheet(
            CUEStyle style,
            CUEConfig config = null,
            string format = "flac")
        {
            config = config ?? CreateConfig();
            string inputPath = Path.Combine(_root, "input.wav");
            string cuePath = Path.Combine(_root, "input.cue");
            WriteWav(inputPath, TotalSamples, seed: 17);
            File.WriteAllText(
                cuePath,
                "PERFORMER \"Proof Artist\"" + Environment.NewLine +
                "TITLE \"Proof Album\"" + Environment.NewLine +
                "FILE \"input.wav\" WAVE" + Environment.NewLine +
                "  TRACK 01 AUDIO" + Environment.NewLine +
                "    TITLE \"First\"" + Environment.NewLine +
                "    PERFORMER \"Proof Artist\"" + Environment.NewLine +
                "    INDEX 00 00:00:00" + Environment.NewLine +
                "    INDEX 01 00:00:01" + Environment.NewLine +
                "  TRACK 02 AUDIO" + Environment.NewLine +
                "    TITLE \"Second\"" + Environment.NewLine +
                "    PERFORMER \"Proof Artist\"" + Environment.NewLine +
                "    INDEX 00 00:00:03" + Environment.NewLine +
                "    INDEX 01 00:00:04" + Environment.NewLine);

            var sheet = new CUESheet(config);
            sheet.Open(cuePath);
            sheet.Action = CUEAction.Encode;
            sheet.OutputStyle = style;
            sheet.SetExplicitTrackNames(
                new string[] { "01 - First", "02 - Second" });
            sheet.VerifyFinalOutputAfterMetadata = true;
            sheet.GenerateFilenames(
                AudioEncoderType.Lossless,
                format,
                Path.Combine(_root, "output", "album.cue"));
            return sheet;
        }

        private static CUEConfig CreateConfig()
        {
            var config = new CUEConfig
            {
                autoCorrectFilenames = false,
                preserveHTOA = true,
                useHTOALengthThreshold = false,
                detectHDCD = false,
                separateDecodingThread = false,
                createEACLOG = false,
                writeArLogOnConvert = false,
                writeArTagsOnEncode = false,
                extractLog = false,
                embedLog = false,
                extractAlbumArt = false,
                embedAlbumArt = false,
                CopyAlbumArt = false,
                copyBasicTags = false,
                copyUnknownTags = false,
                writeBasicTagsFromCUEData = true,
                createCUEFileWhenEmbedded = false,
                createCUEFileInTracksMode = false,
                createM3U = false
            };
            var encoderSettings =
                new CUETools.Codecs.Flake.EncoderSettings
                {
                    EncoderMode = "0",
                    DoVerify = true,
                    DoMD5 = true
                };
            var decoderSettings =
                new CUETools.Codecs.Flake.DecoderSettings();
            config.formats["flac"] = new CUEToolsFormat(
                "flac",
                CUEToolsTagger.TagLibSharp,
                true,
                false,
                true,
                true,
                new AudioEncoderSettingsViewModel(encoderSettings),
                null,
                new AudioDecoderSettingsViewModel(decoderSettings));
            return config;
        }

        private static void WriteWav(
            string path,
            int sampleCount,
            int seed)
        {
            var settings =
                new CUETools.Codecs.WAV.EncoderSettings(
                    AudioPCMConfig.RedBook);
            var destination =
                new CUETools.Codecs.WAV.AudioEncoder(settings, path);
            destination.FinalSampleCount = sampleCount;
            destination.Write(new AudioBuffer(
                AudioPCMConfig.RedBook,
                CreatePcmBytes(sampleCount, seed),
                sampleCount));
            destination.Close();
        }

        private static void WriteFlac(
            string path,
            int sampleCount,
            int seed)
        {
            var settings =
                new CUETools.Codecs.Flake.EncoderSettings
                {
                    PCM = AudioPCMConfig.RedBook,
                    EncoderMode = "0",
                    Padding = 64,
                    DoVerify = true,
                    DoMD5 = true
                };
            var destination =
                new CUETools.Codecs.Flake.AudioEncoder(settings, path);
            destination.FinalSampleCount = sampleCount;
            destination.Write(new AudioBuffer(
                AudioPCMConfig.RedBook,
                CreatePcmBytes(sampleCount, seed),
                sampleCount));
            destination.Close();
        }

        private static byte[] CreatePcmBytes(
            int sampleCount,
            int seed)
        {
            byte[] bytes =
                new byte[sampleCount * AudioPCMConfig.RedBook.BlockAlign];
            int offset = 0;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    int value =
                        ((sample + seed) * 257 + channel * 8191) & 0xffff;
                    short signed = unchecked((short)(value - 32768));
                    bytes[offset++] = unchecked((byte)signed);
                    bytes[offset++] = unchecked((byte)(signed >> 8));
                }
            }
            return bytes;
        }

        private sealed class FaultingAudioSource : IAudioSource
        {
            private readonly IAudioSource _inner;
            private readonly string _phase;

            internal FaultingAudioSource(
                IAudioSource inner,
                string phase)
            {
                _inner = inner;
                _phase = phase;
            }

            public IAudioDecoderSettings Settings => _inner.Settings;
            public AudioPCMConfig PCM => _inner.PCM;
            public string Path => _inner.Path;
            public TimeSpan Duration => _inner.Duration;
            public long Length => _inner.Length;
            public long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }
            public long Remaining => _inner.Remaining;

            public int Read(AudioBuffer buffer, int maxLength)
            {
                if (_phase == "read")
                    throw new IOException(
                        "Injected decoder-read failure.");
                return _inner.Read(buffer, maxLength);
            }

            public void Close()
            {
                _inner.Close();
                if (_phase == "close")
                    throw new IOException(
                        "Injected decoder-close failure.");
            }
        }

        public sealed class FailingFinalizeEncoderSettings :
            IAudioEncoderSettings
        {
            public string Name => "finalization-failure";
            public string Extension => "failproof";
            public Type EncoderType =>
                typeof(FailingFinalizeEncoder);
            public bool Lossless => true;
            public int Priority => 0;
            public string SupportedModes => "default";
            public string DefaultMode => "default";
            public string EncoderMode { get; set; } = "default";
            public AudioPCMConfig PCM { get; set; }
            public int BlockSize { get; set; }
            public int Padding { get; set; }

            public IAudioEncoderSettings Clone()
            {
                return new FailingFinalizeEncoderSettings
                {
                    EncoderMode = EncoderMode,
                    PCM = PCM,
                    BlockSize = BlockSize,
                    Padding = Padding
                };
            }
        }

        public sealed class FailingFinalizeEncoder : IAudioDest
        {
            private FileStream _stream;
            private readonly string _path;

            public FailingFinalizeEncoder(
                IAudioEncoderSettings settings,
                string path,
                Stream output)
            {
                Settings = settings;
                _path = path;
                _stream = output as FileStream ??
                    new FileStream(
                        path,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read);
            }

            public IAudioEncoderSettings Settings { get; }
            public string Path => _path;
            public long FinalSampleCount { set { } }

            public void Write(AudioBuffer buffer)
            {
                _stream.Write(
                    buffer.Bytes,
                    0,
                    buffer.ByteLength);
            }

            public void Close()
            {
                _stream.Close();
                _stream = null;
                throw new IOException(
                    "Injected encoder-finalization failure.");
            }

            public void Delete()
            {
                if (_stream != null)
                {
                    _stream.Close();
                    _stream = null;
                }
                if (File.Exists(_path))
                    File.Delete(_path);
            }
        }
    }
}
