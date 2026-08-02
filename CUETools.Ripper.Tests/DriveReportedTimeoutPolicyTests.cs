using Bwg.Scsi;
using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    /// <summary>
    /// R118: the drive's own per-sector surrender - HardwareError 3E/02 (TIMEOUT ON LOGICAL
    /// UNIT) on a single-sector pinpoint whose parent batch independently reported MediumError -
    /// enters the untrusted path instead of failing a nearly complete job. The code is ASSIGNED
    /// with standard semantics, so the gate is the corroboration set, not a drive identity;
    /// every other hardware failure stays fatal.
    /// </summary>
    [TestClass]
    public class DriveReportedTimeoutPolicyTests
    {
        private static bool Classify(
            bool mediumParent = true,
            int parentSectors = 16,
            int childSectors = 1,
            bool speedTransition = false,
            bool cacheTransition = false,
            Device.CommandStatus status = Device.CommandStatus.DeviceFailed,
            Device.SenseKeyType key = Device.SenseKeyType.HardwareError,
            byte asc = 0x3E,
            byte ascq = 0x02)
        {
            return PayloadReadFailurePolicy.IsDriveReportedTimeoutPinpoint(
                mediumParent, parentSectors, childSectors,
                speedTransition, cacheTransition,
                status, key, asc, ascq);
        }

        [TestMethod]
        public void TheObservedShapeIsAccepted()
        {
            // The live H: failure: sector 356562, 16-sector MediumError parent, single-sector
            // child, no transitions, HardwareError / 3E/02.
            Assert.IsTrue(Classify());
        }

        [TestMethod]
        public void EveryMissingCorroborationStaysFatal()
        {
            Assert.IsFalse(Classify(mediumParent: false),
                "no medium-error parent, no media corroboration");
            Assert.IsFalse(Classify(parentSectors: 1),
                "a single-sector parent has no batch corroboration");
            Assert.IsFalse(Classify(childSectors: 16),
                "only the exact pinpoint shape is covered");
            Assert.IsFalse(Classify(speedTransition: true),
                "a pending speed transition owns its own recovery");
            Assert.IsFalse(Classify(cacheTransition: true),
                "a pending cache transition owns its own recovery");
        }

        [TestMethod]
        public void EveryOtherFailureClassStaysFatal()
        {
            Assert.IsFalse(Classify(status: Device.CommandStatus.IoctlFailed,
                key: Device.SenseKeyType.NoSense, asc: 0, ascq: 0),
                "an OS-level ioctl death is transport, not a drive verdict");
            Assert.IsFalse(Classify(key: Device.SenseKeyType.MediumError, asc: 0x11, ascq: 0x05),
                "a medium-error child already has its own path");
            Assert.IsFalse(Classify(asc: 0x3E, ascq: 0x00),
                "3E/00 LOGICAL UNIT FAILURE is a different verdict than the timeout");
            Assert.IsFalse(Classify(asc: 0x08, ascq: 0x0A),
                "the unassigned communication qualifier keeps its own drive-gated policy");
            Assert.IsFalse(Classify(key: Device.SenseKeyType.IllegalRequest, asc: 0x24, ascq: 0x00),
                "24/00 keeps its own corroborated pinpoint policy");
        }
    }
}
