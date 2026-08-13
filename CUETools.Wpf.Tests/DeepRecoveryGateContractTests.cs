using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// Decision D9: deep recovery is a Secure/Paranoid capability. Burst keeps only the classic
/// 16-pass re-read cap, so it stays "fast until trouble" instead of grinding a defective disc
/// at the drive floor for an hour. Source contracts pin the gate in both layers.
/// </summary>
[TestClass]
public sealed class DeepRecoveryGateContractTests
{
    [TestMethod]
    public void EngineGatesDeepRecoveryToSecureAndParanoid()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(System.AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        string source = File.ReadAllText(
            Path.Combine(root, "CUETools.Ripper.SCSI", "SCSIDrive.cs"));

        StringAssert.Contains(source,
            "bool deepRecoveryActive = DeepRecovery && _correctionQuality > 0;",
            "the extension, slip probe, and StartWindow must all key on the gated value");
        Assert.IsFalse(source.Contains("int hard_cap = DeepRecovery ?"),
            "the pass cap must not consult the raw setting");
    }

    [TestMethod]
    public void RipServiceRecordsTheEffectiveDeepRecoveryState()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(System.AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        string source = File.ReadAllText(
            Path.Combine(root, "CUETools.App.Core", "Services", "RipService.cs"));

        StringAssert.Contains(source,
            "bool deepRecovery = _settings.DeepRecovery && cq > 0;",
            "floor-speed drops and the recorded DeepRecovery flag must match what actually ran");
    }
}
