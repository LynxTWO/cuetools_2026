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
