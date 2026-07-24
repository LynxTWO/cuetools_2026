using System;

namespace CUETools.Ripper.SCSI
{
    /// <summary>Pure cross-correlation to detect read misalignment (jitter) in a persistent-slip
    /// window. Given a reference read and a candidate read of the same window, finds the sample
    /// shift that best aligns the candidate and how strong that alignment is. No SCSI - unit-tested
    /// with no drive. This only PROPOSES an alignment; the unchanged clean-agreement vote still
    /// decides what is accepted, so a wrong offset costs a failed recovery, never wrong data.</summary>
    public static class SlipCorrelator
    {
        public const double MinStrength = 0.9;   // below this, no reliable alignment -> treat as destruction

        /// <summary>Best shift of candidate against reference within +/-maxShift, and its normalized
        /// correlation in [0,1]. The returned offset s is the value where candidate[i+s] best matches
        /// reference[i], so shifting the candidate LEFT by s realigns it onto the reference.</summary>
        public static (int offset, double strength) FindOffset(short[] reference, short[] candidate, int maxShift)
        {
            int n = Math.Min(reference.Length, candidate.Length);
            if (n == 0) return (0, 0);
            double bestCorr = double.NegativeInfinity; int bestOff = 0;
            for (int shift = -maxShift; shift <= maxShift; shift++)
            {
                double dot = 0, er = 0, ec = 0; int count = 0;
                for (int i = 0; i < n; i++)
                {
                    int j = i + shift;
                    if (j < 0 || j >= n) continue;
                    double r = reference[i], c = candidate[j];
                    dot += r * c; er += r * r; ec += c * c; count++;
                }
                if (count < n / 2) continue;                  // too little overlap to trust
                double denom = Math.Sqrt(er * ec);
                double corr = denom > 0 ? dot / denom : 0;     // normalized [-1,1]
                if (corr > bestCorr) { bestCorr = corr; bestOff = shift; }
            }
            return (bestOff, Math.Max(0, bestCorr));
        }
    }
}
