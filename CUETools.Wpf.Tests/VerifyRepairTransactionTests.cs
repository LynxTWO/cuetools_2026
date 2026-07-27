using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class VerifyRepairTransactionTests
{
    [TestMethod]
    public void Repair_PublishesVerifiedSibling_AndLeavesOriginalUnchanged()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteSource("album.cue", "ORIGINAL CUE");
        var engine = new FakeRepairEngine();
        var service = CreateService(new CUEConfig(), engine);

        VerifyFilesResult result = service.Repair(source, (_, _) => { });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.IsTrue(result.RepairApplied);
        Assert.AreEqual("ORIGINAL CUE", File.ReadAllText(source));
        Assert.IsTrue(Directory.Exists(result.OutputPath));
        Assert.AreEqual(
            "repaired audio",
            File.ReadAllText(Path.Combine(result.OutputPath, "album.flac")));
        Assert.AreEqual(
            "repaired cue",
            File.ReadAllText(Path.Combine(result.OutputPath, "album.cue")));
        Assert.IsFalse(File.Exists(Path.Combine(
            result.OutputPath, RepairWorkspace.OwnershipMarkerName)));
        Assert.AreEqual(1, engine.VerifyCalls);
        Assert.AreEqual(0, temp.StagingDirectories().Length);
    }

    [TestMethod]
    public void Repair_PublishCollision_UsesUniqueSiblingWithoutTouchingExistingDirectory()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteSource("album.cue", "ORIGINAL");
        string collision = Path.Combine(temp.Path, "album - repaired");
        Directory.CreateDirectory(collision);
        File.WriteAllText(Path.Combine(collision, "keep.txt"), "KEEP");
        var service = CreateService(new CUEConfig(), new FakeRepairEngine());

        VerifyFilesResult result = service.Repair(source, (_, _) => { });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual("album - repaired (2)", Path.GetFileName(result.OutputPath));
        Assert.AreEqual("KEEP", File.ReadAllText(Path.Combine(collision, "keep.txt")));
        Assert.AreEqual("ORIGINAL", File.ReadAllText(source));
    }

    [TestMethod]
    public void Repair_CompletionCallbackFailure_DoesNotMisreportCommittedPublication()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteSource("album.cue", "ORIGINAL");
        var service = CreateService(new CUEConfig(), new FakeRepairEngine());

        VerifyFilesResult result = service.Repair(
            source,
            (progress, _) =>
            {
                if (progress == 1)
                    throw new InvalidOperationException("simulated UI callback failure");
            });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.IsTrue(Directory.Exists(result.OutputPath));
        Assert.AreEqual("ORIGINAL", File.ReadAllText(source));
    }

    [TestMethod]
    public void Repair_EngineFailure_RemovesOnlyOwnedStageAndLeavesOriginalUnchanged()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteSource("album.cue", "ORIGINAL");
        var engine = new FakeRepairEngine { ThrowAfterWriting = true };
        var service = CreateService(new CUEConfig(), engine);

        VerifyFilesResult result = service.Repair(source, (_, _) => { });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "simulated repair failure");
        Assert.AreEqual("ORIGINAL", File.ReadAllText(source));
        Assert.AreEqual(0, temp.StagingDirectories().Length);
        Assert.IsFalse(Directory.Exists(Path.Combine(temp.Path, "album - repaired")));
    }

    [TestMethod]
    public void Repair_RejectsUnappliedRepairAndEmptyOutputBeforeVerification()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteSource("album.cue", "ORIGINAL");
        var unapplied = new FakeRepairEngine { Applied = false };
        VerifyFilesResult unappliedResult =
            CreateService(new CUEConfig(), unapplied).Repair(source, (_, _) => { });

        Assert.IsFalse(unappliedResult.Ok);
        Assert.AreEqual(0, unapplied.VerifyCalls);
        Assert.AreEqual(0, temp.StagingDirectories().Length);

        var empty = new FakeRepairEngine { EmptyAudio = true };
        VerifyFilesResult emptyResult =
            CreateService(new CUEConfig(), empty).Repair(source, (_, _) => { });

        Assert.IsFalse(emptyResult.Ok);
        StringAssert.Contains(emptyResult.Error, "repaired audio is empty");
        Assert.AreEqual(0, empty.VerifyCalls);
        Assert.AreEqual("ORIGINAL", File.ReadAllText(source));
        Assert.AreEqual(0, temp.StagingDirectories().Length);
    }

    [TestMethod]
    public void Repair_VerificationFailure_RollsBackStageAndDoesNotPublish()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteSource("album.cue", "ORIGINAL");
        var engine = new FakeRepairEngine { VerificationSucceeds = false };
        var service = CreateService(new CUEConfig(), engine);

        VerifyFilesResult result = service.Repair(source, (_, _) => { });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "could not be independently verified");
        Assert.AreEqual(1, engine.VerifyCalls);
        Assert.AreEqual("ORIGINAL", File.ReadAllText(source));
        Assert.AreEqual(0, temp.StagingDirectories().Length);
        Assert.IsFalse(Directory.Exists(Path.Combine(temp.Path, "album - repaired")));
    }

    [TestMethod]
    public void Repair_OutputEscapeIsRejected_AndExternalFileIsNeverDeleted()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteSource("album.cue", "ORIGINAL");
        string sentinel = temp.WriteSource("outside.flac", "DO NOT DELETE");
        var engine = new FakeRepairEngine { ExpectedAudioOverride = sentinel };
        var service = CreateService(new CUEConfig(), engine);

        VerifyFilesResult result = service.Repair(source, (_, _) => { });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "escaped the repair staging directory");
        Assert.AreEqual("DO NOT DELETE", File.ReadAllText(sentinel));
        Assert.AreEqual("ORIGINAL", File.ReadAllText(source));
        Assert.AreEqual(0, temp.StagingDirectories().Length);
    }

    [TestMethod]
    public void Repair_UsesIndependentConfigClonesWithAllVerifyWritesDisabled()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteSource("album.cue", "ORIGINAL");
        var config = new CUEConfig
        {
            writeArTagsOnVerify = true,
            writeArLogOnVerify = true,
            arLogToSourceFolder = true,
            createCUEFileInTracksMode = false,
            createCUEFileWhenEmbedded = false
        };
        config.advanced.WriteCTDBTagsOnVerify = true;
        config.advanced.CreateTOC = true;
        config.writeArLogOnConvert = true;
        config.keepOriginalFilenames = true;
        config.trackFilenameFormat = @"..\escape\%tracknumber%";
        config.singleFilenameFormat = @"..\escape\album";
        config.extractAlbumArt = true;
        config.extractLog = true;
        config.createM3U = true;
        config.writeBasicTagsFromCUEData = false;
        config.copyBasicTags = false;
        config.copyUnknownTags = false;
        config.CopyAlbumArt = false;
        config.embedAlbumArt = false;
        var engine = new FakeRepairEngine();
        var service = CreateService(config, engine);

        VerifyFilesResult result = service.Repair(source, (_, _) => { });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(2, engine.Configs.Count);
        foreach (CUEConfig used in engine.Configs)
        {
            Assert.AreNotSame(config, used);
            Assert.IsFalse(used.writeArTagsOnVerify);
            Assert.IsFalse(used.writeArLogOnVerify);
            Assert.IsFalse(used.writeArLogOnConvert);
            Assert.IsFalse(used.arLogToSourceFolder);
            Assert.IsTrue(used.keepOriginalFilenames);
            Assert.AreEqual("%tracknumber%", used.trackFilenameFormat);
            Assert.AreEqual("album", used.singleFilenameFormat);
            Assert.IsTrue(used.writeBasicTagsFromCUEData);
            Assert.IsTrue(used.copyBasicTags);
            Assert.IsTrue(used.copyUnknownTags);
            Assert.IsTrue(used.CopyAlbumArt);
            Assert.IsFalse(used.embedAlbumArt);
            Assert.IsFalse(used.extractAlbumArt);
            Assert.IsFalse(used.extractLog);
            Assert.IsFalse(used.createM3U);
            Assert.IsFalse(used.advanced.WriteCTDBTagsOnVerify);
            Assert.IsFalse(used.advanced.CreateTOC);
            Assert.IsTrue(used.createCUEFileInTracksMode);
            Assert.IsTrue(used.createCUEFileWhenEmbedded);
        }
        Assert.AreNotSame(engine.Configs[0], engine.Configs[1]);

        // The persisted/shared settings object is not modified by a repair run.
        Assert.IsTrue(config.writeArTagsOnVerify);
        Assert.IsTrue(config.writeArLogOnVerify);
        Assert.IsTrue(config.writeArLogOnConvert);
        Assert.IsTrue(config.arLogToSourceFolder);
        Assert.IsTrue(config.keepOriginalFilenames);
        Assert.AreEqual(@"..\escape\%tracknumber%", config.trackFilenameFormat);
        Assert.AreEqual(@"..\escape\album", config.singleFilenameFormat);
        Assert.IsTrue(config.extractAlbumArt);
        Assert.IsTrue(config.extractLog);
        Assert.IsTrue(config.createM3U);
        Assert.IsFalse(config.writeBasicTagsFromCUEData);
        Assert.IsFalse(config.copyBasicTags);
        Assert.IsFalse(config.copyUnknownTags);
        Assert.IsFalse(config.CopyAlbumArt);
        Assert.IsFalse(config.embedAlbumArt);
        Assert.IsTrue(config.advanced.WriteCTDBTagsOnVerify);
        Assert.IsTrue(config.advanced.CreateTOC);
        Assert.IsFalse(config.createCUEFileInTracksMode);
        Assert.IsFalse(config.createCUEFileWhenEmbedded);
    }

    [TestMethod]
    public void Repair_GeneratedDestinationGuardRejectsTraversalBeforeEngineWrites()
    {
        using var temp = new TempDirectory();
        string stage = Path.Combine(temp.Path, "stage");
        Directory.CreateDirectory(stage);
        string outside = Path.Combine(temp.Path, "outside.flac");

        InvalidOperationException error = Assert.ThrowsException<InvalidOperationException>(
            () => RepairWorkspace.RequireContainedPath(stage, outside, "repaired audio"));

        StringAssert.Contains(error.Message, "escaped the repair staging directory");
        RepairWorkspace.RequireContainedPath(
            stage,
            Path.Combine(stage, "track.flac"),
            "repaired audio");
    }

    [TestMethod]
    public void Repair_DuplicatePreservedFlacNameIsRejectedBeforeEngineWrites()
    {
        using var temp = new TempDirectory();
        string first = Path.Combine(temp.Path, "same.flac");
        string second = Path.Combine(temp.Path, "SAME.flac");

        InvalidOperationException error =
            Assert.ThrowsException<InvalidOperationException>(
                () => RepairWorkspace.RequireDistinctAudioPaths(
                    new[] { first, second }));

        StringAssert.Contains(
            error.Message,
            "collide after conversion to repaired FLAC names");
    }

    [TestMethod]
    public void Repair_RejectsFolderInputBeforeCreatingAWorkspace()
    {
        using var temp = new TempDirectory();
        var engine = new FakeRepairEngine();
        var service = CreateService(new CUEConfig(), engine);

        VerifyFilesResult result = service.Repair(temp.Path, (_, _) => { });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "folder input is not supported");
        Assert.AreEqual(0, engine.RepairCalls);
        Assert.AreEqual(0, temp.StagingDirectories().Length);
    }

    [TestMethod]
    public void RepairWorkspace_RefusesToDeleteAReplacedDirectory()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteSource("album.cue", "ORIGINAL");
        RepairWorkspace workspace = RepairWorkspace.Create(source);
        string stage = workspace.StagingDirectory;

        Directory.Delete(stage, recursive: true);
        Directory.CreateDirectory(stage);
        string sentinel = Path.Combine(stage, "do-not-delete.txt");
        File.WriteAllText(sentinel, "KEEP");

        InvalidOperationException error = Assert.ThrowsException<InvalidOperationException>(
            () => workspace.Dispose());

        StringAssert.Contains(error.Message, "ownership could not be proven");
        Assert.AreEqual("KEEP", File.ReadAllText(sentinel));
    }

    private static VerifyService CreateService(CUEConfig config, IRepairEngine engine)
        => new(config, new FakeLog(), engine);

    private sealed class FakeRepairEngine : IRepairEngine
    {
        public bool Applied { get; init; } = true;
        public bool EmptyAudio { get; init; }
        public bool ThrowAfterWriting { get; init; }
        public bool VerificationSucceeds { get; init; } = true;
        public string ExpectedAudioOverride { get; init; }
        public int RepairCalls { get; private set; }
        public int VerifyCalls { get; private set; }
        public List<CUEConfig> Configs { get; } = new();

        public RepairEngineResult Repair(
            string sourcePath,
            string stagingDirectory,
            CUEConfig config,
            Action<double, string> onProgress)
        {
            RepairCalls++;
            Configs.Add(config);
            string cue = Path.Combine(stagingDirectory, "album.cue");
            string audio = Path.Combine(stagingDirectory, "album.flac");
            File.WriteAllText(cue, "repaired cue");
            File.WriteAllText(audio, EmptyAudio ? "" : "repaired audio");
            if (ThrowAfterWriting)
                throw new InvalidOperationException("simulated repair failure");

            return new RepairEngineResult
            {
                Applied = Applied,
                StagedCuePath = cue,
                ExpectedAudioPaths = new[] { ExpectedAudioOverride ?? audio },
                Result = new VerifyFilesResult
                {
                    Ok = true,
                    Artist = "artist",
                    Album = "album",
                    TrackCount = 1,
                    RepairApplied = Applied,
                    RepairSamples = 12,
                    RepairSectors = 1,
                    RepairNpar = 4
                }
            };
        }

        public RepairVerificationResult Verify(
            string stagedCuePath,
            CUEConfig config,
            Action<double, string> onProgress)
        {
            VerifyCalls++;
            Configs.Add(config);
            return new RepairVerificationResult
            {
                Verified = VerificationSucceeds,
                Result = new VerifyFilesResult
                {
                    Ok = true,
                    Artist = "artist",
                    Album = "album",
                    TrackCount = 1,
                    CtdbConfidence = VerificationSucceeds ? 5 : 0,
                    CtdbTotal = 5
                }
            };
        }
    }

    private sealed class FakeLog : IDiagnosticLog
    {
        public string LogPath => "";
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message, Exception ex = null) { }
        public void Redact(params string[] sensitive) { }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cuetools-repair-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string WriteSource(string name, string contents)
        {
            string path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, contents);
            return path;
        }

        public string[] StagingDirectories()
            => Directory.GetDirectories(Path, ".cuetools-repair-*.staging");

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
