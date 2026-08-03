using System.Collections;
using CUETools.Ripper;
using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    /// <summary>
    /// R123: a window's wall-clock budget. Every other recovery rule is evaluated between
    /// passes, so none of them can stop a window whose SINGLE pass grinds for hours - observed
    /// live: one pass ran past six hours while the app logged nothing. The budget is checked
    /// inside the chunk loop, and a window cut mid-pass must still account for the sectors the
    /// cut pass never revisited.
    /// </summary>
    [TestClass]
    public class WindowBudgetTests
    {
        [TestMethod]
        public void BudgetIsGenerousButFinite()
        {
            Assert.IsFalse(RecoveryPolicy.WindowBudgetExhausted(0));
            Assert.IsFalse(RecoveryPolicy.WindowBudgetExhausted(300),
                "a legitimate stuck window converged in about five minutes on the live matrix");
            Assert.IsTrue(RecoveryPolicy.WindowBudgetExhausted(
                RecoveryPolicy.WindowHardBudgetSeconds));
            Assert.IsTrue(RecoveryPolicy.WindowBudgetExhausted(6 * 3600),
                "the observed six-hour grind must be long past the budget");
            Assert.IsTrue(
                RecoveryPolicy.WindowHardBudgetSeconds > RecoveryPolicy.CeilingSeconds,
                "the hard budget is a safety valve above the progress-aware ceiling, not a replacement");
        }

        private static byte[] Seeded(int length, int correctionQuality)
        {
            var counts = new byte[length];
            for (int i = 0; i < length; i++)
                counts[i] = (byte)(correctionQuality + 1);
            return counts;
        }

        [TestMethod]
        public void APassCutMidWindowAccountsForBothHalves()
        {
            // Budget fired after sector 4 of a 0..8 window on pass 20.
            const int cq = 2, lastPass = 20, cut = 4;
            var retry = Seeded(8, cq);
            retry[1] = (byte)(lastPass + 2);   // re-read this pass, still failing
            retry[2] = (byte)(lastPass + 1);   // converged THIS pass (stale flag from pass 19)
            retry[6] = (byte)(lastPass + 1);   // never revisited: still unresolved from pass 19
            retry[7] = (byte)(lastPass);       // resolved earlier, quiet since
            var failed = new BitArray(8);

            int marked = FailedSectorAccounting.FinalizeWindow(
                retry, 0, 0, 8, lastPass, failed, partialFromSector: cut);

            Assert.IsTrue(failed[1], "flagged on the cut pass");
            Assert.IsFalse(failed[2], "below the cut it was re-read and converged");
            Assert.IsTrue(failed[6], "above the cut its last verdict still stands");
            Assert.IsFalse(failed[7]);
            Assert.AreEqual(2, marked);
        }

        [TestMethod]
        public void ACompletedPassIsUnaffectedByTheNewParameter()
        {
            const int cq = 2, lastPass = 30;
            var retry = Seeded(6, cq);
            retry[3] = (byte)(lastPass + 2);
            retry[4] = (byte)(lastPass + 1);   // converged on the final complete pass
            var failed = new BitArray(6);

            int marked = FailedSectorAccounting.FinalizeWindow(
                retry, 0, 0, 6, lastPass, failed);

            Assert.AreEqual(1, marked, "the default keeps the completed-pass rule exactly");
            Assert.IsTrue(failed[3]);
            Assert.IsFalse(failed[4]);
        }
    }
}
