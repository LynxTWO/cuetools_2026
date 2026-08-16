using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// D-069 (ask once, remember the answer) and D-070 (only reads without unrecoverable
/// errors may be submitted). Nothing here touches a network: the policy is the whole
/// decision, so it can be pinned exactly.
/// </summary>
[TestClass]
public sealed class CtdbSubmissionPolicyTests
{
    private static CUEConfigAdvanced FirstRun() => new() { CTDBAsk = true, CTDBSubmit = false };

    private static CtdbSubmissionCandidate CleanRip() => new()
    {
        RunCompleted = true,
        LookupFailed = false,
        FailedWindows = 0,
        Salvaged = false,
        Held = false,
        Album = "Album",
        Artist = "Artist",
        Confidence = 3
    };

    [TestMethod]
    public void ACleanReadIsEligibleAndAsksFirst()
    {
        CtdbSubmissionDecision decision = CtdbSubmissionPolicy.Decide(CleanRip(), FirstRun());

        Assert.IsTrue(decision.Eligible);
        Assert.IsTrue(decision.NeedsPrompt);
    }

    [TestMethod]
    public void AnUnrecoverableWindowBlocksSubmission()
    {
        var candidate = new CtdbSubmissionCandidate
        {
            RunCompleted = true,
            FailedWindows = 1,
            Album = "Album"
        };

        CtdbSubmissionDecision decision = CtdbSubmissionPolicy.Decide(candidate, FirstRun());

        Assert.IsFalse(decision.Eligible);
        Assert.AreEqual(CtdbSubmissionBlock.UnrecoverableWindows, decision.Block);
    }

    [TestMethod]
    public void ASalvageCaptureIsNeverSubmitted()
    {
        var candidate = new CtdbSubmissionCandidate
        {
            RunCompleted = true,
            Salvaged = true,
            Album = "Album"
        };

        Assert.AreEqual(
            CtdbSubmissionBlock.Salvaged,
            CtdbSubmissionPolicy.Decide(candidate, FirstRun()).Block);
    }

    [TestMethod]
    public void AHeldTestAndCopyIsNeverSubmitted()
    {
        var candidate = new CtdbSubmissionCandidate
        {
            RunCompleted = true,
            Held = true,
            Album = "Album"
        };

        Assert.AreEqual(
            CtdbSubmissionBlock.Held,
            CtdbSubmissionPolicy.Decide(candidate, FirstRun()).Block);
    }

    [TestMethod]
    public void AFailedLookupBlocksSubmission()
    {
        var candidate = new CtdbSubmissionCandidate
        {
            RunCompleted = true,
            LookupFailed = true,
            Album = "Album"
        };

        Assert.AreEqual(
            CtdbSubmissionBlock.LookupFailed,
            CtdbSubmissionPolicy.Decide(candidate, FirstRun()).Block);
    }

    [TestMethod]
    public void AnIncompleteRunBlocksSubmission()
    {
        var candidate = new CtdbSubmissionCandidate { RunCompleted = false, Album = "Album" };

        Assert.AreEqual(
            CtdbSubmissionBlock.RunIncomplete,
            CtdbSubmissionPolicy.Decide(candidate, FirstRun()).Block);
    }

    [TestMethod]
    public void ARememberedYesSubmitsWithoutAskingAgain()
    {
        var advanced = new CUEConfigAdvanced { CTDBAsk = false, CTDBSubmit = true };

        CtdbSubmissionDecision decision = CtdbSubmissionPolicy.Decide(CleanRip(), advanced);

        Assert.IsTrue(decision.Eligible);
        Assert.IsFalse(decision.NeedsPrompt);
    }

    [TestMethod]
    public void ARememberedNoStopsEveryFurtherSubmission()
    {
        var advanced = new CUEConfigAdvanced { CTDBAsk = false, CTDBSubmit = false };

        Assert.AreEqual(
            CtdbSubmissionBlock.DeclinedPreviously,
            CtdbSubmissionPolicy.Decide(CleanRip(), advanced).Block);
    }

    [TestMethod]
    public void QualityBlocksOutrankARememberedYes()
    {
        var advanced = new CUEConfigAdvanced { CTDBAsk = false, CTDBSubmit = true };
        var candidate = new CtdbSubmissionCandidate
        {
            RunCompleted = true,
            Salvaged = true,
            Album = "Album"
        };

        Assert.AreEqual(
            CtdbSubmissionBlock.Salvaged,
            CtdbSubmissionPolicy.Decide(candidate, advanced).Block);
    }

    [TestMethod]
    public void RememberingAnAnswerStopsTheAskingEitherWay()
    {
        var yes = new CUEConfigAdvanced { CTDBAsk = true, CTDBSubmit = false };
        CtdbSubmissionPolicy.Remember(yes, submit: true);
        Assert.IsFalse(yes.CTDBAsk);
        Assert.IsTrue(yes.CTDBSubmit);

        var no = new CUEConfigAdvanced { CTDBAsk = true, CTDBSubmit = true };
        CtdbSubmissionPolicy.Remember(no, submit: false);
        Assert.IsFalse(no.CTDBAsk);
        Assert.IsFalse(no.CTDBSubmit);
    }

    [TestMethod]
    public void TheSubmittedQualityIsTheClassicConstant()
    {
        Assert.AreEqual(100, CtdbSubmissionPolicy.CleanReadQuality);
    }
}
