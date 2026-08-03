using System.Collections.Generic;
using CUETools.Wpf.Accuracy;
using CUETools.Wpf.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// R109: a damaged-consistent result must render as consistent on EVERY surface - certificate
/// headline, badge, history row, log body - and database evidence must bind to the committed
/// read's own checks, never the newest read's.
/// </summary>
[TestClass]
public sealed class DamageTruthLabelingTests
{
    private static RipReport AgreedReads(int failedWindows, bool repair = false) => new()
    {
        Mode = "Test & Copy (3 reads)",
        OpticalReadsUsed = 3,
        MinimumAgreeingReads = 2,
        FailedWindows = failedWindows,
        DamageRepairRequired = repair,
    };

    [TestMethod]
    public void DamagedAgreementIsNeverVerified()
    {
        var clean = AgreedReads(0);
        Assert.IsTrue(clean.Verified);
        Assert.IsTrue(clean.IndependentReadsVerified);
        Assert.IsFalse(clean.Damaged);

        var damaged = AgreedReads(3);
        Assert.IsTrue(damaged.Damaged);
        Assert.IsFalse(damaged.Verified, "unrecoverable windows demote agreement to consistency");
        Assert.IsFalse(damaged.IndependentReadsVerified);

        var repairable = AgreedReads(0, repair: true);
        Assert.IsTrue(repairable.Damaged, "CTDB-detected damage counts even with zero failed windows");
        Assert.IsFalse(repairable.Verified);
    }

    [TestMethod]
    public void DatabaseMatchDoesNotOverrideDamage()
    {
        var r = new RipReport
        {
            Accurate = true,
            ArConfidence = 12,
            OpticalReadsUsed = 2,
            MinimumAgreeingReads = 2,
            FailedWindows = 1,
        };
        Assert.IsTrue(r.DatabaseConfirmed);
        Assert.IsFalse(r.Verified,
            "matching reads with unrecoverable windows are CONSISTENT, not cleanly verified");
    }

    [TestMethod]
    public void LogBodyNamesTheDamageAndDowngradesTheIndependentLine()
    {
        string damagedBody = AgreedReads(2, repair: true).BuildLogBody();
        StringAssert.Contains(damagedBody, "Independent   : consistent after 3 optical reads");
        StringAssert.Contains(damagedBody, "Damage        : 2 unrecoverable sector window(s)");
        StringAssert.Contains(damagedBody, "CTDB-detected damage");

        string cleanBody = AgreedReads(0).BuildLogBody();
        StringAssert.Contains(cleanBody, "Independent   : verified after 3 optical reads");
        Assert.IsFalse(cleanBody.Contains("Damage        :"),
            "old and clean reports must not invent a damage line");
    }

    [TestMethod]
    public void TestCopyLogBindsDatabaseLinesToTheCommittedRead()
    {
        var committed = new VerifyRecord { ArConfidence = 0, ArTotal = 5, CtdbConfidence = 0, CtdbTotal = 2 };
        var newest = new VerifyRecord { ArConfidence = 9, ArTotal = 9, CtdbConfidence = 7, CtdbTotal = 7 };
        var reads = new List<VerifyRecord> { new(), committed, newest };
        var result = new TestCopyResult
        {
            Outcome = TestCopyOutcome.Passed,
            ReadsUsed = 3,
            Tracks = System.Array.Empty<TrackVerdict>(),
            HeldTracks = System.Array.Empty<int>(),
        };

        string bound = TestAndCopyLog.Format(result, reads, "disc", "drive", 6,
            failedWindows: 0, committedReadIndex: 1);
        StringAssert.Contains(bound, "AccurateRip: found, no exact match",
            "the committed read's own AR outcome must be reported");
        Assert.IsFalse(bound.Contains("confidence 9"),
            "the newest read's verdict must not stand in for the committed bytes");

        string legacy = TestAndCopyLog.Format(result, reads, "disc", "drive", 6,
            failedWindows: 0);
        StringAssert.Contains(legacy, "accurate, confidence 9",
            "callers that do not name a committed read keep the newest-read fallback");
    }
}
