using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    [TestClass]
    public class SlipCorrelatorTests
    {
        // A deterministic, non-repeating (within n < 4000) signal so correlation peaks are unambiguous.
        private static short[] Ramp(int n, int start)
        {
            var a = new short[n];
            for (int i = 0; i < n; i++) a[i] = (short)((start + i) * 7 % 4000 - 2000);
            return a;
        }

        [TestMethod]
        public void ZeroOffsetForIdentical()
        {
            var a = Ramp(2000, 0);
            var (off, str) = SlipCorrelator.FindOffset(a, (short[])a.Clone(), 64);
            Assert.AreEqual(0, off);
            Assert.IsTrue(str >= SlipCorrelator.MinStrength, $"identical should be strong, was {str}");
        }

        [TestMethod]
        public void DetectsShift()
        {
            var reference = Ramp(2000, 0);
            // candidate is reference delayed by 5 samples (leading zeros). candidate[i+5] == reference[i],
            // so the best shift that aligns candidate onto reference is +5.
            var candidate = new short[2000];
            for (int i = 5; i < 2000; i++) candidate[i] = reference[i - 5];
            var (off, str) = SlipCorrelator.FindOffset(reference, candidate, 64);
            Assert.AreEqual(5, off);
            Assert.IsTrue(str >= SlipCorrelator.MinStrength, $"a clean shift should be strong, was {str}");
        }

        [TestMethod]
        public void WeakForUnrelatedGarbage()
        {
            var reference = Ramp(2000, 0);
            var rnd = new System.Random(1);
            var garbage = new short[2000];
            for (int i = 0; i < garbage.Length; i++) garbage[i] = (short)rnd.Next(-2000, 2000);
            var (_, str) = SlipCorrelator.FindOffset(reference, garbage, 64);
            Assert.IsTrue(str < SlipCorrelator.MinStrength, $"garbage should be weak, was {str}");
        }
    }
}
