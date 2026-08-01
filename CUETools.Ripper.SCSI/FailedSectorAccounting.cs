using System.Collections;

namespace CUETools.Ripper.SCSI
{
    /// <summary>Pure give-up bookkeeping for the secure read loop. No SCSI, no drive state, so it
    /// is unit-tested with no hardware. A sector "failed" when the window's pass loop stopped while
    /// the vote still flagged it, i.e. its retry entry records the final executed pass. The legacy
    /// scheme instead compared against the exact baseline sentinel (16 &lt;&lt; quality) + 1, which
    /// deep recovery's extension zone overshoots: never-converged sectors read as clean and
    /// late-converged sectors read as failed (R106).</summary>
    public static class FailedSectorAccounting
    {
        /// <summary>Mark, in <paramref name="failedSectors"/>, every window sector whose retry
        /// entry shows it was still flagged on the window's final executed pass. Call only for a
        /// window that ended with a nonzero error count. Sectors outside the failed array (lead-in
        /// or lead-out overread) are skipped. Returns the number of newly marked sectors.</summary>
        public static int FinalizeWindow(
            byte[] retryCount,
            int retrySectorBase,
            int windowStartSector,
            int windowEndSector,
            int lastExecutedPass,
            BitArray failedSectors)
        {
            // CorrectSectors stores pass + 2 for a sector flagged on that pass, so the final
            // executed pass leaves exactly this value behind. RecoveryPolicy.MaxPasses keeps
            // pass + 2 within a byte, so the cast cannot alias an earlier pass.
            byte stillFailing = (byte)(lastExecutedPass + 2);
            int marked = 0;
            for (int sector = windowStartSector; sector < windowEndSector; sector++)
            {
                int retryIndex = sector - retrySectorBase;
                if (retryIndex < 0 || retryIndex >= retryCount.Length)
                    continue;
                if (retryCount[retryIndex] != stillFailing)
                    continue;
                if (sector < 0 || sector >= failedSectors.Count)
                    continue;
                if (failedSectors[sector])
                    continue;
                failedSectors[sector] = true;
                marked++;
            }
            return marked;
        }
    }
}
