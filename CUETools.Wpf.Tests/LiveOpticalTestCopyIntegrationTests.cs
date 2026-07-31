#if OPTICAL_TESTCOPY_LIVE_PROBE
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CUETools.Codecs;
using CUETools.Processor;
using CUETools.Ripper.SCSI;
using CUETools.Wpf.Accuracy;
using CUETools.Wpf.Models;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Opt-in hardware/service evidence. Build with RunOpticalTestCopyProbe=true, set
    /// CUETOOLS_OPTICAL_TESTCOPY_DRIVE to the loaded drive, and set
    /// CUETOOLS_OPTICAL_TESTCOPY_OUTPUT_BASE to a unique path that does not exist. The probe owns
    /// that exact root, leaves it intact on failure, and removes it only after all evidence passes.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public sealed class LiveOpticalTestCopyIntegrationTests
    {
        private const string DriveEnvironmentVariable =
            "CUETOOLS_OPTICAL_TESTCOPY_DRIVE";
        private const string OutputEnvironmentVariable =
            "CUETOOLS_OPTICAL_TESTCOPY_OUTPUT_BASE";
        private const string SectorEnvironmentVariable =
            "CUETOOLS_OPTICAL_PROBE_SECTOR";
        private const string SpeedEnvironmentVariable =
            "CUETOOLS_OPTICAL_PROBE_SPEED_KBPS";
        private const string CacheBytesEnvironmentVariable =
            "CUETOOLS_OPTICAL_PROBE_CACHE_BYTES";
        private const string CorrectionQualityEnvironmentVariable =
            "CUETOOLS_OPTICAL_PROBE_CORRECTION_QUALITY";
        private const string DeepRecoveryEnvironmentVariable =
            "CUETOOLS_OPTICAL_PROBE_DEEP_RECOVERY";
        private const string ImageOutputEnvironmentVariable =
            "CUETOOLS_OPTICAL_IMAGE_OUTPUT_DIRECTORY";
        private const string OwnershipMarker = ".cuetools-live-testcopy-owner";

        [TestMethod]
        [TestCategory("Hardware")]
        public void LoadedDiscReportsReleasePreferenceEvidence()
        {
            char drive = ReadDrive();
            var log = new ProbeLog(TestContext);
            var service = new DriveService(App.CreateWpfDefaultConfig(), log);

            DiscInfo disc = service.ReadDisc(
                drive,
                status => TestContext.WriteLine("STATUS " + status));

            Assert.IsNotNull(disc, "The loaded audio disc could not be identified.");
            Assert.IsTrue(
                disc.Releases.Count > 0,
                "The loaded disc returned no metadata candidates.");

            foreach (ReleaseMatch release in disc.Releases)
            {
                CUEMetadata metadata = release.Metadata;
                TestContext.WriteLine(
                    $"RELEASE index={release.Index} best={release.IsBest} " +
                    $"source={release.Source} score={release.Score} " +
                    $"cover={release.HasCover} totalDiscs='{metadata?.TotalDiscs}' " +
                    $"discNumber='{metadata?.DiscNumber}' " +
                    $"discNameGeneric={IsGenericDiscName(metadata?.DiscName)} " +
                    $"artist={Digest(metadata?.Artist)} title={Digest(metadata?.Title)} " +
                    $"year={Digest(metadata?.Year)} barcode={Digest(metadata?.Barcode)} " +
                    $"trackTitles={DigestTracks(metadata, titles: true)} " +
                    $"trackArtists={DigestTracks(metadata, titles: false)}");
            }
        }

        [TestMethod]
        [TestCategory("Hardware")]
        public void PublishedImageHasEmbeddedCueReceiptAndRepairBinding()
        {
            string outputDirectory =
                Environment.GetEnvironmentVariable(
                    ImageOutputEnvironmentVariable) ?? "";
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(outputDirectory),
                $"Set {ImageOutputEnvironmentVariable} to the committed album directory.");
            outputDirectory = Path.GetFullPath(outputDirectory);
            Assert.IsTrue(
                Directory.Exists(outputDirectory),
                "The committed image-output directory does not exist.");

            string[] audioPaths = Directory.GetFiles(
                outputDirectory,
                "*.flac",
                SearchOption.TopDirectoryOnly);
            Assert.AreEqual(
                1,
                audioPaths.Length,
                "Image output must publish exactly one FLAC file.");
            string imagePath = audioPaths[0];

            string embeddedCue = Tagging.Analyze(imagePath).Get("CUESHEET");
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(embeddedCue),
                "The FLAC does not contain an embedded CUESHEET tag.");
            Assert.AreEqual(
                10,
                CountCueTracks(embeddedCue),
                "The embedded CUESHEET does not describe all ten physical tracks.");

            string[] cuePaths = Directory.GetFiles(
                outputDirectory,
                "*.cue",
                SearchOption.TopDirectoryOnly);
            Assert.AreEqual(
                1,
                cuePaths.Length,
                "The requested external cue sidecar is missing or ambiguous.");
            string externalCue = File.ReadAllText(cuePaths[0]);
            Assert.AreEqual(
                10,
                CountCueTracks(externalCue),
                "The external cue sidecar does not describe all ten physical tracks.");
            StringAssert.Contains(
                externalCue,
                $"FILE \"{Path.GetFileName(imagePath)}\" WAVE");

            string folderArtPath = Path.Combine(outputDirectory, "folder.jpg");
            byte[] folderArt = File.ReadAllBytes(folderArtPath);
            using (TagLib.File tagged = TagLib.File.Create(imagePath))
            {
                Assert.AreEqual(
                    1,
                    tagged.Tag.Pictures.Length,
                    "The image FLAC must contain exactly one selected cover.");
                CollectionAssert.AreEqual(
                    folderArt,
                    tagged.Tag.Pictures[0].Data.Data,
                    "The embedded cover differs from the selected published cover.");
            }

            string repairSource = RipService.FindRepairSourceRelativePath(
                outputDirectory,
                audioPaths);
            Assert.AreEqual(
                Path.GetFileName(cuePaths[0]),
                repairSource,
                ignoreCase: true,
                "Post-rip CTDB repair did not bind to the image's authoritative cue.");
            Assert.AreEqual(
                Path.GetFullPath(cuePaths[0]),
                RipService.RebindRepairSource(
                    outputDirectory,
                    repairSource),
                ignoreCase: true);

            string receiptPath = Path.Combine(outputDirectory, "rip.verify");
            VerifyRecord receipt = JsonSerializer.Deserialize<VerifyRecord>(
                File.ReadAllText(receiptPath));
            Assert.IsNotNull(receipt, "The rip.verify receipt did not deserialize.");
            Assert.AreEqual("flac", receipt.Format, ignoreCase: true);
            Assert.AreEqual(10, receipt.Tracks.Length);
            Assert.IsTrue(receipt.OutputVerificationKnown);
            Assert.IsTrue(receipt.LosslessOutput);
            Assert.IsTrue(
                receipt.OutputVerificationPerformed,
                receipt.OutputVerificationDetail);
            StringAssert.Contains(
                receipt.OutputVerificationDetail,
                "decoded and compared");

            var decoder = new CUETools.Codecs.Flake.AudioDecoder(
                new CUETools.Codecs.Flake.DecoderSettings(),
                imagePath);
            long decodedSamples = 0;
            try
            {
                Assert.AreEqual(16, decoder.PCM.BitsPerSample);
                Assert.AreEqual(2, decoder.PCM.ChannelCount);
                Assert.AreEqual(44100, decoder.PCM.SampleRate);
                var buffer = new AudioBuffer(decoder, 65536);
                int read;
                while ((read = decoder.Read(buffer, 65536)) > 0)
                    decodedSamples += read;
                Assert.AreEqual(
                    decoder.Length,
                    decodedSamples,
                    "The independent managed FLAC decoder did not reach the declared end.");
            }
            finally
            {
                decoder.Close();
            }

            TestContext.WriteLine(
                $"PASS image={Path.GetFileName(imagePath)} " +
                $"bytes={new FileInfo(imagePath).Length} " +
                $"samples={decodedSamples} tracks={receipt.Tracks.Length} " +
                $"coverBytes={folderArt.Length} " +
                $"repairSource={repairSource}");
        }

        [TestMethod]
        [TestCategory("Hardware")]
        public void LoadedDriveReadsConfiguredSectorWindow()
        {
            char drive = ReadDrive();
            Assert.IsTrue(
                int.TryParse(
                    Environment.GetEnvironmentVariable(
                        SectorEnvironmentVariable),
                    out int sector) &&
                sector >= 0,
                $"Set {SectorEnvironmentVariable} to a non-negative relative sector.");

            using var reader = new CDDriveReader();
            Assert.IsTrue(
                reader.Open(drive),
                "The loaded audio disc could not be opened.");
            int correctionQuality = 0;
            string correctionQualityText =
                Environment.GetEnvironmentVariable(
                    CorrectionQualityEnvironmentVariable) ?? "";
            if (!string.IsNullOrWhiteSpace(correctionQualityText))
            {
                Assert.IsTrue(
                    int.TryParse(
                        correctionQualityText,
                        out correctionQuality) &&
                    correctionQuality >= 0 &&
                    correctionQuality <= 2,
                    $"{CorrectionQualityEnvironmentVariable} must be 0, 1, or 2.");
            }
            reader.CorrectionQuality = correctionQuality;
            string deepRecoveryText =
                Environment.GetEnvironmentVariable(
                    DeepRecoveryEnvironmentVariable) ?? "";
            bool deepRecovery = false;
            if (!string.IsNullOrWhiteSpace(deepRecoveryText))
            {
                Assert.IsTrue(
                    bool.TryParse(deepRecoveryText, out deepRecovery),
                    $"{DeepRecoveryEnvironmentVariable} must be true or false.");
            }
            reader.DeepRecovery = deepRecovery;
            int cacheBytes = 0;
            string cacheBytesText =
                Environment.GetEnvironmentVariable(
                    CacheBytesEnvironmentVariable) ?? "";
            if (!string.IsNullOrWhiteSpace(cacheBytesText))
            {
                Assert.IsTrue(
                    int.TryParse(cacheBytesText, out cacheBytes) &&
                    cacheBytes > 0,
                    $"{CacheBytesEnvironmentVariable} must be a positive integer.");
                reader.SetCacheDefeat(cacheBytes);
            }
            string speedText =
                Environment.GetEnvironmentVariable(
                    SpeedEnvironmentVariable) ?? "";
            if (!string.IsNullOrWhiteSpace(speedText))
            {
                Assert.IsTrue(
                    int.TryParse(speedText, out int speedKbps) &&
                    speedKbps > 0,
                    $"{SpeedEnvironmentVariable} must be a positive integer.");
                reader.RequestReadSpeed(speedKbps);
            }
            reader.Position = (long)sector * 588;

            const int WindowSamples = 2400 * 588;
            var buffer = new AudioBuffer(
                AudioPCMConfig.RedBook,
                WindowSamples);
            int read = reader.Read(buffer, WindowSamples);
            Assert.IsTrue(
                read > 0,
                "The optical reader returned no samples for the configured window.");
            TestContext.WriteLine(
                $"PASS drive={drive}: relative-sector={sector} samples={read} " +
                $"cq={correctionQuality} cacheBytes={cacheBytes} " +
                $"deepRecovery={deepRecovery} " +
                $"readCommand='{reader.CurrentReadCommand}' " +
                $"readCommunicationRetries={reader.ReadCommunicationRetryCount} " +
                $"cacheRetries={reader.CacheDefeatRetryCount} " +
                $"cacheChunkFallbacks={reader.CacheDefeatChunkFallbackCount} " +
                $"cacheWakes={reader.CacheDefeatWakeCount} " +
                $"cacheWakeReadinessRetries=" +
                $"{reader.CacheDefeatWakeReadinessRetryCount} " +
                $"cacheWakeReadinessIndeterminate=" +
                $"{reader.CacheDefeatWakeReadinessIndeterminateCount}");
        }

        [TestMethod]
        [TestCategory("Hardware")]
        public void LoadedDriveCalibratesAndSustainsParanoidCacheDefeat()
        {
            char drive = ReadDrive();
            string outputRoot = ReadUniqueOutputRoot();
            string markerPath = Path.Combine(outputRoot, OwnershipMarker);
            string markerToken = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(outputRoot);
            File.WriteAllText(markerPath, markerToken);

            var log = new ProbeLog(TestContext);
            string calibrationPath =
                Path.Combine(outputRoot, "drive-calibration.json");
            var calibrationStore = new DriveCalibrationStore(calibrationPath);
            var calibrationService =
                new DriveCalibrationService(log, calibrationStore);

            DriveCalibration calibration = calibrationService.Calibrate(drive);
            Assert.IsNotNull(calibration, "The loaded drive did not calibrate.");
            Assert.IsTrue(
                DriveCalibrationService.IsCurrent(calibration),
                "Calibration did not use the current capability schema.");
            Assert.IsTrue(
                calibration.CacheDefeat == "Media re-reads (no cache)" ||
                DriveCalibrationService.ParseFlushBytes(
                    calibration.CacheDefeat) > 0,
                "Calibration did not establish an independent re-read strategy.");

            using (var reader = new CDDriveReader())
            {
                Assert.IsTrue(reader.Open(drive), "The loaded audio disc could not be opened.");
                int driveOffset = 0;
                bool driveOffsetKnown = false;
                try
                {
                    driveOffsetKnown =
                        CUETools.AccurateRip.AccurateRipVerify.FindDriveReadOffset(
                        reader.ARName,
                        out driveOffset);
                }
                catch
                {
                    // Calibration uses the same safe fallback. The cache-defeat proof
                    // still runs; edge consumption remains unavailable evidence.
                }
                reader.DriveOffset = driveOffset;
                reader.CorrectionQuality = 2;
                reader.DeepRecovery = false;
                bool applyOverread = DriveCalibrationService.CanApplyOverread(
                    calibration,
                    driveOffsetKnown,
                    driveOffset);
                reader.SetOverread(
                    applyOverread && calibration.OverreadLeadIn,
                    applyOverread && calibration.OverreadLeadOut);
                int flushBytes = DriveCalibrationService.ParseFlushBytes(
                    calibration.CacheDefeat);
                if (flushBytes > 0)
                    reader.SetCacheDefeat(flushBytes);

                // The reported regression failed 19 seconds into Paranoid Test & Copy. Exercise
                // twenty-five complete 2400-sector secure windows (~20% of a 70-minute disc),
                // including every cache-flush/long-seek/payload-read boundary, without writing.
                const int WindowSamples = 2400 * 588;
                long target = Math.Min(reader.Length, 25L * WindowSamples);
                var buffer = new AudioBuffer(AudioPCMConfig.RedBook, WindowSamples);
                while (reader.Position < target)
                {
                    int read = reader.Read(buffer, WindowSamples);
                    Assert.IsTrue(read > 0, "The optical reader ended before the smoke range.");
                }

                // Seek to the real end and consume the final offset-corrected samples.
                // This reaches calibrated lead-out for positive offsets; negative
                // offsets already reached calibrated lead-in at Position 0 above.
                reader.Position = Math.Max(0, reader.Length - 2L * 588);
                while (reader.Position < reader.Length)
                {
                    int read = reader.Read(buffer, WindowSamples);
                    Assert.IsTrue(
                        read > 0,
                        "The offset-corrected optical reader ended before the disc edge.");
                }
                TestContext.WriteLine(
                    $"PASS drive={drive}: paranoid cache-defeat smoke " +
                    $"samples={reader.Position} offset=" +
                    $"{(driveOffsetKnown ? driveOffset.ToString() : "unknown")} " +
                    $"flush={flushBytes} applyOverread={applyOverread} " +
                    $"overreadIn={calibration.OverreadLeadIn} " +
                    $"overreadOut={calibration.OverreadLeadOut}");
            }

            DeleteOwnedOutputRoot(outputRoot, markerPath, markerToken);
            Assert.IsFalse(Directory.Exists(outputRoot));
        }

        [TestMethod]
        [TestCategory("Hardware")]
        public void LoadedDiscPassesRealFlacTestAndCopyWithEncodedOutputAssurance()
        {
            char drive = ReadDrive();
            string outputRoot = ReadUniqueOutputRoot();
            string markerPath = Path.Combine(outputRoot, OwnershipMarker);
            string markerToken = Guid.NewGuid().ToString("N");

            Directory.CreateDirectory(outputRoot);
            File.WriteAllText(markerPath, markerToken);

            var log = new ProbeLog(TestContext);
            var settings = new AppSettings
            {
                AdaptiveReadSpeed = true,
                DeepRecovery = true,
                LockTrayDuringRip = false,
                PreventSleepDuringRip = true,
                StopOnUnrecoverable = true,
                OutputBaseDir = outputRoot,
                SelectedFormat = "flac",
                CorrectionQuality = 1,
            };

            EnsureFlakePluginRegistered();
            var config = new CUEConfig
            {
                // This probe never asks the engine or drive layer to open the tray.
                ejectAfterRip = false,
                disableEjectDisc = false,
                CopyAlbumArt = false,
                embedAlbumArt = false,
                extractAlbumArt = false,
            };

            var flake = config.Encoders.FirstOrDefault(
                encoder => encoder.Settings is CUETools.Codecs.Flake.EncoderSettings);
            Assert.IsNotNull(flake,
                "The real managed Flake encoder was not registered in CUEConfig.");
            ((CUETools.Codecs.Flake.EncoderSettings)flake.Settings).DoVerify = true;
            config.formats["flac"].encoderLossless = flake;

            var catalog = new EncoderCatalog(
                log, settings, Path.Combine(outputRoot, "encoders"));
            catalog.EnsureRegistered(config);
            Assert.IsTrue(catalog.IsUsable(flake),
                "The configured in-process FLAC encoder is not usable.");

            string calibrationPath =
                Path.Combine(outputRoot, "drive-calibration.json");
            var calibrationStore = new DriveCalibrationStore(calibrationPath);
            var history = new VerifyHistoryStore(
                Path.Combine(outputRoot, "verify-history.json.gz"));
            var calibrationService =
                new DriveCalibrationService(log, calibrationStore);
            var rip = new RipService(
                config,
                log,
                settings,
                catalog,
                calibrationStore,
                history,
                calibrationService);

            TestCopyRunResult result = rip.RunTestAndCopy(
                drive,
                cq: 1,
                format: "flac",
                metadata: null,
                outputBaseDir: outputRoot,
                onProgress: (fraction, message) =>
                    TestContext.WriteLine(
                        $"{fraction:P0} {StripPath(message, outputRoot)}"));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(TestCopyOutcome.Passed, result.Outcome,
                "The loaded disc did not produce a committed Test & Copy result.");
            Assert.IsTrue(result.ReadsUsed >= 2,
                "Test & Copy did not report at least two independent reads.");
            Assert.IsTrue(File.Exists(calibrationPath),
                "The isolated real-stack run did not persist its drive calibration.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.OutputDir));
            AssertPathIsWithinOwnedRoot(outputRoot, result.OutputDir);
            Assert.IsTrue(Directory.Exists(result.OutputDir));
            Assert.IsTrue(result.FileCount > 0,
                "Test & Copy committed no audio files.");
            Assert.IsTrue(
                Directory.EnumerateFiles(
                    result.OutputDir, "*.flac", SearchOption.AllDirectories).Any(),
                "The committed album contains no FLAC files.");

            Assert.AreEqual("flac", result.Format,
                ignoreCase: true);
            Assert.IsTrue(result.OutputVerificationKnown,
                "The result omitted its encoded-output assurance contract.");
            Assert.IsTrue(result.LosslessOutput);
            Assert.IsTrue(result.OutputVerificationPerformed,
                result.OutputVerificationDetail);
            StringAssert.Contains(
                result.OutputVerificationDetail,
                "decoded and compared");

            string receiptPath = Path.Combine(result.OutputDir, "rip.verify");
            Assert.IsTrue(File.Exists(receiptPath),
                "The committed album has no rip.verify receipt.");
            VerifyRecord receipt = JsonSerializer.Deserialize<VerifyRecord>(
                File.ReadAllText(receiptPath));
            Assert.IsNotNull(receipt,
                "The rip.verify receipt did not deserialize.");
            Assert.AreEqual("flac", receipt.Format,
                ignoreCase: true);
            Assert.IsTrue(receipt.OutputVerificationKnown,
                "rip.verify omitted its encoded-output assurance contract.");
            Assert.IsTrue(receipt.LosslessOutput);
            Assert.IsTrue(receipt.OutputVerificationPerformed,
                receipt.OutputVerificationDetail);
            StringAssert.Contains(
                receipt.OutputVerificationDetail,
                "decoded and compared");
            Assert.AreEqual(
                result.OutputVerificationDetail,
                receipt.OutputVerificationDetail);

            TestContext.WriteLine(
                $"PASS drive={drive}: reads={result.ReadsUsed} " +
                $"files={result.FileCount} outputVerification=performed");

            DeleteOwnedOutputRoot(outputRoot, markerPath, markerToken);
            Assert.IsFalse(Directory.Exists(outputRoot),
                "The validated test-owned output root was not removed.");
        }

        public TestContext TestContext { get; set; }

        private static void EnsureFlakePluginRegistered()
        {
            lock (CUEProcessorPlugins.encs)
            {
                if (!CUEProcessorPlugins.encs.Any(
                        codec => codec is CUETools.Codecs.Flake.EncoderSettings))
                    CUEProcessorPlugins.encs.Add(
                        new CUETools.Codecs.Flake.EncoderSettings());
            }
            lock (CUEProcessorPlugins.decs)
            {
                if (!CUEProcessorPlugins.decs.Any(
                        codec => codec is CUETools.Codecs.Flake.DecoderSettings))
                    CUEProcessorPlugins.decs.Add(
                        new CUETools.Codecs.Flake.DecoderSettings());
            }
        }

        private static char ReadDrive()
        {
            string value = (
                Environment.GetEnvironmentVariable(DriveEnvironmentVariable) ??
                "").Trim().TrimEnd('\\', '/');
            if (value.EndsWith(":", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);
            Assert.AreEqual(
                1,
                value.Length,
                $"Set {DriveEnvironmentVariable} to one drive letter.");
            char drive = char.ToUpperInvariant(value[0]);
            Assert.IsTrue(
                drive >= 'A' && drive <= 'Z',
                $"{DriveEnvironmentVariable} is not a drive letter.");
            Assert.AreEqual(
                DriveType.CDRom,
                new DriveInfo(drive + @":\").DriveType,
                $"{drive}: is not an optical drive.");
            return drive;
        }

        private static string ReadUniqueOutputRoot()
        {
            string configured =
                Environment.GetEnvironmentVariable(OutputEnvironmentVariable) ??
                "";
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(configured),
                $"Set {OutputEnvironmentVariable} to a unique path that does not exist.");

            string root = Path.GetFullPath(configured).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string volumeRoot = Path.GetPathRoot(root)?.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) ?? "";
            Assert.IsFalse(
                string.Equals(root, volumeRoot, StringComparison.OrdinalIgnoreCase),
                "The output root must not be a volume root.");
            Assert.IsFalse(
                File.Exists(root) || Directory.Exists(root),
                "The unique output root already exists. Use a new path so the test cannot delete pre-existing data.");
            return root;
        }

        private static void AssertPathIsWithinOwnedRoot(
            string outputRoot,
            string candidate)
        {
            string rootWithSeparator = Path.GetFullPath(outputRoot).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullCandidate = Path.GetFullPath(candidate);
            Assert.IsTrue(
                fullCandidate.StartsWith(
                    rootWithSeparator,
                    StringComparison.OrdinalIgnoreCase),
                "The committed output escaped the test-owned root.");
        }

        private static void DeleteOwnedOutputRoot(
            string outputRoot,
            string markerPath,
            string markerToken)
        {
            Assert.AreEqual(
                markerToken,
                File.ReadAllText(markerPath),
                "The test ownership marker changed; refusing recursive cleanup.");
            Assert.IsFalse(
                (File.GetAttributes(outputRoot) & FileAttributes.ReparsePoint) != 0,
                "The test-owned root became a reparse point; refusing recursive cleanup.");
            Directory.Delete(outputRoot, recursive: true);
        }

        private static string StripPath(string message, string outputRoot) =>
            (message ?? "").Replace(
                outputRoot,
                "<test-output>",
                StringComparison.OrdinalIgnoreCase);

        private static bool IsGenericDiscName(string value)
        {
            string name = (value ?? "").Trim();
            if (name.Length == 0)
                return true;
            return name.StartsWith("Disc ", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(name.Substring(5).Trim(), out _);
        }

        private static string DigestTracks(CUEMetadata metadata, bool titles)
        {
            if (metadata?.Tracks == null)
                return Digest("");
            return Digest(string.Join(
                "\n",
                metadata.Tracks.Select(
                    track => titles ? track?.Title ?? "" : track?.Artist ?? "")));
        }

        private static string Digest(string value)
        {
            string normalized = string.Join(
                " ",
                (value ?? "").Trim().ToUpperInvariant().Split(
                    (char[])null,
                    StringSplitOptions.RemoveEmptyEntries));
            if (normalized.Length == 0)
                return "empty";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(hash, 0, 6);
        }

        private static int CountCueTracks(string cueContents) =>
            cueContents.Split(
                    new[] { "\r\n", "\n" },
                    StringSplitOptions.None)
                .Count(line =>
                    line.TrimStart().StartsWith(
                        "TRACK ",
                        StringComparison.OrdinalIgnoreCase));

        private sealed class ProbeLog : IDiagnosticLog
        {
            private readonly TestContext _context;

            public ProbeLog(TestContext context)
            {
                _context = context;
            }

            public string LogPath => "";

            public void Info(string category, string message) =>
                _context.WriteLine($"INFO {category}: {message}");

            public void Warn(string category, string message) =>
                _context.WriteLine($"WARN {category}: {message}");

            public void Error(
                string category,
                string message,
                Exception ex = null) =>
                _context.WriteLine(
                    $"ERROR {category}: {message} ({ex?.GetType().Name})");

            public void Redact(params string[] sensitive)
            {
            }
        }
    }
}
#endif
