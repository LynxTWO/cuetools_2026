namespace CUETools.Ripper.SCSI
{
    /// <summary>Pure per-command READ CD timeout decision (R118). No SCSI, no state, unit-tested
    /// with no drive. The observed failure shape: in a slipping zone at low speed the drive's
    /// internal servo hunting exceeds the fixed 10 s pass-through timeout, the port driver
    /// resets the device, and the read dies as IoctlFailed with Win32 error 121
    /// (ERROR_SEM_TIMEOUT) - killing a 25-minute job. Extending the timeout ONLY for the exact
    /// observed shape (a window already in re-read recovery, at low speed) lets the drive finish
    /// hunting, while healthy reads keep the tight baseline so a genuinely dead drive still
    /// fails fast.</summary>
    public static class ReadTimeoutPolicy
    {
        /// <summary>Speeds at or below this are the low-speed recovery territory where both
        /// live error-121 deaths occurred (observed at 1408 kB/s and at the 44 kB/s floor).</summary>
        public const int SlowSpeedKbps = 1500;

        /// <summary>Bounded ceiling for a grinding recovery read. The worst added wait on a
        /// genuinely dead drive is this many seconds, paid only inside a damaged-zone grind.</summary>
        public const int RecoverySeconds = 45;

        /// <summary>Compute the timeout for one payload READ CD.</summary>
        /// <param name="baselineSeconds">The configured command timeout (default 10 s).</param>
        /// <param name="appliedSpeedKbps">The last speed the drive accepted; 0 = never set.</param>
        /// <param name="windowInRecovery">True when the current window still has disagreeing
        /// sectors, i.e. this is a re-read of known trouble.</param>
        public static int SecondsFor(int baselineSeconds, int appliedSpeedKbps, bool windowInRecovery)
        {
            if (!windowInRecovery)
                return baselineSeconds;
            // 0 means no speed was ever requested (drive default, normally fast). Only the
            // OBSERVED low-speed recovery shape gets the extension; unknown stays baseline.
            if (appliedSpeedKbps <= 0 || appliedSpeedKbps > SlowSpeedKbps)
                return baselineSeconds;
            return baselineSeconds >= RecoverySeconds ? baselineSeconds : RecoverySeconds;
        }
    }
}
