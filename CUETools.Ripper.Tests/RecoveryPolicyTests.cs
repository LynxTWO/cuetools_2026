using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    [TestClass]
    public class RecoveryPolicyTests
    {
        [TestMethod]
        public void StopsImmediatelyWhenConverged()
        {
            var p = new RecoveryPolicy(); p.StartWindow();
            Assert.IsFalse(p.ShouldContinue(0, 1.0), "errors==0 must stop");
        }

        [TestMethod]
        public void ContinuesWhileImproving()
        {
            var p = new RecoveryPolicy(); p.StartWindow();
            int[] seq = { 400, 300, 200, 150, 100, 60, 30, 10, 5, 2 };
            double t = 0;
            foreach (var e in seq) { t += 1; Assert.IsTrue(p.ShouldContinue(e, t), $"still improving at {e}"); }
        }

        [TestMethod]
        public void StopsAfterPlateau()
        {
            var p = new RecoveryPolicy(); p.StartWindow();
            // improve to 100, then flatline; best never beats 100 again
            p.ShouldContinue(100, 1);
            for (int i = 0; i < RecoveryPolicy.PlateauPasses - 1; i++)
                Assert.IsTrue(p.ShouldContinue(100, 2 + i), "within plateau window");
            Assert.IsFalse(p.ShouldContinue(100, 50), "plateau exhausted -> stop");
        }

        [TestMethod]
        public void PlateauResetsOnNewBest()
        {
            var p = new RecoveryPolicy(); p.StartWindow();
            p.ShouldContinue(100, 1);
            for (int i = 0; i < RecoveryPolicy.PlateauPasses - 2; i++) p.ShouldContinue(100, 2 + i);
            Assert.IsTrue(p.ShouldContinue(90, 20), "new best resets the counter");
            for (int i = 0; i < RecoveryPolicy.PlateauPasses - 1; i++)
                Assert.IsTrue(p.ShouldContinue(90, 30 + i));
            Assert.IsFalse(p.ShouldContinue(90, 60));
        }

        [TestMethod]
        public void StopsAtTimeCeiling()
        {
            var p = new RecoveryPolicy(); p.StartWindow();
            Assert.IsFalse(p.ShouldContinue(50, RecoveryPolicy.CeilingSeconds + 1), "over the ceiling -> stop even if improving");
        }
    }
}
