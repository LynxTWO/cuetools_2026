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
            // 0 means no speed was ever requested (drive default, normally fast); an unknown
            // read context keeps the tight baseline.
            if (appliedSpeedKbps <= 0)
                return baselineSeconds;
            // A LOW APPLIED SPEED IS ITSELF THE RECOVERY SIGNAL, independent of whether this
            // window has recorded errors yet. The drive only reads this slowly because adaptive
            // speed stepped down, deep recovery hit the floor, or salvage pinned the minimum -
            // all deliberate responses to difficult media. Gating on recorded errors alone
            // missed the observed shape: the FIRST pass of a fresh window at pinned minimum
            // speed on a defective zone exceeded the baseline and the OS killed the ioctl
            // (ERROR_SEM_TIMEOUT) before the drive answered. A re-read of known trouble also
            // qualifies at any speed; both paths stay bounded by RecoverySeconds.
            if (appliedSpeedKbps <= SlowSpeedKbps || windowInRecovery)
                return baselineSeconds >= RecoverySeconds ? baselineSeconds : RecoverySeconds;
            return baselineSeconds;
        }
    }
}
