using System;
using System.IO;
using System.Linq;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class VerificationSourceDiscoveryTests
{
    [TestMethod]
    public void NullEmptyInvalidAndMissingSelectionsFailClearly()
    {
        var discovery = new VerificationSourceDiscovery();

        Assert.ThrowsException<ArgumentNullException>(() => discovery.Discover(null));
        StringAssert.Contains(discovery.Discover(Array.Empty<string>()).Error, "Drop one album folder");
        StringAssert.Contains(discovery.Discover(new[] { "invalid\0path" }).Error, "invalid path");
        StringAssert.Contains(
            discovery.Discover(new[] { Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cue") }).Error,
            "no longer exist");
    }

    [TestMethod]
    public void MixedExistingAndMissingSelectionFailsIfAnyEntryDisappeared()
    {
        using var folder = new TestFolder();
        string existing = Path.Combine(folder.Path, "existing.cue");
        File.WriteAllText(existing, "FILE \"audio.flac\" WAVE\n");
        string missing = Path.Combine(folder.Path, "missing.cue");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { existing, missing });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "no longer exist");
    }

    [TestMethod]
    public void DiscoveryResultDefaultsToAnEmptyFailure()
    {
        var result = new VerificationSourceDiscoveryResult();

        Assert.IsFalse(result.Ok);
        Assert.IsNull(result.SourceSet);
        Assert.AreEqual("", result.Error);
    }

    [TestMethod]
    public void OversizedExplicitSelectionIsRejectedBeforeFilesystemWork()
    {
        using var folder = new TestFolder();
        string[] paths = Enumerable.Range(0, 2049)
            .Select(index => Path.Combine(folder.Path, index + ".flac"))
            .ToArray();

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(paths);

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "more than 2048 files");
    }

    [TestMethod]
    public void MaximumExplicitSelectionIsAcceptedByTheSafetyBoundary()
    {
        using var folder = new TestFolder();
        string[] paths = Enumerable.Range(0, 2048)
            .Select(index => Path.Combine(folder.Path, index + ".unsupported"))
            .ToArray();
        foreach (string path in paths)
            File.WriteAllBytes(path, Array.Empty<byte>());

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(paths);

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "No CUE sheet");
        Assert.IsFalse(result.Error.Contains("more than 2048", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SeveralFoldersAreRejectedWithoutGuessingScope()
    {
        using var first = new TestFolder();
        using var second = new TestFolder();

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { first.Path, second.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "one album folder at a time");
    }

    [TestMethod]
    public void AlbumFolderFindsAndOrdersNestedDiscCueSheets()
    {
        using var folder = new TestFolder();
        string disc2 = folder.Directory("Disc 02 - Bonus");
        string disc1 = folder.Directory("CD1");
        WriteCue(disc2, "bonus.cue", 2, 2, "bonus.flac");
        WriteCue(disc1, "main.cue", 1, 2, "main.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.IsNotNull(result.SourceSet);
        Assert.AreEqual(2, result.SourceSet.Discs.Count);
        Assert.AreEqual(1, result.SourceSet.Discs[0].DiscNumber);
        Assert.AreEqual(2, result.SourceSet.Discs[1].DiscNumber);
        Assert.AreEqual(2, result.SourceSet.Discs[0].TotalDiscs);
        StringAssert.Contains(result.SourceSet.Discs[0].RelativePath, "CD1");
        StringAssert.Contains(result.SourceSet.Discs[1].RelativePath, "Disc 02");
    }

    [TestMethod]
    public void CueSheetsTakePrecedenceOverPlaylistsAndLooseAudio()
    {
        using var folder = new TestFolder();
        WriteCue(folder.Path, "album.cue", 1, 1, "01.flac");
        File.WriteAllText(Path.Combine(folder.Path, "album.m3u"), "01.flac\n");
        File.WriteAllBytes(Path.Combine(folder.Path, "01.flac"), new byte[] { 1 });

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(1, result.SourceSet!.Discs.Count);
        Assert.AreEqual(
            VerificationSourceKind.CueSheet,
            result.SourceSet.Discs[0].Kind);
    }

    [TestMethod]
    public void PlaylistsIgnoreCommentsAndRemoteUrisWhenCheckingOverlap()
    {
        using var folder = new TestFolder();
        File.WriteAllText(
            Path.Combine(folder.Path, "Disc 1.m3u8"),
            "#EXTM3U\nhttps://example.invalid/cover\nfirst.flac\n");
        File.WriteAllText(
            Path.Combine(folder.Path, "Disc 2.m3u8"),
            "#EXTM3U\nhttps://example.invalid/cover\nsecond.flac\n");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(2, result.SourceSet!.Discs.Count);
        Assert.IsTrue(result.SourceSet.Discs.All(
            source => source.Kind == VerificationSourceKind.Playlist));
    }

    [TestMethod]
    public void FileUrisStillParticipateInPlaylistOverlapDetection()
    {
        using var folder = new TestFolder();
        string audio = Path.Combine(folder.Path, "shared.flac");
        string reference = new Uri(audio).AbsoluteUri;
        File.WriteAllText(Path.Combine(folder.Path, "Disc 1.m3u"), reference + "\n");
        File.WriteAllText(Path.Combine(folder.Path, "Disc 2.m3u"), reference + "\n");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "same audio");
    }

    [TestMethod]
    public void FileUriAndRelativePathToTheSameAudioOverlap()
    {
        using var folder = new TestFolder();
        string audio = Path.Combine(folder.Path, "shared.flac");
        File.WriteAllText(
            Path.Combine(folder.Path, "Disc 1.m3u"),
            new Uri(audio).AbsoluteUri + "\n");
        File.WriteAllText(
            Path.Combine(folder.Path, "Disc 2.m3u"),
            "shared.flac\n");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "same audio");
    }

    [TestMethod]
    public void PlaylistBlankLinesAreIgnored()
    {
        using var folder = new TestFolder();
        File.WriteAllText(
            Path.Combine(folder.Path, "album.m3u"),
            "\n# comment\ntrack.flac\n\n");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(VerificationSourceKind.Playlist, result.SourceSet!.Discs[0].Kind);
    }

    [TestMethod]
    public void CueDirectivesAreCaseInsensitive()
    {
        using var folder = new TestFolder();
        File.WriteAllText(
            Path.Combine(folder.Path, "alpha.cue"),
            "rem discnumber 1\nrem disctotal 2\nfile \"first.flac\" WAVE\n");
        File.WriteAllText(
            Path.Combine(folder.Path, "beta.cue"),
            "rem disc 2\nrem totaldiscs 2\nfile \"second.flac\" WAVE\n");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            result.SourceSet!.Discs.Select(source => source.DiscNumber).ToArray());
    }

    [TestMethod]
    public void LowercaseTotalDiscDirectiveSurvivesOnASingleManifest()
    {
        using var folder = new TestFolder();
        File.WriteAllText(
            Path.Combine(folder.Path, "album.cue"),
            "rem discnumber 1\nrem disctotal 2\nfile \"album.flac\" WAVE\n");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(2, result.SourceSet!.Discs[0].TotalDiscs);
    }

    [TestMethod]
    public void LowercaseCueFileDirectivesStillDetectOverlap()
    {
        using var folder = new TestFolder();
        File.WriteAllText(Path.Combine(folder.Path, "Disc 1.cue"), "file \"same.flac\" WAVE\n");
        File.WriteAllText(Path.Combine(folder.Path, "Disc 2.cue"), "file \"same.flac\" WAVE\n");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "same audio");
    }

    [TestMethod]
    public void OverlappingCueSheetsAreRejectedInsteadOfDoubleVerifyingAudio()
    {
        using var folder = new TestFolder();
        WriteCue(folder.Path, "album.cue", 1, 1, "01.flac");
        WriteCue(folder.Path, "copy.cue", 1, 1, "01.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "same audio");
        StringAssert.Contains(result.Error, "does not have to guess");
    }

    [TestMethod]
    public void AbsoluteAudioReferencesStillParticipateInOverlapDetection()
    {
        using var folder = new TestFolder();
        string audio = Path.Combine(folder.Path, "01.flac");
        WriteCue(folder.Path, "album.cue", 1, 1, audio);
        WriteCue(folder.Path, "copy.cue", 1, 1, audio);

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "same audio");
    }

    [TestMethod]
    public void DistinctCueSheetsInOneFolderRemainSeparateDiscs()
    {
        using var folder = new TestFolder();
        WriteCue(folder.Path, "Set - Disc 2.cue", 2, 2, "disc2.flac");
        WriteCue(folder.Path, "Set - Disc 1.cue", 1, 2, "disc1.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            result.SourceSet!.Discs.Select(source => source.DiscNumber).ToArray());
    }

    [TestMethod]
    public void ImplausiblyLargeManifestSetIsRejectedBeforeAlbumWorkStarts()
    {
        using var folder = new TestFolder();
        for (int disc = 1; disc <= 101; disc++)
            WriteCue(folder.Path, $"Disc {disc}.cue", disc, 101, $"disc-{disc}.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "more than 100 disc manifests");
    }

    [TestMethod]
    public void MaximumDiscManifestSetIsStillACompleteAlbum()
    {
        using var folder = new TestFolder();
        for (int disc = 1; disc <= 100; disc++)
            WriteCue(folder.Path, $"Disc {disc}.cue", disc, 100, $"disc-{disc}.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(100, result.SourceSet!.Discs.Count);
        Assert.AreEqual(1, result.SourceSet.Discs[0].DiscNumber);
        Assert.AreEqual(100, result.SourceSet.Discs[99].DiscNumber);
    }

    [TestMethod]
    public void DiscNumbersCanBeInferredFromNestedFolderNames()
    {
        using var folder = new TestFolder();
        string disc1 = folder.Directory("CD1");
        string disc2 = folder.Directory("Disc 02 - Bonus");
        WriteCue(disc1, "album.cue", 0, 0, "first.flac");
        WriteCue(disc2, "album.cue", 0, 0, "second.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            result.SourceSet!.Discs.Select(source => source.DiscNumber).ToArray());
        CollectionAssert.AreEqual(
            new[] { 2, 2 },
            result.SourceSet.Discs.Select(source => source.TotalDiscs).ToArray());
        StringAssert.StartsWith(result.SourceSet.Discs[0].DisplayName, "Disc 1:");
    }

    [TestMethod]
    public void DiscNumberCanBeInferredFromTheSecondParentDirectory()
    {
        using var folder = new TestFolder();
        string disc1 = folder.Directory("Disc 1");
        string disc2 = folder.Directory("Disc 2");
        string nested1 = Path.Combine(disc1, "Audio");
        string nested2 = Path.Combine(disc2, "Audio");
        Directory.CreateDirectory(nested1);
        Directory.CreateDirectory(nested2);
        WriteCue(nested1, "album.cue", 0, 0, "first.flac");
        WriteCue(nested2, "album.cue", 0, 0, "second.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            result.SourceSet!.Discs.Select(source => source.DiscNumber).ToArray());
    }

    [TestMethod]
    public void DiscIdentityBeyondTheDocumentedAncestorLimitIsNotGuessed()
    {
        using var folder = new TestFolder();
        string disc1 = folder.Directory("Disc 1");
        string disc2 = folder.Directory("Disc 2");
        string nested1 = Path.Combine(disc1, "Layer", "Audio");
        string nested2 = Path.Combine(disc2, "Layer", "Audio");
        Directory.CreateDirectory(nested1);
        Directory.CreateDirectory(nested2);
        WriteCue(nested1, "album.cue", 0, 0, "first.flac");
        WriteCue(nested2, "album.cue", 0, 0, "second.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "disc numbers are missing");
    }

    [TestMethod]
    public void HiddenCandidatesAndCandidatesBeyondMaximumDepthAreIgnored()
    {
        using var folder = new TestFolder();
        string hiddenCue = Path.Combine(folder.Path, "hidden.cue");
        WriteCue(folder.Path, "hidden.cue", 1, 1, "hidden.flac");
        File.SetAttributes(hiddenCue, File.GetAttributes(hiddenCue) | FileAttributes.Hidden);
        string audio = Path.Combine(folder.Path, "album.flac");
        File.WriteAllBytes(audio, new byte[] { 1 });

        string current = folder.Path;
        for (int depth = 1; depth <= 9; depth++)
        {
            current = Path.Combine(current, "level" + depth);
            Directory.CreateDirectory(current);
        }
        WriteCue(current, "too-deep.cue", 1, 1, "deep.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(VerificationSourceKind.AudioFile, result.SourceSet!.Discs[0].Kind);
        Assert.AreEqual(audio, result.SourceSet.Discs[0].Path);
    }

    [TestMethod]
    public void MaximumDepthDirectoryIsStillScanned()
    {
        using var folder = new TestFolder();
        string current = folder.Path;
        for (int depth = 1; depth <= 8; depth++)
        {
            current = Path.Combine(current, "level" + depth);
            Directory.CreateDirectory(current);
        }
        string cue = Path.Combine(current, "album.cue");
        WriteCue(current, "album.cue", 1, 1, "album.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(cue, result.SourceSet!.Discs[0].Path);
    }

    [TestMethod]
    public void ExplicitNestedManifestsUseTheirCommonAlbumDirectory()
    {
        using var folder = new TestFolder();
        string disc1 = folder.Directory("Disc 1");
        string disc2 = folder.Directory("Disc 2");
        WriteCue(disc1, "one.cue", 1, 2, "one.flac");
        WriteCue(disc2, "two.cue", 2, 2, "two.flac");
        string cue1 = Path.Combine(disc1, "one.cue");
        string cue2 = Path.Combine(disc2, "two.cue");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { cue1, cue2, cue1 });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(folder.Path, result.SourceSet!.RootPath);
        Assert.AreEqual(2, result.SourceSet.Discs.Count);
    }

    [TestMethod]
    public void MultipleManifestsWithoutCompleteDiscIdentityAreRejected()
    {
        using var folder = new TestFolder();
        WriteCue(folder.Path, "first.cue", 0, 0, "first.flac");
        WriteCue(folder.Path, "second.cue", 0, 0, "second.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "disc numbers are missing");
        StringAssert.Contains(result.Error, "does not guess album order");
    }

    [TestMethod]
    public void IncompleteDeclaredDiscSetIsRejectedAsAnAlbum()
    {
        using var folder = new TestFolder();
        WriteCue(folder.Path, "Disc 1.cue", 1, 3, "first.flac");
        WriteCue(folder.Path, "Disc 2.cue", 2, 3, "second.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "more discs than were found");
    }

    [TestMethod]
    public void OneIncompleteDeclarationRejectsAnOtherwiseCompleteSet()
    {
        using var folder = new TestFolder();
        WriteCue(folder.Path, "Disc 1.cue", 1, 3, "first.flac");
        WriteCue(folder.Path, "Disc 2.cue", 2, 2, "second.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "more discs than were found");
    }

    [TestMethod]
    public void SeveralLooseTracksRequireAnExplicitManifest()
    {
        using var folder = new TestFolder();
        File.WriteAllBytes(Path.Combine(folder.Path, "01.flac"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(folder.Path, "02.flac"), new byte[] { 2 });

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "no CUE sheet or playlist");
        StringAssert.Contains(result.Error, "track order and disc boundaries are explicit");
    }

    [TestMethod]
    public void OneLosslessFileIsAValidSingleDiscSource()
    {
        using var folder = new TestFolder();
        string audio = Path.Combine(folder.Path, "album.flac");
        File.WriteAllBytes(audio, new byte[] { 1 });

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { audio });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(VerificationSourceKind.AudioFile, result.SourceSet!.Discs[0].Kind);
        Assert.AreEqual(audio, result.SourceSet.Discs[0].Path);
    }

    [TestMethod]
    public void UnsupportedFileWithNoCodecConfigurationFailsWithoutThrowing()
    {
        using var folder = new TestFolder();
        string audio = Path.Combine(folder.Path, "album.unknown");
        File.WriteAllBytes(audio, new byte[] { 1 });

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { audio });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "supported lossless audio");
    }

    [TestMethod]
    public void ExtensionlessFileFailsWithoutIndexingAnEmptyExtension()
    {
        using var folder = new TestFolder();
        string source = Path.Combine(folder.Path, "album");
        File.WriteAllBytes(source, new byte[] { 1 });

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { source });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "supported lossless audio");
    }

    [TestMethod]
    public void OptimFrogFileIsAValidConfiguredLosslessSource()
    {
        using var folder = new TestFolder();
        string audio = Path.Combine(folder.Path, "album.ofr");
        File.WriteAllBytes(audio, new byte[] { 1 });

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { audio });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(audio, result.SourceSet!.Discs[0].Path);
    }

    [DataTestMethod]
    [DataRow(".flac")]
    [DataRow(".wv")]
    [DataRow(".ape")]
    [DataRow(".tak")]
    [DataRow(".m4a")]
    [DataRow(".tta")]
    [DataRow(".wav")]
    [DataRow(".ofr")]
    public void EveryBaselineLosslessExtensionIsDiscoverable(string extension)
    {
        using var folder = new TestFolder();
        string audio = Path.Combine(folder.Path, "album" + extension);
        File.WriteAllBytes(audio, new byte[] { 1 });

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { audio });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(audio, result.SourceSet!.Discs[0].Path);
        Assert.AreEqual("album", result.SourceSet.Discs[0].DisplayName);
        Assert.AreEqual(1, result.SourceSet.Discs[0].DiscNumber);
        Assert.AreEqual(1, result.SourceSet.Discs[0].TotalDiscs);
    }

    [TestMethod]
    public void ASingleManifestDisplayNameHasNoRedundantDiscPrefix()
    {
        using var folder = new TestFolder();
        WriteCue(folder.Path, "Album Disc 1.cue", 1, 1, "album.flac");

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual("Album Disc 1", result.SourceSet!.Discs[0].DisplayName);
    }

    [TestMethod]
    public void MixedFolderAndFileDropIsRejectedWithoutGuessingScope()
    {
        using var folder = new TestFolder();
        string audio = Path.Combine(folder.Path, "album.flac");
        File.WriteAllBytes(audio, new byte[] { 1 });

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path, audio });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "one album folder at a time");
    }

    // The filesystem-root guard: files whose nearest shared ancestor is a drive root are
    // unrelated, and treating that root as an album folder would scope the run to the whole
    // disk. Its branches survived the 2026-08 mutation campaign unexercised because no test
    // could put real files at a root. SubstDrive maps a drive letter onto a shared temp
    // directory (the programmatic `subst`), so these tests get a genuine root without
    // touching a real one.

    [TestMethod]
    public void TheFilesystemRootTestKnowsARootFromAFolderFromNothing()
    {
        Assert.IsFalse(
            VerificationSourceDiscovery.IsFilesystemRoot(""),
            "an empty string is not a root; treating it as one would misfire the guard");
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.IsTrue(
            VerificationSourceDiscovery.IsFilesystemRoot(root),
            root + " is a filesystem root");
        Assert.IsTrue(
            VerificationSourceDiscovery.IsFilesystemRoot(root.TrimEnd(Path.DirectorySeparatorChar)),
            "a root without its trailing separator is still a root");
        Assert.IsFalse(
            VerificationSourceDiscovery.IsFilesystemRoot(Path.GetTempPath()),
            "an ordinary directory is not a root");
    }

    [TestMethod]
    public void ASingleManifestSelectedAtADriveRootIsStillAccepted()
    {
        using var drive = new SubstDrive();
        if (drive.Root == null)
            Assert.Inconclusive("No free drive letter to map a test root onto.");

        string cue = Path.Combine(drive.Root, drive.Unique("Album") + ".cue");
        File.WriteAllText(cue, "FILE \"album.flac\" WAVE\n  TRACK 01 AUDIO\n    INDEX 01 00:00:00\n");
        try
        {
            VerificationSourceDiscoveryResult result =
                new VerificationSourceDiscovery().Discover(new[] { cue });

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(1, result.SourceSet!.Discs.Count);
        }
        finally
        {
            File.Delete(cue);
        }
    }

    [TestMethod]
    public void MultipleManifestsWhoseOnlySharedAncestorIsADriveRootAreRejected()
    {
        using var drive = new SubstDrive();
        if (drive.Root == null)
            Assert.Inconclusive("No free drive letter to map a test root onto.");

        // Two cues that would form a valid two-disc set anywhere else, so the failure is
        // attributable to the root guard alone.
        string stem = drive.Unique("Album");
        string cue1 = Path.Combine(drive.Root, stem + " Disc 1.cue");
        string cue2 = Path.Combine(drive.Root, stem + " Disc 2.cue");
        WriteCue(drive.Root, Path.GetFileName(cue1), 1, 2, "d1.flac");
        WriteCue(drive.Root, Path.GetFileName(cue2), 2, 2, "d2.flac");
        try
        {
            VerificationSourceDiscoveryResult result =
                new VerificationSourceDiscovery().Discover(new[] { cue1, cue2 });

            Assert.IsFalse(result.Ok, "a whole drive root must not become the album scope");
            StringAssert.Contains(result.Error, "same album location");
        }
        finally
        {
            File.Delete(cue1);
            File.Delete(cue2);
        }
    }

    private static void WriteCue(
        string directory,
        string filename,
        int disc,
        int total,
        string audio)
    {
        File.WriteAllText(
            Path.Combine(directory, filename),
            (disc > 0 ? $"REM DISCNUMBER {disc}\n" : "") +
            (total > 0 ? $"REM TOTALDISCS {total}\n" : "") +
            $"FILE \"{audio}\" WAVE\n  TRACK 01 AUDIO\n    INDEX 01 00:00:00\n");
    }

    /// <summary>
    /// Maps a free drive letter onto one fixed shared temp directory - the programmatic
    /// `subst` - so tests can place files at a genuine filesystem root. Every process maps
    /// its letter to the SAME target, so concurrent test sessions (Stryker runs many) can
    /// stack identical definitions harmlessly; tests keep their files collision-free with
    /// Unique() names instead. Dispose pops only this instance's definition, leaving any
    /// identical stacked ones intact for the sessions that own them.
    /// </summary>
    // The mutation profile compiles this file with Nullable=disable while the main test
    // project enables it; the directive keeps the annotations legal in both hosts.
#nullable enable
    private sealed class SubstDrive : IDisposable
    {
        [System.Runtime.InteropServices.DllImport(
            "kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode,
            SetLastError = true)]
        private static extern bool DefineDosDevice(int flags, string deviceName, string? targetPath);

        private const int RemoveDefinition = 2;    // DDD_REMOVE_DEFINITION
        private const int ExactMatchOnRemove = 4;  // DDD_EXACT_MATCH_ON_REMOVE

        private readonly string _device = "";
        private readonly string _target = "";

        public string? Root { get; }

        public SubstDrive()
        {
            string target = Path.Combine(Path.GetTempPath(), "cuetools-subst-root");
            System.IO.Directory.CreateDirectory(target);
            for (char letter = 'Z'; letter >= 'E'; letter--)
            {
                string device = letter + ":";
                string root = device + Path.DirectorySeparatorChar;
                if (System.IO.Directory.Exists(root))
                    continue;
                if (!DefineDosDevice(0, device, target))
                    continue;

                // Prove the letter really serves OUR target before trusting it: a probe file
                // written into the target must be visible through the root. A letter that
                // raced with a real drive or a foreign mapping fails this and is released.
                string probe = Path.Combine(target, "probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                bool visible = File.Exists(Path.Combine(root, Path.GetFileName(probe)));
                File.Delete(probe);
                if (!visible)
                {
                    DefineDosDevice(RemoveDefinition | ExactMatchOnRemove, device, target);
                    continue;
                }

                _device = device;
                _target = target;
                Root = root;
                return;
            }
        }

        public string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");

        public void Dispose()
        {
            if (Root != null)
                DefineDosDevice(RemoveDefinition | ExactMatchOnRemove, _device, _target);
        }
    }
#nullable restore

    private sealed class TestFolder : IDisposable
    {
        public TestFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "verify-discovery-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Directory(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Path))
                System.IO.Directory.Delete(Path, recursive: true);
        }
    }
}
