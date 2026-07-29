using CUETools.Processor;
using CUETools.Wpf.Models;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class ReleasePreferenceTests
{
    [TestMethod]
    public void IdenticalGenericMultiDiscEntryRanksBelowSingleDiscRelease()
    {
        CUEMetadata single = KennyG(totalDiscs: "1", discName: "");
        CUEMetadata multi = KennyG(totalDiscs: "2", discName: "Disc 1");
        var singleMatch = Match(single, 100);
        var multiMatch = Match(multi, 100);

        DriveService.PreferSingleDiscDuplicate(
            new[] { multiMatch, singleMatch });

        Assert.AreEqual(97, multiMatch.Score);
        Assert.AreEqual(100, singleMatch.Score);
        StringAssert.Contains(multiMatch.Why, "identical single-disc");
    }

    [TestMethod]
    public void NamedBoxDiscAndDifferentBarcodeRemainUntouched()
    {
        CUEMetadata single = KennyG(totalDiscs: "1", discName: "");
        CUEMetadata named = KennyG(totalDiscs: "2", discName: "Bonus Demos");
        CUEMetadata otherBarcode = KennyG(
            totalDiscs: "2",
            discName: "Disc 1",
            barcode: "DIFFERENT");
        var namedMatch = Match(named, 100);
        var otherMatch = Match(otherBarcode, 100);

        DriveService.PreferSingleDiscDuplicate(
            new[] { Match(single, 100), namedMatch, otherMatch });

        Assert.AreEqual(100, namedMatch.Score);
        Assert.AreEqual(100, otherMatch.Score);
    }

    private static ReleaseMatch Match(CUEMetadata metadata, int score) =>
        new()
        {
            Metadata = metadata,
            Score = score,
            Why = "MusicBrainz: matches the disc layout",
        };

    private static CUEMetadata KennyG(
        string totalDiscs,
        string discName,
        string barcode = "078221908528")
    {
        var metadata = new CUEMetadata("release", 2)
        {
            Artist = "Kenny G",
            Title = "Classics in the Key of G",
            Year = "1999",
            Barcode = barcode,
            DiscNumber = "1",
            TotalDiscs = totalDiscs,
            DiscName = discName,
        };
        metadata.Tracks[0].Artist = "Kenny G";
        metadata.Tracks[0].Title = "Summertime";
        metadata.Tracks[1].Artist = "Kenny G";
        metadata.Tracks[1].Title = "The Look of Love";
        return metadata;
    }
}
