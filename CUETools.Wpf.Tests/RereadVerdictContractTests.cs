using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// R110: StopOnUnrecoverable and the Unreadable badge must key on the engine's one-shot give-up
/// verdict (WindowGivenUpSectors), never on running mid-pass counts or a pass threshold. A
/// mid-pass latch aborts jobs whose final pass would still converge and cuts the deep-recovery
/// extension short. Source contracts, matching the repo's dead-switch test style.
/// </summary>
[TestClass]
public sealed class RereadVerdictContractTests
{
    [TestMethod]
    public void RipViewModel_StopsOnEngineVerdictOnly()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(System.AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        string source = File.ReadAllText(
            Path.Combine(root, "CUETools.Wpf", "ViewModels", "RipViewModel.cs"));

        StringAssert.Contains(source, "bool failed = r.WindowGivenUpSectors > 0;",
            "the unreadable/stop latch must key on the engine's give-up verdict");
        Assert.IsFalse(source.Contains(">= RereadMax && errs > 0"),
            "the legacy mid-pass latch (reread threshold on a running error count) must not return");
        Assert.AreEqual(1, CountOf(source, "MakeRereadHandler(System.Windows.Threading.Dispatcher"),
            "exactly one shared reread handler; the two job paths must not fork the latch again");
    }

    [TestMethod]
    public void RipService_ForwardsTheEngineVerdictAndCountsItOnce()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(System.AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        string source = File.ReadAllText(
            Path.Combine(root, "CUETools.Wpf", "Services", "RipService.cs"));

        StringAssert.Contains(source, "if (e.WindowGivenUpSectors > 0)",
            "failedWindows must count engine verdicts, not a mid-pass reread heuristic");
        StringAssert.Contains(source, "e.WindowGivenUpSectors));",
            "the verdict must be forwarded to the UI reread report");
        Assert.IsFalse(source.Contains("reReads >= maxReReads && e.ErrorsCount > 0"),
            "the legacy mid-pass gave-up heuristic must not return");
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
