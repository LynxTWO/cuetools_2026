using System;
using System.IO;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class TestCopyRepairBindingTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cuetools-testcopy-repair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [TestMethod]
    public void RebindRepairSourceAcceptsOnlyAnExistingFileInsidePublishedAlbum()
    {
        string album = Path.Combine(_root, "published");
        Directory.CreateDirectory(album);
        string cue = Path.Combine(album, "album.cue");
        File.WriteAllText(cue, "FILE \"01.flac\" WAVE");
        File.WriteAllText(
            Path.Combine(_root, "outside.cue"),
            "FILE \"outside.flac\" WAVE");

        Assert.AreEqual(
            cue,
            RipService.RebindRepairSource(album, "album.cue"));
        Assert.AreEqual(
            "",
            RipService.RebindRepairSource(album, "missing.cue"));
        Assert.AreEqual(
            "",
            RipService.RebindRepairSource(album, "..\\outside.cue"));
    }
}
