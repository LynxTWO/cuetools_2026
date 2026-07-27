using Bwg.Scsi;

namespace CUETools.Ripper.SCSI
{
    /// <summary>
    /// Classifies READ CD payload failures without touching the drive or the secure vote.
    /// Medium errors describe unreadable media and may be represented as untrusted sectors.
    /// Transport, readiness, unit-attention, command, and hardware failures remain fatal.
    /// </summary>
    public static class PayloadReadFailurePolicy
    {
        public static bool IsMediumError(
            Device.CommandStatus status,
            Device.SenseKeyType senseKey)
        {
            return status == Device.CommandStatus.DeviceFailed &&
                senseKey == Device.SenseKeyType.MediumError;
        }

        public static bool ShouldSplitBatch(
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            if (status != Device.CommandStatus.DeviceFailed)
                return false;

            // 64/00 is the legacy "illegal mode for this track" fallback. Keep it
            // separate from medium errors because a whole batch of 64/00 failures
            // still means the selected READ CD command cannot read this region.
            return senseKey == Device.SenseKeyType.MediumError ||
                (asc == 0x64 && ascq == 0x00);
        }

        /// <summary>
        /// Some optical firmware briefly rejects the first READ CD after an accepted
        /// control-plane transition such as SET CD SPEED. Retry only the exact
        /// observed 24/00 rejection, only while that transition is still pending.
        /// A repeated rejection and every unrelated failure remain fatal.
        /// </summary>
        public static bool ShouldRetryAfterControlTransition(
            bool transitionPending,
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return transitionPending &&
                status == Device.CommandStatus.DeviceFailed &&
                senseKey == Device.SenseKeyType.IllegalRequest &&
                asc == 0x24 &&
                ascq == 0x00;
        }
    }
}
