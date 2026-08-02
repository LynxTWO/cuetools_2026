using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    /// <summary>
    /// R118: the extended READ CD timeout covers the reads that legitimately take a long time -
    /// anything at a deliberately low speed, plus any re-read of known trouble - so the OS stops
    /// killing ioctls the drive would have answered. Reads at full speed with no recorded
    /// trouble keep the tight baseline, so a genuinely dead drive still fails fast.
    /// </summary>
    [TestClass]
    public class ReadTimeoutPolicyTests
    {
        [TestMethod]
        public void HealthyFullSpeedReadsKeepTheBaseline()
        {
            Assert.AreEqual(10, ReadTimeoutPolicy.SecondsFor(10, 7040, windowInRecovery: false),
                "a clean read at full speed has no reason to hang");
            Assert.AreEqual(10, ReadTimeoutPolicy.SecondsFor(10, 0, windowInRecovery: true),
                "0 means no speed was ever requested - unknown context stays baseline");
        }

        [TestMethod]
        public void LowSpeedAloneEarnsTheExtension()
        {
            // The live H: salvage death: FIRST pass of a fresh window at the pinned 4x minimum,
            // no recorded errors yet, OS-killed at the 10 s baseline (Win32 121).
            Assert.AreEqual(ReadTimeoutPolicy.RecoverySeconds,
                ReadTimeoutPolicy.SecondsFor(10, 704, windowInRecovery: false),
                "a pinned-minimum read is recovery context even before any error is recorded");
            // The earlier deaths: 1408 kB/s and the 44 kB/s deep-recovery floor.
            Assert.AreEqual(ReadTimeoutPolicy.RecoverySeconds,
                ReadTimeoutPolicy.SecondsFor(10, 1408, windowInRecovery: true));
            Assert.AreEqual(ReadTimeoutPolicy.RecoverySeconds,
                ReadTimeoutPolicy.SecondsFor(10, 44, windowInRecovery: false));
            Assert.IsTrue(1408 <= ReadTimeoutPolicy.SlowSpeedKbps,
                "the threshold must cover every observed death speed");
        }

        [TestMethod]
        public void RereadsOfKnownTroubleQualifyAtAnySpeed()
        {
            Assert.AreEqual(ReadTimeoutPolicy.RecoverySeconds,
                ReadTimeoutPolicy.SecondsFor(10, 7040, windowInRecovery: true),
                "a window with disagreeing sectors is trouble even at full speed");
        }

        [TestMethod]
        public void AConfiguredLongerBaselineIsNeverShortened()
        {
            Assert.AreEqual(60, ReadTimeoutPolicy.SecondsFor(60, 44, windowInRecovery: true),
                "a user-configured timeout above the extension wins");
            Assert.AreEqual(60, ReadTimeoutPolicy.SecondsFor(60, 7040, windowInRecovery: false));
        }
    }
}
