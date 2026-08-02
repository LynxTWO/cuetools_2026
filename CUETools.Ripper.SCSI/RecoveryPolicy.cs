namespace CUETools.Ripper.SCSI
{
    /// <summary>Pure decision for the deep-recovery progress-aware cap. No SCSI, no state beyond the
    /// window's best-error history, so it is unit-tested with no drive. Call StartWindow() when a new
    /// window's re-reads begin, then ShouldContinue(currentErrors, elapsedSeconds) once per completed
    /// pass. Returns false to stop re-reading. Never affects the vote or the accepted bytes.</summary>
    public sealed class RecoveryPolicy
    {
        public const int PlateauPasses = 8;       // stop after this many passes with no new best
        public const double CeilingSeconds = 120; // hard wall-clock stop per window
        /// <summary>Absolute wall-clock budget for ONE window, enforced inside the chunk loop
        /// instead of between passes (R123). Every rule above is evaluated once per COMPLETED
        /// pass, so none of them can stop a window whose single pass runs for hours - observed
        /// live: each 16-sector batch of a window failed and decomposed into 16 slow
        /// single-sector reads, one pass consumed more than six hours, and the app logged
        /// nothing the whole time because logging is per pass. A window that has spent this
        /// long has stopped being recovery and become a hang; it ends through the normal
        /// give-up path with its unresolved sectors flagged. Generous on purpose: a legitimate
        /// stuck window that converged on the live matrix took about five minutes.</summary>
        public const double WindowHardBudgetSeconds = 1200;

        /// <summary>True when this window has consumed its absolute wall-clock budget.</summary>
        public static bool WindowBudgetExhausted(double elapsedSeconds)
        {
            return elapsedSeconds >= WindowHardBudgetSeconds;
        }

        /// <summary>Absolute pass cap for a deep-recovery window (R107). Every per-sector vote
        /// accumulator is 8-bit: the UserData bit lanes carry into their neighbor at 256
        /// observations, and C2Count and the retry entries wrap at 256. The cap keeps pass counts
        /// (and the pass + 2 retry encoding) strictly inside those bounds; the plateau and time
        /// ceiling almost always stop a window first.</summary>
        public const int MaxPasses = 252;

        private int _best = int.MaxValue;
        private int _sinceImproved;

        public void StartWindow() { _best = int.MaxValue; _sinceImproved = 0; }

        public bool ShouldContinue(int currentErrors, double elapsedSeconds)
        {
            if (currentErrors <= 0) return false;               // converged
            if (elapsedSeconds >= CeilingSeconds) return false; // time ceiling
            if (currentErrors < _best) { _best = currentErrors; _sinceImproved = 0; }
            else if (++_sinceImproved >= PlateauPasses) return false; // plateau
            return true;
        }
    }
}
