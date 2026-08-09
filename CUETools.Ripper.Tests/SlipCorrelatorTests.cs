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
            SlipCorrelationResult result =
                SlipCorrelator.FindOffset(a, (short[])a.Clone(), 64);
            Assert.AreEqual(0, result.Offset);
            Assert.AreEqual(1.0, result.Strength, 1e-12);
        }

        [TestMethod]
        public void DetectsShift()
        {
            var reference = Ramp(2000, 0);
            // candidate is reference delayed by 5 samples (leading zeros). candidate[i+5] == reference[i],
            // so the best shift that aligns candidate onto reference is +5.
            var candidate = new short[2000];
            for (int i = 5; i < 2000; i++) candidate[i] = reference[i - 5];
            SlipCorrelationResult result =
                SlipCorrelator.FindOffset(reference, candidate, 64);
            Assert.AreEqual(5, result.Offset);
            Assert.AreEqual(1.0, result.Strength, 1e-12);
        }

        [TestMethod]
        public void DetectsNegativeShiftAtTheConfiguredBoundary()
        {
            var reference = Ramp(2000, 0);
            var candidate = new short[2000];
            for (int i = 0; i < candidate.Length - 5; i++)
                candidate[i] = reference[i + 5];

            SlipCorrelationResult result =
                SlipCorrelator.FindOffset(reference, candidate, 5);

            Assert.AreEqual(-5, result.Offset);
            Assert.AreEqual(1.0, result.Strength, 1e-12);
        }

        [TestMethod]
        public void ExactlyHalfOverlapRemainsEligible()
        {
            var reference = Ramp(8, 100);
            var candidate = new short[8];
            for (int i = 4; i < candidate.Length; i++)
                candidate[i] = reference[i - 4];

            SlipCorrelationResult result =
                SlipCorrelator.FindOffset(reference, candidate, 4);

            Assert.AreEqual(4, result.Offset);
            Assert.AreEqual(1.0, result.Strength, 1e-12);
        }

        [TestMethod]
        public void UnequalInputsUseOnlyTheirSharedLength()
        {
            short[] reference = Ramp(128, 0);
            short[] candidate = new short[64];
            System.Array.Copy(reference, candidate, candidate.Length);

            SlipCorrelationResult result =
                SlipCorrelator.FindOffset(reference, candidate, 0);

            Assert.AreEqual(0, result.Offset);
            Assert.AreEqual(1.0, result.Strength, 1e-12);
        }

        [TestMethod]
        public void TheFirstOverlappingSampleParticipatesInCorrelation()
        {
            short[] samples = { 1234, 0 };

            SlipCorrelationResult result =
                SlipCorrelator.FindOffset(samples, (short[])samples.Clone(), 0);

            Assert.AreEqual(0, result.Offset);
            Assert.AreEqual(1.0, result.Strength, 1e-12);
        }

        [TestMethod]
        public void EmptySharedInputHasNoAlignmentEvidence()
        {
            SlipCorrelationResult result =
                SlipCorrelator.FindOffset(System.Array.Empty<short>(), new short[8], 4);

            Assert.AreEqual(0, result.Offset);
            Assert.AreEqual(0.0, result.Strength);
        }

        [TestMethod]
        public void WeakForUnrelatedGarbage()
        {
            var reference = Ramp(2000, 0);
            var rnd = new System.Random(1);
            var garbage = new short[2000];
            for (int i = 0; i < garbage.Length; i++) garbage[i] = (short)rnd.Next(-2000, 2000);
            SlipCorrelationResult result =
                SlipCorrelator.FindOffset(reference, garbage, 64);
            Assert.IsTrue(
                result.Strength < SlipCorrelator.MinStrength,
                $"garbage should be weak, was {result.Strength}");
        }
    }
}
