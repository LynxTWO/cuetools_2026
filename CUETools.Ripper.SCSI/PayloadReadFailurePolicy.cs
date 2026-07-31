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
        /// A few drives reject an otherwise valid multi-sector READ CD transfer
        /// with 24/00 while accepting the same address range one sector at a time.
        /// This is a transfer-shape fallback, not damaged-media recovery: callers
        /// may continue only when every independently read sector succeeds.
        /// </summary>
        public static bool ShouldDecomposeRejectedPayloadBatch(
            int sectorCount,
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return sectorCount > 1 &&
                status == Device.CommandStatus.DeviceFailed &&
                senseKey == Device.SenseKeyType.IllegalRequest &&
                asc == 0x24 &&
                ascq == 0x00;
        }

        /// <summary>
        /// Some firmware reports a batch-level medium error, then transiently
        /// rejects the exact single-sector pinpoint read with 24/00. The parent
        /// medium error and valid multi-sector shape are required corroboration;
        /// an uncorroborated single-sector illegal request remains fatal.
        /// </summary>
        public static bool ShouldRetryPinpointAfterMediumBatch(
            bool parentWasMediumError,
            int parentSectorCount,
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return parentWasMediumError &&
                parentSectorCount > 1 &&
                status == Device.CommandStatus.DeviceFailed &&
                senseKey == Device.SenseKeyType.IllegalRequest &&
                asc == 0x24 &&
                ascq == 0x00;
        }

        /// <summary>
        /// A parent multi-sector 24/00 followed by an exact one-sector 24/00 disproves
        /// the original "transfer shape only" hypothesis for that address. Retry that
        /// pinpoint once after a bounded settle; unrelated child failures remain fatal.
        /// </summary>
        public static bool ShouldRetryPinpointAfterRejectedPayloadBatch(
            int parentSectorCount,
            Device.CommandStatus parentStatus,
            Device.SenseKeyType parentSenseKey,
            byte parentAsc,
            byte parentAscq,
            Device.CommandStatus pinpointStatus,
            Device.SenseKeyType pinpointSenseKey,
            byte pinpointAsc,
            byte pinpointAscq)
        {
            return ShouldDecomposeRejectedPayloadBatch(
                    parentSectorCount,
                    parentStatus,
                    parentSenseKey,
                    parentAsc,
                    parentAscq) &&
                pinpointStatus == Device.CommandStatus.DeviceFailed &&
                pinpointSenseKey == Device.SenseKeyType.IllegalRequest &&
                pinpointAsc == 0x24 &&
                pinpointAscq == 0x00;
        }

        /// <summary>
        /// Two different parent/child command shapes plus one bounded exact retry all
        /// rejected the same in-range address with 24/00. No rejected payload is used;
        /// that one sector may enter the existing untrusted vote/CTDB path.
        /// </summary>
        public static bool IsCorroboratedRejectedBatchPinpoint(
            int parentSectorCount,
            Device.CommandStatus parentStatus,
            Device.SenseKeyType parentSenseKey,
            byte parentAsc,
            byte parentAscq,
            Device.CommandStatus initialStatus,
            Device.SenseKeyType initialSenseKey,
            byte initialAsc,
            byte initialAscq,
            Device.CommandStatus repeatedStatus,
            Device.SenseKeyType repeatedSenseKey,
            byte repeatedAsc,
            byte repeatedAscq)
        {
            return ShouldRetryPinpointAfterRejectedPayloadBatch(
                    parentSectorCount,
                    parentStatus,
                    parentSenseKey,
                    parentAsc,
                    parentAscq,
                    initialStatus,
                    initialSenseKey,
                    initialAsc,
                    initialAscq) &&
                repeatedStatus == Device.CommandStatus.DeviceFailed &&
                repeatedSenseKey == Device.SenseKeyType.IllegalRequest &&
                repeatedAsc == 0x24 &&
                repeatedAscq == 0x00;
        }

        /// <summary>
        /// A repeated 24/00 for the same in-range pinpoint may enter the existing
        /// untrusted-sector pipeline only when a parent transfer independently
        /// reported medium error. No payload from either rejected command is used.
        /// </summary>
        public static bool IsCorroboratedUnreadablePinpoint(
            bool parentWasMediumError,
            int parentSectorCount,
            Device.CommandStatus initialStatus,
            Device.SenseKeyType initialSenseKey,
            byte initialAsc,
            byte initialAscq,
            Device.CommandStatus repeatedStatus,
            Device.SenseKeyType repeatedSenseKey,
            byte repeatedAsc,
            byte repeatedAscq)
        {
            return parentWasMediumError &&
                parentSectorCount > 1 &&
                initialStatus == Device.CommandStatus.DeviceFailed &&
                initialSenseKey == Device.SenseKeyType.IllegalRequest &&
                initialAsc == 0x24 &&
                initialAscq == 0x00 &&
                repeatedStatus == Device.CommandStatus.DeviceFailed &&
                repeatedSenseKey == Device.SenseKeyType.IllegalRequest &&
                repeatedAsc == 0x24 &&
                repeatedAscq == 0x00;
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

        /// <summary>
        /// The HL-DT-ST BD-RE WH16NS40 firmware 1.05 on the hardware matrix has
        /// intermittently returned HardwareError/08/0A for otherwise valid normal
        /// 16-sector BEh payload reads at unrelated addresses. ASC 08 identifies
        /// the logical-unit communication family; 0A is an unassigned qualifier.
        /// Permit one retry
        /// for only that observed command shape when no control transition is in
        /// flight. This is transport recovery, never damaged-media evidence.
        /// </summary>
        public static bool ShouldRetryObservedReadCommunicationFailure(
            int retriesForCommand,
            bool isObservedDrive,
            bool isReadCdBeh,
            int sectorCount,
            bool speedTransitionPending,
            bool cacheTransitionPending,
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return retriesForCommand == 0 &&
                isObservedDrive &&
                isReadCdBeh &&
                sectorCount == 16 &&
                !speedTransitionPending &&
                !cacheTransitionPending &&
                status == Device.CommandStatus.DeviceFailed &&
                senseKey == Device.SenseKeyType.HardwareError &&
                asc == 0x08 &&
                ascq == 0x0A;
        }

        /// <summary>
        /// The ASUS BW-16D1HT has intermittently rejected valid in-range READ CD
        /// commands with 24/00 during long reads. Cache eviction uses the same
        /// payload command. Each exact LBA/transfer-shape command may settle and
        /// retry once. A retry used by one command must not suppress the bounded
        /// retry for a different address or shape. This does not cover medium
        /// errors, readiness failures, transport errors, or a repeat.
        /// </summary>
        public static bool ShouldRetryCacheDefeatRead(
            int retriesForCommand,
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return retriesForCommand == 0 &&
                IsCacheDefeatInvalidField(status, senseKey, asc, ascq);
        }

        /// <summary>
        /// Classify the one cache-defeat failure that may select another already
        /// bounded address or transfer shape. This does not grant another retry.
        /// </summary>
        public static bool IsCacheDefeatInvalidField(
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return status == Device.CommandStatus.DeviceFailed &&
                senseKey == Device.SenseKeyType.IllegalRequest &&
                asc == 0x24 &&
                ascq == 0x00;
        }

        /// <summary>
        /// Retry the first target payload after a completed cache eviction only
        /// for the observed firmware transition rejection. The completed eviction
        /// remains valid; every other payload failure keeps its normal meaning.
        /// </summary>
        public static bool ShouldRetryAfterCacheDefeatTransition(
            bool transitionPending,
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return transitionPending &&
                IsCacheDefeatInvalidField(status, senseKey, asc, ascq);
        }

        /// <summary>
        /// A dormant-drive recovery is permitted once, and only after every
        /// bounded address and transfer shape ended in the exact invalid-field
        /// signature. The wake does not itself satisfy cache independence.
        /// </summary>
        public static bool ShouldAttemptCacheDefeatWake(
            int wakeAttempts,
            bool exhaustedInvalidFieldShapes)
        {
            return wakeAttempts == 0 && exhaustedInvalidFieldShapes;
        }

        /// <summary>
        /// The first readiness query immediately after a successful START UNIT
        /// can cross the same ASUS firmware transition as READ CD. Settle and
        /// retry that exact readiness CDB once; every other failure stays fatal.
        /// </summary>
        public static bool ShouldRetryCacheDefeatWakeReadiness(
            int retriesForTransition,
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return retriesForTransition == 0 &&
                IsCacheDefeatInvalidField(status, senseKey, asc, ascq);
        }

        /// <summary>
        /// TEST UNIT READY is advisory after a successful START UNIT; the complete
        /// cache-eviction read remains the proof of readiness and independence.
        /// After both bounded readiness attempts return the observed ASUS 24/00
        /// transition, allow that proof read once. Other readiness failures stay
        /// fatal, and failure of the proof read still fails cache defeat.
        /// </summary>
        public static bool ShouldAttemptCacheDefeatProofAfterIndeterminateReadiness(
            int retriesForTransition,
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return retriesForTransition == 1 &&
                IsCacheDefeatInvalidField(status, senseKey, asc, ascq);
        }

        /// <summary>
        /// Convert a requested eviction byte count to whole CD sectors without
        /// allowing a corrupt large persisted value to wrap to a smaller proof.
        /// </summary>
        public static int RequiredCacheDefeatSectors(int cacheDefeatBytes)
        {
            if (cacheDefeatBytes <= 0)
                return 0;
            return (int)(((long)cacheDefeatBytes + 2351L) / 2352L);
        }

        /// <summary>
        /// Decompose a rejected cache-defeat transfer without changing its address
        /// range or required byte count. Zero means the one-sector shape was already
        /// attempted and no weaker proof is available.
        /// </summary>
        public static int NextCacheDefeatChunkSize(int currentSectorCount)
        {
            if (currentSectorCount <= 1)
                return 0;
            return System.Math.Max(1, currentSectorCount / 2);
        }
    }
}
