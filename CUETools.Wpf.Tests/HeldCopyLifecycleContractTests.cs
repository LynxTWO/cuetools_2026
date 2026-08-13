using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// R108: the held Copy is the only completed encoded result and must never be silently deleted.
/// A stop during the confirming read holds it; a tray event or eject parks it keyed to its disc;
/// only a different disc, a new job, or an explicit Discard frees the staging. Source contracts,
/// matching the repo's dead-switch test style.
/// </summary>
[TestClass]
public sealed class HeldCopyLifecycleContractTests
{
    private static string Source(string project, string folder, string file)
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(System.AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        return File.ReadAllText(Path.Combine(root, project, folder, file));
    }

    [TestMethod]
    public void RipService_HoldsTheCompletedCopyWhenTheConfirmingReadStops()
    {
        string source = Source("CUETools.App.Core", "Services", "RipService.cs");

        Assert.IsFalse(source.Contains("return Fail(thirdResult.Error)"),
            "a Stopped confirming read must never take the Fail path that deletes the staged Copy");
        StringAssert.Contains(source, "\"Stopped during the confirming read.\",");
        StringAssert.Contains(source, "\"Stopped before the confirming read.\",");
        Assert.AreEqual(2, CountOf(source, "honorStop: false);"),
            "exactly the two confirm-phase stop routes bypass the hold-time stop guard");
        StringAssert.Contains(source, "if (honorStop) ThrowIfStopRequested();",
            "every other hold still cancels outright when the user stopped before a Copy existed");
    }

    [TestMethod]
    public void RipViewModel_ParksTheHeldCopyInsteadOfDeletingIt()
    {
        string source = Source("CUETools.Wpf", "ViewModels", "RipViewModel.cs");

        StringAssert.Contains(source, "_parkedHeldResult = _heldResult;",
            "a tray/disc-view clear parks the held result");
        Assert.IsFalse(source.Contains("try { _rip.DiscardStaging(_heldResult); } catch { }"),
            "no tray-driven path may delete the live held staging directly");
        StringAssert.Contains(source, "if (_parkedHeldDiscId == discId)",
            "the parked result is restored only for the disc that produced it");
        Assert.AreEqual(2, CountOf(source, "DiscardParkedHeld(\"a new job started\")"),
            "both job entry points free a parked result the user moved on from");
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
