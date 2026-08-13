using System.Collections.Generic;
using System.IO;
using CUETools.Wpf.Accuracy;
using CUETools.Wpf.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// R119: a salvage capture (Burst quality, C2 off, minimum speed) proves the DRIVE's output is
/// stable, never that the disc is undamaged. The reads leg of verification must not apply; an
/// exact database match remains honest verification of the bytes.
/// </summary>
[TestClass]
public sealed class SalvageModeTests
{
    [TestMethod]
    public void SalvagedAgreementIsNotReadVerification()
    {
        var r = new RipReport
        {
            OpticalReadsUsed = 2,
            MinimumAgreeingReads = 2,
            Salvaged = true,
        };
        Assert.IsFalse(r.IndependentReadsVerified,
            "salvage agreement proves drive-stable output, not disc health");
        Assert.IsFalse(r.Verified);
        StringAssert.Contains(r.BuildLogBody(), "Salvage       : drive-stable capture");
        StringAssert.Contains(r.BuildLogBody(), "Accuracy mode : Salvage");
    }

    [TestMethod]
    public void ExactDatabaseMatchStillVerifiesASalvageCapture()
    {
        var r = new RipReport
        {
            Accurate = true,
            ArConfidence = 8,
            OpticalReadsUsed = 2,
            MinimumAgreeingReads = 2,
            Salvaged = true,
        };
        Assert.IsTrue(r.Verified,
            "an exact AccurateRip match verifies the bytes regardless of how they were read");

        var damaged = new RipReport
        {
            Accurate = true,
            ArConfidence = 8,
            Salvaged = true,
            FailedWindows = 2,
        };
        Assert.IsFalse(damaged.Verified, "damage still outranks everything");
    }

    [TestMethod]
    public void SalvageLogNamesTheCaptureAndNeverSaysPassed()
    {
        var reads = new List<VerifyRecord> { new(), new() };
        var result = new TestCopyResult
        {
            Outcome = TestCopyOutcome.Passed,
            ReadsUsed = 2,
            Tracks = System.Array.Empty<TrackVerdict>(),
            HeldTracks = System.Array.Empty<int>(),
        };

        string log = TestAndCopyLog.Format(result, reads, "disc", "drive", 6,
            failedWindows: 0, salvage: true);
        StringAssert.Contains(log, "SALVAGE capture: Burst quality, C2 pointers off");
        StringAssert.Contains(log, "Test & Copy SALVAGE CONSISTENT");
        Assert.IsFalse(log.Contains("Test & Copy PASSED"),
            "a salvage capture must never claim the verified PASSED outcome");
    }

    [TestMethod]
    public void SalvageRunsTheEngineAtBurstWithIndependenceGuardsIntact()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(System.AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        string source = File.ReadAllText(
            Path.Combine(root, "CUETools.App.Core", "Services", "RipService.cs"));

        StringAssert.Contains(source, "int rq = salvage ? 0 : Math.Max(1, Math.Min(2, cq));",
            "salvage runs Burst quality; everything else keeps forced-Secure");
        StringAssert.Contains(source, "reader.ConcealUnconfirmedSamples = true;",
            "salvage conceals what the vote cannot confirm - a raw guess is audible garbage");
        Assert.IsFalse(source.Contains("reader.DriveC2ErrorMode = 0;"),
            "C2 must stay ON: it is how the drive reports which samples it could not correct, " +
            "and silencing it produced an unlistenable capture");
        StringAssert.Contains(source, "requireIndependentReads: true",
            "the calibration gate must survive salvage - agreement from a cache echo is fake");
    }
}
