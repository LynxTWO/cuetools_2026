using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    /// <summary>
    /// R118: the extended READ CD timeout applies to exactly the observed ERROR_SEM_TIMEOUT
    /// shape - a re-read of a window still in recovery, at low speed - and nowhere else, so a
    /// genuinely dead drive keeps failing fast on every healthy path.
    /// </summary>
    [TestClass]
    public class ReadTimeoutPolicyTests
    {
        [TestMethod]
        public void HealthyReadsKeepTheBaseline()
        {
            Assert.AreEqual(10, ReadTimeoutPolicy.SecondsFor(10, 44, windowInRecovery: false),
                "a first pass at the floor is not the observed shape");
            Assert.AreEqual(10, ReadTimeoutPolicy.SecondsFor(10, 7040, windowInRecovery: true),
                "fast recovery reads answered within the baseline live");
            Assert.AreEqual(10, ReadTimeoutPolicy.SecondsFor(10, 0, windowInRecovery: true),
                "0 means no speed was ever requested - unknown stays baseline");
        }

        [TestMethod]
        public void LowSpeedRecoveryReadsGetTheBoundedExtension()
        {
            // Both live deaths: 1408 kB/s and the 44 kB/s floor.
            Assert.AreEqual(ReadTimeoutPolicy.RecoverySeconds,
                ReadTimeoutPolicy.SecondsFor(10, 44, windowInRecovery: true));
            Assert.AreEqual(ReadTimeoutPolicy.RecoverySeconds,
                ReadTimeoutPolicy.SecondsFor(10, 1408, windowInRecovery: true));
            Assert.IsTrue(1408 <= ReadTimeoutPolicy.SlowSpeedKbps,
                "the threshold must cover both observed death speeds");
        }

        [TestMethod]
        public void AConfiguredLongerBaselineIsNeverShortened()
        {
            Assert.AreEqual(60, ReadTimeoutPolicy.SecondsFor(60, 44, windowInRecovery: true),
                "a user-configured timeout above the extension wins");
        }
    }
}
