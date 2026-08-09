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
        StringAssert.Contains(result.Error, "instead of guessing");
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
        StringAssert.StartsWith(result.SourceSet.Discs[0].DisplayName, "Disc 1:");
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
