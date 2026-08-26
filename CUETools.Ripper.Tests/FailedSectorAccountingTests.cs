using System.Collections;
using CUETools.Ripper;
using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    /// <summary>
    /// R106/R107: the secure loop's give-up accounting. A sector is "failed" when it was still
    /// flagged on the window's final executed pass - regardless of how many passes deep recovery
    /// added - and never merely because its retry count matches a magic pass number.
    /// </summary>
    [TestClass]
    public class FailedSectorAccountingTests
    {
        private static byte[] SeededRetryCounts(int length, int correctionQuality)
        {
            var counts = new byte[length];
            for (int i = 0; i < length; i++)
                counts[i] = (byte)(correctionQuality + 1);
            return counts;
        }

        [TestMethod]
        public void MarksOnlySectorsFlaggedOnFinalPass()
        {
            const int cq = 2;
            var retry = SeededRetryCounts(10, cq);
            const int lastPass = 40;
            retry[2] = (byte)(lastPass + 2);   // still failing when the loop stopped
            retry[7] = (byte)(lastPass + 2);
            retry[4] = (byte)(lastPass + 1);   // converged on the final pass
            var failed = new BitArray(10);

            int marked = FailedSectorAccounting.FinalizeWindow(retry, 0, 0, 10, lastPass, failed);

            Assert.AreEqual(2, marked);
            Assert.IsTrue(failed[2]);
            Assert.IsTrue(failed[7]);
            Assert.IsFalse(failed[4], "a sector that converged on the final pass is not failed");
            Assert.AreEqual(2, failed.PopulationCount());
        }

        [TestMethod]
        public void BaselineCapMatchesLegacySentinel()
        {
            // Without deep recovery the loop's last pass is max_scans - 1, so a still-failing
            // sector holds (max_scans - 1) + 2 == (16 << cq) + 1: the exact legacy sentinel.
            const int cq = 1;
            int maxScans = 16 << cq;
            var retry = SeededRetryCounts(4, cq);
            retry[1] = (byte)((16 << cq) + 1);
            var failed = new BitArray(4);

            int marked = FailedSectorAccounting.FinalizeWindow(retry, 0, 0, 4, maxScans - 1, failed);

            Assert.AreEqual(1, marked);
            Assert.IsTrue(failed[1]);
        }

        [TestMethod]
        public void DeepRecoveryOvershootIsStillMarked()
        {
            // R106 regression: the legacy getter compared against the exact baseline sentinel,
            // so a window that extended past max_scans and never converged reported clean.
            const int cq = 2;
            int maxScans = 16 << cq;                 // 64
            const int lastPass = 70;                 // extension zone
            var retry = SeededRetryCounts(6, cq);
            retry[3] = (byte)(lastPass + 2);         // 72 != legacy sentinel 65
            Assert.AreNotEqual((16 << cq) + 1, lastPass + 2, "test shape: overshoot must miss the legacy sentinel");
            var failed = new BitArray(6);

            int marked = FailedSectorAccounting.FinalizeWindow(retry, 0, 0, 6, lastPass, failed);

            Assert.AreEqual(1, marked);
            Assert.IsTrue(failed[3], "never-converged sector past the baseline cap must be failed");
            Assert.IsTrue(lastPass >= maxScans, "test shape: the window really extended");
        }

        [TestMethod]
        public void LateConvergedSectorIsNotMarked()
        {
            // The converse R106 case: last failed exactly at the baseline sentinel pass, then
            // converged during the extension. The legacy getter would have flagged it.
            const int cq = 2;
            var retry = SeededRetryCounts(6, cq);
            retry[3] = (byte)((16 << cq) + 1);       // == legacy sentinel 65
            const int lastPass = 70;
            var failed = new BitArray(6);

            int marked = FailedSectorAccounting.FinalizeWindow(retry, 0, 0, 6, lastPass, failed);

            Assert.AreEqual(0, marked);
            Assert.IsFalse(failed[3], "a sector that converged during the extension is not failed");
        }

        [TestMethod]
        public void LeadInOverreadSectorsHaveNoFailedSlot()
        {
            // With lead-in overread the retry array starts below sector 0; failed bits only
            // cover the audio range, so negative sectors are skipped without throwing.
            const int cq = 1;
            const int retrySectorBase = -2;
            var retry = SeededRetryCounts(7, cq);    // sectors -2..4
            const int lastPass = 31;
            retry[0] = (byte)(lastPass + 2);         // sector -2: no failed slot
            retry[3] = (byte)(lastPass + 2);         // sector 1: audio
            var failed = new BitArray(5);

            int marked = FailedSectorAccounting.FinalizeWindow(retry, retrySectorBase, -2, 5, lastPass, failed);

            Assert.AreEqual(1, marked);
            Assert.IsTrue(failed[1]);
            Assert.AreEqual(1, failed.PopulationCount());
        }

        [TestMethod]
        public void AlreadyMarkedSectorIsNotCountedAgain()
        {
            const int cq = 1;
            var retry = SeededRetryCounts(3, cq);
            const int lastPass = 31;
            retry[1] = (byte)(lastPass + 2);
            var failed = new BitArray(3);
            failed[1] = true;

            int marked = FailedSectorAccounting.FinalizeWindow(retry, 0, 0, 3, lastPass, failed);

            Assert.AreEqual(0, marked, "a bit set by an earlier finalize is not new damage");
            Assert.IsTrue(failed[1]);
        }

        [TestMethod]
        public void WindowEndIsExclusiveAndRetryIndexZeroIsValid()
        {
            const int lastPass = 5;
            var retry = new byte[4];
            retry[0] = (byte)(lastPass + 2);
            retry[3] = (byte)(lastPass + 2);
            var failed = new BitArray(4);

            int marked = FailedSectorAccounting.FinalizeWindow(
                retry, 0, 0, 3, lastPass, failed);

            Assert.AreEqual(1, marked);
            Assert.IsTrue(failed[0]);
            Assert.IsFalse(failed[3]);
        }

        [TestMethod]
        public void RetryAndFailedArrayUpperBoundsAreSkippedWithoutReadingPastThem()
        {
            const int lastPass = 5;
            var retry = new byte[2];
            retry[1] = (byte)(lastPass + 2);
            var failed = new BitArray(1);

            int marked = FailedSectorAccounting.FinalizeWindow(
                retry, 0, 0, 3, lastPass, failed);

            Assert.AreEqual(0, marked);
            Assert.AreEqual(0, failed.PopulationCount());
        }

        [TestMethod]
        public void PartialPassBoundaryUsesThePreviousFlagAtAndAfterTheCut()
        {
            const int lastPass = 5;
            var retry = new byte[4];
            retry[1] = (byte)(lastPass + 1);
            retry[2] = (byte)(lastPass + 1);
            var failed = new BitArray(4);

            int marked = FailedSectorAccounting.FinalizeWindow(
                retry,
                retrySectorBase: 0,
                windowStartSector: 0,
                windowEndSector: 4,
                lastExecutedPass: lastPass,
                failedSectors: failed,
                partialFromSector: 2);

            Assert.AreEqual(1, marked);
            Assert.IsFalse(failed[1]);
            Assert.IsTrue(failed[2]);
        }

        [TestMethod]
        public void DeepRecoveryPassCapProtectsByteAccumulators()
        {
            // R107: every per-sector accumulator is 8-bit (UserData bit lanes carry into their
            // neighbor at 256 observations, C2Count and m_retryCount wrap at 256). The deep
            // recovery pass cap must keep pass counts strictly below those wrap points, with
            // room for the pass + 2 retry encoding.
            Assert.IsTrue(RecoveryPolicy.MaxPasses + 2 <= byte.MaxValue,
                "retry encoding pass + 2 must fit a byte");
            Assert.IsTrue(RecoveryPolicy.MaxPasses < 256,
                "bit lanes carry into the neighboring lane at 256 observations");
            Assert.IsTrue(RecoveryPolicy.MaxPasses >= 16 << 3,
                "the cap must never cut baseline Paranoid (max_scans = 128) short");
        }

        [TestMethod]
        public void ProgressArgsCarryNoGiveUpVerdictByDefault()
        {
            Assert.AreEqual(-1, new ReadProgressArgs().WindowGivenUpSectors,
                "like the slip verdict, the give-up verdict is surfaced once, not on every event");
        }
    }
}
