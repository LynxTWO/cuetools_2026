using CUETools.Wpf.Accuracy;
using CUETools.Wpf.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class TrackCrcPresentationTests
{
    [TestMethod]
    public void MatchingRepeatedCrossDriveEvidenceIsVisible()
    {
        var item = new TrackItem();
        item.ApplyCrcEvidence(new TrackCrc
        {
            TestCrc32 = 0x1234ABCD,
            CopyCrc32 = 0x1234ABCD,
            TestMatchCount = 3,
            CopyMatchCount = 2,
            TestDriveCount = 2,
            CopyDriveCount = 1,
            TestDriveFingerprints = "A,B",
            CopyDriveFingerprints = "A",
        });

        Assert.AreEqual("1234ABCD x3", item.TestCrc);
        Assert.AreEqual("1234ABCD x2", item.CopyCrc);
        Assert.IsTrue(item.CrcsMatch);
        Assert.IsFalse(item.CrcsDiffer);
        Assert.IsTrue(item.CrossDriveCorroborated);
        StringAssert.Contains(item.CrcEvidenceTip, "2 drive(s)");
    }

    [TestMethod]
    public void MatchingTestAndCopyFromDifferentSingleDrivesIsCrossDriveEvidence()
    {
        var item = new TrackItem();
        item.ApplyCrcEvidence(new TrackCrc
        {
            TestCrc32 = 0x1234ABCD,
            CopyCrc32 = 0x1234ABCD,
            TestMatchCount = 1,
            CopyMatchCount = 1,
            TestDriveCount = 1,
            CopyDriveCount = 1,
            TestDriveFingerprints = "DRIVE-A",
            CopyDriveFingerprints = "DRIVE-B",
        });

        Assert.IsTrue(item.CrossDriveCorroborated);
        StringAssert.Contains(
            item.CrcEvidenceTip,
            "Corroborated across 2 distinct drives");
    }

    [TestMethod]
    public void DifferentCrcsAreMarkedAsDisagreement()
    {
        var item = new TrackItem();
        item.ApplyCrcEvidence(new TrackCrc
        {
            TestCrc32 = 1,
            CopyCrc32 = 2,
            TestMatchCount = 1,
            CopyMatchCount = 1,
        });

        Assert.IsFalse(item.CrcsMatch);
        Assert.IsTrue(item.CrcsDiffer);
        Assert.IsFalse(item.CrossDriveCorroborated);
    }
}
