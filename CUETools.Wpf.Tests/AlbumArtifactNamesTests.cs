using System;
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class AlbumArtifactNamesTests
{
    [TestMethod]
    public void StemCarriesArtistAlbumYearAndDiscIdentity()
    {
        var metadata = new CUEMetadata
        {
            Artist = "Genesis",
            Title = "...Calling All Stations...",
            Year = "1997",
            DiscNumber = "2",
            TotalDiscs = "2",
            DiscName = "Bonus"
        };

        string stem = AlbumArtifactNames.CreateStem(metadata, value => value);

        Assert.AreEqual(
            "Genesis - ...Calling All Stations... (1997) (Disc 2 of 2 - Bonus)",
            stem);
        Assert.AreEqual(stem + ".cue", AlbumArtifactNames.CueFileName(stem));
        Assert.AreEqual(
            stem + " - Test & Copy.log",
            AlbumArtifactNames.TestCopyLogFileName(stem));
        Assert.AreEqual(
            stem + " - CTDB Repair.log",
            AlbumArtifactNames.RepairLogFileName(stem));
        Assert.AreEqual(
            stem + ".accurip",
            AlbumArtifactNames.AccurateRipFileName(stem));
    }

    [TestMethod]
    public void StemIsPortableBoundedAndHasAnUnknownFallback()
    {
        string longTitle = new string('A', 300) + ":?";
        var metadata = new CUEMetadata { Artist = "Artist", Title = longTitle };

        string stem = AlbumArtifactNames.CreateStem(metadata, value => value);

        Assert.IsTrue(stem.Length <= AlbumArtifactNames.MaximumStemLength);
        Assert.IsFalse(stem.Contains(':'));
        Assert.IsFalse(stem.Contains('?'));
        Assert.AreEqual(
            "Unknown Album",
            AlbumArtifactNames.CreateStem(new CUEMetadata(), _ => ""));
    }

    [TestMethod]
    public void PartialMetadataAndSingleDiscIdentityDoNotCreateDanglingSyntax()
    {
        Assert.AreEqual(
            "Artist",
            AlbumArtifactNames.CreateStem(
                new CUEMetadata { Artist = "Artist" },
                value => value));
        Assert.AreEqual(
            "Album",
            AlbumArtifactNames.CreateStem(
                new CUEMetadata { Title = "Album" },
                value => value));
        Assert.AreEqual(
            "Artist - Album",
            AlbumArtifactNames.CreateStem(
                new CUEMetadata
                {
                    Artist = "Artist",
                    Title = "Album",
                    DiscNumber = "1",
                    TotalDiscs = "1"
                },
                value => value));
    }

    [TestMethod]
    public void NullCleanserIsRejectedBeforeMetadataIsRead()
    {
        Assert.ThrowsException<ArgumentNullException>(
            () => AlbumArtifactNames.CreateStem(null, null));
    }

    [TestMethod]
    public void FallbackAndNullCleanserResultCannotProduceABlankStem()
    {
        Assert.AreEqual(
            "Fallback Album",
            AlbumArtifactNames.CreateStem(null, value => value, "Fallback Album"));
        Assert.AreEqual(
            "Unknown Album",
            AlbumArtifactNames.CreateStem(null, _ => null, "Fallback Album"));
    }

    [TestMethod]
    public void DiscNameWithoutATotalDoesNotAddAnEmptyOfClause()
    {
        var metadata = new CUEMetadata
        {
            Artist = "Artist",
            Title = "Album",
            DiscNumber = "2",
            TotalDiscs = "",
            DiscName = "Bonus",
        };

        Assert.AreEqual(
            "Artist - Album (Disc 2 - Bonus)",
            AlbumArtifactNames.CreateStem(metadata, value => value));
    }

    [TestMethod]
    public void BlankStemUsesFallbackForEveryHumanFacingArtifactName()
    {
        Assert.AreEqual("Unknown Album.cue", AlbumArtifactNames.CueFileName(" "));
        Assert.AreEqual(
            "Unknown Album - Test & Copy.log",
            AlbumArtifactNames.TestCopyLogFileName(null));
        Assert.AreEqual(
            "Unknown Album - CTDB Repair.log",
            AlbumArtifactNames.RepairLogFileName(""));
        Assert.AreEqual("Unknown Album.accurip", AlbumArtifactNames.AccurateRipFileName("\t"));
    }
}
