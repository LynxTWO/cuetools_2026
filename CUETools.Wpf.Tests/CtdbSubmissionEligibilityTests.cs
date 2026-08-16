using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// D-069 (ask once, remember the answer) and D-070 (only reads without unrecoverable
/// errors may be submitted). Nothing here touches a network: the decision is entirely
/// local, so it can be pinned exactly.
///
/// Whether contribution is enabled at all stays with
/// <see cref="CtdbSubmissionPolicy"/> in CUETools.Processor, whose own opt-in contract is
/// pinned by RequestedDefaultsTests. These tests check that eligibility never contradicts
/// it.
/// </summary>
[TestClass]
public sealed class CtdbSubmissionEligibilityTests
{
    private static CUEConfig FirstRun()
    {
        var config = new CUEConfig();
        config.advanced.CTDBAsk = true;
        config.advanced.CTDBSubmit = false;
        return config;
    }

    private static CUEConfig Remembered(bool submit)
    {
        var config = new CUEConfig();
        config.advanced.CTDBAsk = false;
        config.advanced.CTDBSubmit = submit;
        return config;
    }

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
        CtdbSubmissionDecision decision =
            CtdbSubmissionEligibility.Decide(CleanRip(), FirstRun());

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

        CtdbSubmissionDecision decision =
            CtdbSubmissionEligibility.Decide(candidate, FirstRun());

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
            CtdbSubmissionEligibility.Decide(candidate, FirstRun()).Block);
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
            CtdbSubmissionEligibility.Decide(candidate, FirstRun()).Block);
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
            CtdbSubmissionEligibility.Decide(candidate, FirstRun()).Block);
    }

    [TestMethod]
    public void AnIncompleteRunBlocksSubmission()
    {
        var candidate = new CtdbSubmissionCandidate { RunCompleted = false, Album = "Album" };

        Assert.AreEqual(
            CtdbSubmissionBlock.RunIncomplete,
            CtdbSubmissionEligibility.Decide(candidate, FirstRun()).Block);
    }

    [TestMethod]
    public void ARememberedYesSubmitsWithoutAskingAgain()
    {
        CUEConfig config = Remembered(submit: true);

        CtdbSubmissionDecision decision =
            CtdbSubmissionEligibility.Decide(CleanRip(), config);

        Assert.IsTrue(decision.Eligible);
        Assert.IsFalse(decision.NeedsPrompt);
    }

    [TestMethod]
    public void ARememberedNoStopsEveryFurtherSubmission()
    {
        CUEConfig config = Remembered(submit: false);

        Assert.AreEqual(
            CtdbSubmissionBlock.DeclinedPreviously,
            CtdbSubmissionEligibility.Decide(CleanRip(), config).Block);
    }

    [TestMethod]
    public void QualityBlocksOutrankARememberedYes()
    {
        CUEConfig config = Remembered(submit: true);
        var candidate = new CtdbSubmissionCandidate
        {
            RunCompleted = true,
            Salvaged = true,
            Album = "Album"
        };

        Assert.AreEqual(
            CtdbSubmissionBlock.Salvaged,
            CtdbSubmissionEligibility.Decide(candidate, config).Block);
    }

    [TestMethod]
    public void RememberingAnAnswerStopsTheAskingEitherWay()
    {
        CUEConfig yes = FirstRun();
        CtdbSubmissionEligibility.Remember(yes, submit: true);
        Assert.IsFalse(yes.advanced.CTDBAsk);
        Assert.IsTrue(yes.advanced.CTDBSubmit);
        // The shared consent boundary must agree, or one question has two answers.
        Assert.IsTrue(CtdbSubmissionPolicy.IsEnabled(yes));

        CUEConfig no = FirstRun();
        no.advanced.CTDBSubmit = true;
        CtdbSubmissionEligibility.Remember(no, submit: false);
        Assert.IsFalse(no.advanced.CTDBAsk);
        Assert.IsFalse(no.advanced.CTDBSubmit);
        Assert.IsFalse(CtdbSubmissionPolicy.IsEnabled(no));
    }

    [TestMethod]
    public void EligibilityCannotTurnContributionOnByItself()
    {
        // A default profile has never opted in. Eligibility must not manufacture consent
        // from a clean rip: with the prompt disarmed and the answer unset, the disc is
        // blocked, and the shared boundary still reports contribution as off.
        var config = new CUEConfig();
        config.advanced.CTDBAsk = false;
        config.advanced.CTDBSubmit = false;

        Assert.AreEqual(
            CtdbSubmissionBlock.DeclinedPreviously,
            CtdbSubmissionEligibility.Decide(CleanRip(), config).Block);
        Assert.IsFalse(CtdbSubmissionPolicy.IsEnabled(config));
    }

    [TestMethod]
    public void TheSubmittedQualityIsTheClassicConstant()
    {
        Assert.AreEqual(100, CtdbSubmissionEligibility.CleanReadQuality);
    }
}
