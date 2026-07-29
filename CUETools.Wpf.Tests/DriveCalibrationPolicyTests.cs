using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class DriveCalibrationPolicyTests
{
    [TestMethod]
    public void FlushParserAcceptsOnlyPositiveExplicitSizes()
    {
        Assert.AreEqual(786432, DriveCalibrationService.ParseFlushBytes("Flush:786432"));
        Assert.AreEqual(0, DriveCalibrationService.ParseFlushBytes(null));
        Assert.AreEqual(0, DriveCalibrationService.ParseFlushBytes("Unconfirmed"));
        Assert.AreEqual(0, DriveCalibrationService.ParseFlushBytes("Flush:0"));
        Assert.AreEqual(0, DriveCalibrationService.ParseFlushBytes("Flush:not-a-number"));
    }

    [TestMethod]
    public void IndependentReadGateRequiresParsedFlushOrExplicitNoCacheProof()
    {
        Assert.IsTrue(
            DriveCalibrationService.HasIndependentReadStrategy("Flush:786432"));
        Assert.IsTrue(
            DriveCalibrationService.HasIndependentReadStrategy(
                "Media re-reads (no cache)"));
        Assert.IsFalse(
            DriveCalibrationService.HasIndependentReadStrategy(null));
        Assert.IsFalse(
            DriveCalibrationService.HasIndependentReadStrategy("Flush:"));
        Assert.IsFalse(
            DriveCalibrationService.HasIndependentReadStrategy("Flush:0"));
        Assert.IsFalse(
            DriveCalibrationService.HasIndependentReadStrategy(
                "Flush:not-a-number"));
    }

    [TestMethod]
    public void CalibrationVersionGateForcesNewCapabilityProbe()
    {
        Assert.IsFalse(DriveCalibrationService.IsCurrent(null));
        Assert.IsFalse(DriveCalibrationService.IsCurrent(
            new DriveCalibration { RipperVersion = "2026.1.0" }));
        Assert.IsTrue(DriveCalibrationService.IsCurrent(
            new DriveCalibration
            {
                RipperVersion = DriveCalibrationService.CurrentVersion,
            }));
    }

    [TestMethod]
    public void ProvenFlushSurvivesSmallerAndApparentlyUncachedLaterProbes()
    {
        var prior = new DriveCalibration { CacheDefeat = "Flush:1048576" };

        string smaller = DriveCalibrationService.SelectConservativeCacheDefeat(
            new CUETools.Ripper.SCSI.CDDriveReader.DriveProbe
            {
                Probed = true,
                CachesReReads = true,
                FlushEvictBytes = 786432,
            },
            prior,
            out CalConfidence smallerConfidence);
        Assert.AreEqual("Flush:1048576", smaller);
        Assert.AreEqual(CalConfidence.Confirmed, smallerConfidence);

        string apparentlyUncached =
            DriveCalibrationService.SelectConservativeCacheDefeat(
                new CUETools.Ripper.SCSI.CDDriveReader.DriveProbe
                {
                    Probed = true,
                    CachesReReads = false,
                    FlushEvictBytes = 0,
                },
                prior,
                out CalConfidence uncachedConfidence);
        Assert.AreEqual("Flush:1048576", apparentlyUncached);
        Assert.AreEqual(CalConfidence.Confirmed, uncachedConfidence);
    }

    [TestMethod]
    public void OverreadAppliesOnlyToTheExactKnownCalibratedOffset()
    {
        var calibration = new DriveCalibration
        {
            ReadOffsetKnown = true,
            ReadOffsetSamples = 667,
            OverreadLeadOut = true,
        };

        Assert.IsTrue(
            DriveCalibrationService.CanApplyOverread(
                calibration,
                currentOffsetKnown: true,
                currentOffsetSamples: 667));
        Assert.IsFalse(
            DriveCalibrationService.CanApplyOverread(
                calibration,
                currentOffsetKnown: true,
                currentOffsetSamples: 6));
        Assert.IsFalse(
            DriveCalibrationService.CanApplyOverread(
                calibration,
                currentOffsetKnown: false,
                currentOffsetSamples: 667));
        calibration.ReadOffsetKnown = false;
        Assert.IsFalse(
            DriveCalibrationService.CanApplyOverread(
                calibration,
                currentOffsetKnown: true,
                currentOffsetSamples: 667));
    }
}
