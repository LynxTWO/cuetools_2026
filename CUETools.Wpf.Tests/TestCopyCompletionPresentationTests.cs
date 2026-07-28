using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class TestCopyCompletionPresentationTests
{
    [TestMethod]
    public void CleanAgreement_IsVerified()
    {
        Assert.AreEqual(
            TestCopyCompletionState.Verified,
            RipViewModel.ClassifyTestCopyCompletion(new TestCopyRunResult()));
    }

    [TestMethod]
    public void RecoverableCtdbDamage_RequiresRepairEvenWhenReadsAgree()
    {
        var result = new TestCopyRunResult
        {
            CtdbHasErrors = true,
            CtdbCanRecover = true,
            CtdbRepairSectors = 6,
            FailedWindows = 3
        };

        Assert.AreEqual(
            TestCopyCompletionState.ConsistentRepairable,
            RipViewModel.ClassifyTestCopyCompletion(result));
    }

    [TestMethod]
    public void ExhaustedReadWindowsWithoutRepair_AreConsistentDamaged()
    {
        var result = new TestCopyRunResult { FailedWindows = 1 };

        Assert.AreEqual(
            TestCopyCompletionState.ConsistentDamaged,
            RipViewModel.ClassifyTestCopyCompletion(result));
    }
}
