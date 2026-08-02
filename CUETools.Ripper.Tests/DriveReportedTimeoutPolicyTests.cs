using Bwg.Scsi;
using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    /// <summary>
    /// R118: the drive's own surrender - the assigned HardwareError 3E/02 (TIMEOUT ON LOGICAL
    /// UNIT) verdict - is media evidence, not a device death. A surrendered multi-sector batch
    /// decomposes to independent single-sector reads; a surrendered pinpoint inside a
    /// corroborated media batch (MediumError or 3E/02 parent) enters the untrusted path. The
    /// code is ASSIGNED with standard semantics, so the gates are corroboration sets, not drive
    /// identities, and they are transition-agnostic: a verdict the drive deliberated for tens
    /// of seconds is not a transition blip (both live observations prove the shapes - the H:
    /// pinpoint arrived with no transitions, the K: batch with a cache transition pending).
    /// Every other hardware failure stays fatal.
    /// </summary>
    [TestClass]
    public class DriveReportedTimeoutPolicyTests
    {
        private static bool Pinpoint(
            bool corroboratedParent = true,
            int parentSectors = 16,
            int childSectors = 1,
            Device.CommandStatus status = Device.CommandStatus.DeviceFailed,
            Device.SenseKeyType key = Device.SenseKeyType.HardwareError,
            byte asc = 0x3E,
            byte ascq = 0x02)
        {
            return PayloadReadFailurePolicy.IsDriveReportedTimeoutPinpoint(
                corroboratedParent, parentSectors, childSectors, status, key, asc, ascq);
        }

        private static bool Batch(
            int sectors = 16,
            Device.CommandStatus status = Device.CommandStatus.DeviceFailed,
            Device.SenseKeyType key = Device.SenseKeyType.HardwareError,
            byte asc = 0x3E,
            byte ascq = 0x02)
        {
            return PayloadReadFailurePolicy.IsDriveReportedTimeoutBatch(
                sectors, status, key, asc, ascq);
        }

        [TestMethod]
        public void BothObservedShapesAreAccepted()
        {
            // H: live - sector 356562, single-sector pinpoint under a 16-sector MediumError parent.
            Assert.IsTrue(Pinpoint());
            // K: live - sector 266400, the whole 16-sector batch surrendered directly.
            Assert.IsTrue(Batch());
        }

        [TestMethod]
        public void PinpointMissingCorroborationStaysFatal()
        {
            Assert.IsFalse(Pinpoint(corroboratedParent: false),
                "no MediumError or surrendered parent, no media corroboration");
            Assert.IsFalse(Pinpoint(parentSectors: 1),
                "a single-sector parent has no batch corroboration");
            Assert.IsFalse(Pinpoint(childSectors: 16),
                "only the exact pinpoint shape is covered");
        }

        [TestMethod]
        public void OnlyTheExactAssignedVerdictQualifies()
        {
            Assert.IsFalse(Pinpoint(status: Device.CommandStatus.IoctlFailed,
                key: Device.SenseKeyType.NoSense, asc: 0, ascq: 0),
                "an OS-level ioctl death is transport, not a drive verdict");
            Assert.IsFalse(Pinpoint(asc: 0x3E, ascq: 0x00),
                "3E/00 LOGICAL UNIT FAILURE is a different verdict than the timeout");
            Assert.IsFalse(Pinpoint(asc: 0x08, ascq: 0x0A),
                "the unassigned communication qualifier keeps its own drive-gated policy");
            Assert.IsFalse(Pinpoint(key: Device.SenseKeyType.IllegalRequest, asc: 0x24, ascq: 0x00),
                "24/00 keeps its own corroborated pinpoint policy");

            Assert.IsFalse(Batch(sectors: 1),
                "a single-sector 3E/02 with no parent has no corroboration and stays fatal");
            Assert.IsFalse(Batch(key: Device.SenseKeyType.MediumError),
                "a medium-error batch already has its own split path");
            Assert.IsFalse(Batch(ascq: 0x00),
                "3E/00 is a different verdict");
            Assert.IsFalse(Batch(status: Device.CommandStatus.IoctlFailed,
                key: Device.SenseKeyType.NoSense, asc: 0, ascq: 0));
        }

        [TestMethod]
        public void SurrenderedBatchCorroboratesItsOwnSurrenderedChildren()
        {
            // The K: unlock end-to-end: the batch surrenders, decomposes, and a child that
            // repeats the drive's surrender is marked untrusted rather than fatal.
            Assert.IsTrue(Batch());
            Assert.IsTrue(Pinpoint(corroboratedParent: true));
        }
    }
}
