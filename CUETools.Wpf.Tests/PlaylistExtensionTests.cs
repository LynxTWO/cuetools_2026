using System;
using System.IO;
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// Verification discovery accepts .m3u8 and the file picker offers it, so every engine
/// site that reads a playlist has to accept it too. Before D-071 the engine compared
/// against ".m3u" exactly, and an .m3u8 the user was invited to choose failed to load.
/// </summary>
[TestClass]
public sealed class PlaylistExtensionTests
{
    [TestMethod]
    public void BothPlaylistExtensionsAreRecognisedRegardlessOfCase()
    {
        Assert.IsTrue(CUESheet.IsPlaylistExtension(".m3u"));
        Assert.IsTrue(CUESheet.IsPlaylistExtension(".m3u8"));
        Assert.IsTrue(CUESheet.IsPlaylistExtension(".M3U"));
        Assert.IsTrue(CUESheet.IsPlaylistExtension(".M3U8"));
    }

    [TestMethod]
    public void NonPlaylistExtensionsAreNotMistakenForPlaylists()
    {
        Assert.IsFalse(CUESheet.IsPlaylistExtension(".cue"));
        Assert.IsFalse(CUESheet.IsPlaylistExtension(".flac"));
        Assert.IsFalse(CUESheet.IsPlaylistExtension(".m3u88"));
        Assert.IsFalse(CUESheet.IsPlaylistExtension(".m3"));
        Assert.IsFalse(CUESheet.IsPlaylistExtension(""));
    }

    [TestMethod]
    public void Utf8PlaylistIsDiscoveredAsAVerificationSource()
    {
        using var folder = new PlaylistTestFolder();
        File.WriteAllText(Path.Combine(folder.Path, "album.m3u8"), "01.flac\n");
        File.WriteAllBytes(Path.Combine(folder.Path, "01.flac"), new byte[] { 1 });

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery().Discover(new[] { folder.Path });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(1, result.SourceSet!.Discs.Count);
        Assert.AreEqual(VerificationSourceKind.Playlist, result.SourceSet.Discs[0].Kind);
    }

    [TestMethod]
    public void FilesSharingOnlyTheFilesystemRootAreRejected()
    {
        using var first = new PlaylistTestFolder();
        using var second = new PlaylistTestFolder();
        string a = Path.Combine(first.Path, "a.cue");
        string b = Path.Combine(second.Path, "b.cue");
        File.WriteAllText(a, "FILE \"01.flac\" WAVE\n");
        File.WriteAllText(b, "FILE \"02.flac\" WAVE\n");

        // Both temp folders live under the system temp directory, so this pair still shares
        // a real ancestor and must be allowed through to normal manifest handling.
        VerificationSourceDiscoveryResult shared =
            new VerificationSourceDiscovery().Discover(new[] { a, b });
        Assert.AreNotEqual(
            "Selected files must belong to the same album location.",
            shared.Error);
    }

    private sealed class PlaylistTestFolder : IDisposable
    {
        public PlaylistTestFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "playlist-extension-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
