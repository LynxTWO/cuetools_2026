using Bwg.Scsi;
using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    /// <summary>
    /// R114: (1) a multi-sector 24/00 during a pending speed or cache-defeat transition is the
    /// documented transition rejection and must reach the one-shot transition retry - never be
    /// decomposed into per-sector media verdicts; (2) in the medium-error / legacy 64/00 batch
    /// split, only a media-class child (or, under the legacy parent, a child repeating the exact
    /// 64/00 track fault) may be marked untrusted - every other child class stays fatal.
    /// </summary>
    [TestClass]
    public class SplitChildAndTransitionPolicyTests
    {
        [TestMethod]
        public void PendingTransitionRefusesBatchDecomposition()
        {
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
                true,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00),
                "a transition-state 24/00 must reach the one-shot transition retry, not become media evidence");
            Assert.IsTrue(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
                false,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
        }

        [TestMethod]
        public void TransitionFlagChangesNothingForOtherFailures()
        {
            // The gate only reorders WHO handles the exact 24/00 shape; every other failure was
            // already outside decomposition and stays outside it.
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
                true,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.MediumError,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
                true,
                16,
                Device.CommandStatus.IoctlFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
        }

        [TestMethod]
        public void MediumErrorChildMayBeMarkedUnderEitherParent()
        {
            Assert.IsTrue(PayloadReadFailurePolicy.MayMarkSplitChildUnreadable(
                mediumErrorParent: true, legacyTrackParent: false,
                Device.CommandStatus.DeviceFailed, Device.SenseKeyType.MediumError, 0x11, 0x05));
            Assert.IsTrue(PayloadReadFailurePolicy.MayMarkSplitChildUnreadable(
                mediumErrorParent: false, legacyTrackParent: true,
                Device.CommandStatus.DeviceFailed, Device.SenseKeyType.MediumError, 0x11, 0x05));
        }

        [TestMethod]
        public void LegacyParentAcceptsOnlyTheExactTrackFaultChild()
        {
            // Mixed-mode discs: a data track inside the audio range repeats 64/00 per sector.
            Assert.IsTrue(PayloadReadFailurePolicy.MayMarkSplitChildUnreadable(
                mediumErrorParent: false, legacyTrackParent: true,
                Device.CommandStatus.DeviceFailed, Device.SenseKeyType.IllegalRequest, 0x64, 0x00));
            // A 64/00 child under the MEDIUM-ERROR parent is a different class and stays fatal.
            Assert.IsFalse(PayloadReadFailurePolicy.MayMarkSplitChildUnreadable(
                mediumErrorParent: true, legacyTrackParent: false,
                Device.CommandStatus.DeviceFailed, Device.SenseKeyType.IllegalRequest, 0x64, 0x00));
        }

        [TestMethod]
        public void NonMediaChildClassesStayFatalUnderTheLegacyParent()
        {
            // The R114 defect: these fell through to MarkSectorUnreadable before.
            Assert.IsFalse(PayloadReadFailurePolicy.MayMarkSplitChildUnreadable(
                mediumErrorParent: false, legacyTrackParent: true,
                Device.CommandStatus.IoctlFailed, Device.SenseKeyType.NoSense, 0x00, 0x00),
                "transport failures are never media evidence");
            Assert.IsFalse(PayloadReadFailurePolicy.MayMarkSplitChildUnreadable(
                mediumErrorParent: false, legacyTrackParent: true,
                Device.CommandStatus.DeviceFailed, Device.SenseKeyType.HardwareError, 0x08, 0x03));
            Assert.IsFalse(PayloadReadFailurePolicy.MayMarkSplitChildUnreadable(
                mediumErrorParent: false, legacyTrackParent: true,
                Device.CommandStatus.DeviceFailed, Device.SenseKeyType.NotReady, 0x04, 0x01));
            Assert.IsFalse(PayloadReadFailurePolicy.MayMarkSplitChildUnreadable(
                mediumErrorParent: false, legacyTrackParent: true,
                Device.CommandStatus.DeviceFailed, Device.SenseKeyType.UnitAttention, 0x28, 0x00));
        }
    }
}
