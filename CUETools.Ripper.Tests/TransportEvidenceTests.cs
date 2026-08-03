using Bwg.Logging;
using Bwg.Scsi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    /// <summary>
    /// R112: transport evidence must stay honest when the hardware gives less than expected.
    /// Absent autosense reads as NoSense/0/0 instead of a NullReferenceException that would
    /// replace the SCSI failure identity, and 0/0 can never match a retry classifier.
    /// </summary>
    [TestClass]
    public class TransportEvidenceTests
    {
        [TestMethod]
        public void AbsentSenseReadsAsNoSenseInsteadOfThrowing()
        {
            // A Device that has never completed a command has no sense buffer - the same state
            // a CHECK CONDITION with failed autosense leaves behind.
            var device = new Device(new Logger());

            Assert.AreEqual(Device.SenseKeyType.NoSense, device.GetSenseKey());
            Assert.AreEqual((byte)0, device.GetSenseAsc());
            Assert.AreEqual((byte)0, device.GetSenseAscq());
        }

        [TestMethod]
        public void NoCompletedCommandMeansZeroTransferredBytes()
        {
            var device = new Device(new Logger());
            Assert.AreEqual(0u, device.LastDataTransferLength,
                "an exact-length payload guard must never accept a command that did not complete");
        }

        [TestMethod]
        public void AbsentSenseNeverMatchesTheBoundedRetryClassifiers()
        {
            // The R105 communication retry and the R57 transition retry both require exact
            // nonzero sense identities; the defensive NoSense/0/0 reading must stay outside
            // every retry-eligible shape.
            Assert.IsFalse(CUETools.Ripper.SCSI.PayloadReadFailurePolicy.ShouldRetryAfterControlTransition(
                transitionPending: true,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.NoSense,
                asc: 0,
                ascq: 0));
        }
    }
}
