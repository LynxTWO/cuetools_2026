using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class RequestedDefaultsTests
{
    [TestMethod]
    public void NewWpfProfileUsesArchivalEvidenceAndArtworkDefaults()
    {
        CUEConfig config = App.CreateWpfDefaultConfig();

        Assert.IsTrue(config.advanced.CreateTOC);
        Assert.IsTrue(config.advanced.DetailedCTDBLog);
        Assert.AreEqual(
            CUEConfigAdvanced.CTDBCoversSearch.Extensive,
            config.advanced.coversSearch);
        Assert.AreEqual(1500, config.maxAlbumArtSize);
        Assert.IsTrue(config.detectHDCD);
        Assert.IsFalse(config.decodeHDCD);
        Assert.IsFalse(config.decodeHDCDto24bit);
    }

    [TestMethod]
    public void V3MigrationMovesOnlyTheFormerOwnerCoverDefault()
    {
        var oldDefault = new CUEConfig { maxAlbumArtSize = 1000 };
        var settings = new AppSettings();

        Assert.IsTrue(App.ApplyDefaultsV3(oldDefault, settings));
        Assert.AreEqual(1500, oldDefault.maxAlbumArtSize);
        Assert.IsTrue(oldDefault.advanced.CreateTOC);
        Assert.IsTrue(oldDefault.advanced.DetailedCTDBLog);
        Assert.AreEqual(
            CUEConfigAdvanced.CTDBCoversSearch.Extensive,
            oldDefault.advanced.coversSearch);

        var userSized = new CUEConfig { maxAlbumArtSize = 777 };
        Assert.IsTrue(App.ApplyDefaultsV3(userSized, new AppSettings()));
        Assert.AreEqual(777, userSized.maxAlbumArtSize);
    }

    [TestMethod]
    public void CompletedMigrationNeverRewritesLaterUserChoices()
    {
        var config = new CUEConfig { maxAlbumArtSize = 900 };
        config.advanced.CreateTOC = false;
        config.advanced.DetailedCTDBLog = false;
        config.advanced.coversSearch =
            CUEConfigAdvanced.CTDBCoversSearch.None;
        var settings = new AppSettings { DefaultsV3Applied = true };

        Assert.IsFalse(App.ApplyDefaultsV3(config, settings));
        Assert.AreEqual(900, config.maxAlbumArtSize);
        Assert.IsFalse(config.advanced.CreateTOC);
        Assert.IsFalse(config.advanced.DetailedCTDBLog);
        Assert.AreEqual(
            CUEConfigAdvanced.CTDBCoversSearch.None,
            config.advanced.coversSearch);
    }

    [TestMethod]
    public void RipLayoutMapsToTheEngineStyleOnlyForEncoding()
    {
        Assert.AreEqual(
            CUEStyle.GapsAppended,
            RipService.ResolveOutputStyle(true, RipOutputLayout.Tracks));
        Assert.AreEqual(
            CUEStyle.SingleFileWithCUE,
            RipService.ResolveOutputStyle(
                true,
                RipOutputLayout.ImageWithEmbeddedCue));
        Assert.AreEqual(
            CUEStyle.GapsAppended,
            RipService.ResolveOutputStyle(
                false,
                RipOutputLayout.ImageWithEmbeddedCue));
    }
}
