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
        /// While a control or cache-defeat transition is pending, a 24/00 is the
        /// documented transition rejection, not transfer-shape evidence - it must
        /// reach the one-shot transition retry instead of being decomposed into
        /// per-sector media verdicts (R114); the repeat after that retry is fatal.
        /// </summary>
        public static bool ShouldDecomposeRejectedPayloadBatch(
            bool controlTransitionPending,
            int sectorCount,
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return !controlTransitionPending &&
                sectorCount > 1 &&
                status == Device.CommandStatus.DeviceFailed &&
                senseKey == Device.SenseKeyType.IllegalRequest &&
                asc == 0x24 &&
                ascq == 0x00;
        }

        /// <summary>
        /// In the medium-error / legacy 64/00 batch split, decide whether a failed
        /// single-sector child may be marked untrusted media evidence. A medium-error
        /// child always may. Under the legacy 64/00 parent, only a child repeating the
        /// exact 64/00 identity may - mixed-mode data tracks inside the audio range
        /// fail exactly this way. Every other child class - removal, transport,
        /// readiness, command, hardware - stays fatal under either parent (R114).
        /// </summary>
        public static bool MayMarkSplitChildUnreadable(
            bool mediumErrorParent,
            bool legacyTrackParent,
            Device.CommandStatus childStatus,
            Device.SenseKeyType childSenseKey,
            byte childAsc,
            byte childAscq)
        {
            if (IsMediumError(childStatus, childSenseKey))
                return true;
            return legacyTrackParent &&
                childStatus == Device.CommandStatus.DeviceFailed &&
                childAsc == 0x64 &&
                childAscq == 0x00;
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
            // The parent only decomposed because no transition was pending at that moment.
            return ShouldDecomposeRejectedPayloadBatch(
                    false,
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
        /// The drive's own whole-batch surrender (R118, observed live on both matrix drives):
        /// a multi-sector READ CD returns the ASSIGNED verdict HardwareError / 3E/02 (TIMEOUT
        /// ON LOGICAL UNIT) after the extended recovery timeout let the drive finish deciding.
        /// A surrendered batch does not prove every contained sector is bad, so it decomposes
        /// to independent single-sector reads exactly like a medium-error batch. Deliberately
        /// transition-agnostic: the observed batch surrender arrived with a cache transition
        /// pending, and a verdict the drive deliberated for tens of seconds is not a
        /// transition blip (the 24/00 transition rules are unchanged and separate).
        /// </summary>
        public static bool IsDriveReportedTimeoutBatch(
            int sectorCount,
            Device.CommandStatus status,
            Device.SenseKeyType senseKey,
            byte asc,
            byte ascq)
        {
            return sectorCount > 1 &&
                status == Device.CommandStatus.DeviceFailed &&
                senseKey == Device.SenseKeyType.HardwareError &&
                asc == 0x3E &&
                ascq == 0x02;
        }

        /// <summary>
        /// The drive's own per-sector surrender (R118, observed live at the outer edge of a
        /// scratched CD-R): a single-sector pinpoint inside a corroborated media batch (a
        /// MediumError parent, or a parent that itself surrendered with 3E/02) returns the
        /// ASSIGNED verdict HardwareError / 3E/02 (TIMEOUT ON LOGICAL UNIT) after the extended
        /// recovery timeout let the drive finish deciding. Unlike the unassigned 08/0A
        /// qualifier this code has standard semantics - the logical unit timed out reading that
        /// address - so the classifier is corroboration-gated, not drive-gated, and
        /// transition-agnostic for the same reason as the batch form. The drive already spent
        /// its whole extended window internally retrying, so no additional retry is issued;
        /// the sector enters the untrusted vote/CTDB path and every other hardware failure
        /// remains fatal.
        /// </summary>
        public static bool IsDriveReportedTimeoutPinpoint(
            bool corroboratedMediaParent,
            int parentSectorCount,
            int childSectorCount,
            Device.CommandStatus childStatus,
            Device.SenseKeyType childSenseKey,
            byte childAsc,
            byte childAscq)
        {
            return corroboratedMediaParent &&
                parentSectorCount > 1 &&
                childSectorCount == 1 &&
                childStatus == Device.CommandStatus.DeviceFailed &&
                childSenseKey == Device.SenseKeyType.HardwareError &&
                childAsc == 0x3E &&
                childAscq == 0x02;
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
        /// The PLDS DVD-RW DU8A5SH firmware BU51 deterministically aborts BEh+C2
        /// payload reads of exactly 15 or 2 sectors at any disc location
        /// (ABORTED COMMAND 0B/00/00), while 16/14/10/8/4/1 succeed; both USB
        /// matrix drives accept every count. Receipt:
        /// docs/review/2026-08-13-plds-partial-chunk-abort.md. For only that
        /// observed drive, a batch that would take a failing shape reads a
        /// proven-safe first sub-count instead and the caller issues the
        /// remainder (15 -> 8+7, 2 -> 1+1); both remainders are proven counts.
        /// Never changes the shape for any other drive (D10, 2026-08-14).
        /// </summary>
        public static int SafeBatchSectors(bool isObservedDrive, int requested)
        {
            if (!isObservedDrive)
                return requested;
            if (requested == 15)
                return 8;
            if (requested == 2)
                return 1;
            return requested;
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

        /// <summary>
        /// The stuck-drive signature: every bounded eviction address and
        /// transfer shape, down to a single sector, ended in the exact
        /// invalid-field rejection, and the one permitted dormant-drive wake
        /// has already been spent. Observed live on both USB matrix drives
        /// after extended damaged-media recovery (2026-08-13/14); the SATA
        /// drive has never shown it, and only a power cycle or replug has
        /// ever cleared it. This classifies the terminal state for the fatal
        /// message; it never authorizes a read and changes no retry policy.
        /// </summary>
        public static bool IsUnresponsiveDriveSignature(
            bool exhaustedInvalidFieldShapes,
            int wakeAttempts)
        {
            return exhaustedInvalidFieldShapes && wakeAttempts >= 1;
        }

        /// <summary>
        /// Plain-English guidance appended to the fatal cache-defeat failure
        /// when the stuck-drive signature is present. The wording lives beside
        /// the classifier so tests pin the two together.
        /// </summary>
        public const string UnresponsiveDriveGuidance =
            "The drive is rejecting every read shape, down to single sectors, " +
            "in regions it read successfully before. This stuck state has been " +
            "observed on USB drives after extended recovery of damaged media, " +
            "and it survives every software reset. Unplugging and reconnecting " +
            "the drive has cured it at least once (2026-08-19); other wedges " +
            "have needed a full power cycle (2026-08-14). Try the cable first, " +
            "then power the drive off and on, and retry; the disc and any " +
            "completed evidence are unaffected.";

        /// <summary>
        /// Detects the stuck-drive state during ordinary payload reads
        /// (SLICE-011 S11-006). On a wedged drive every rejected batch
        /// decomposes and every child ends as a corroborated 24/00 pinpoint,
        /// so without this the per-address untrusted rule would mass-mark a
        /// healthy disc as damage, batch after batch. A storm is called only
        /// on sustained, fully-corroborated consecutive batches with zero
        /// signs of drive life between them; any successful payload read or
        /// any genuine per-sector sense (a medium error) resets it, so the
        /// documented single-address 24/00 carve-outs can never trip it.
        /// The tracker classifies for a fatal message; it authorizes nothing.
        /// </summary>
        public sealed class PayloadRejectionStormTracker
        {
            /// <summary>Consecutive fully-corroborated decomposed batches
            /// required before the storm is called.</summary>
            public const int MinimumFullyCorroboratedBatches = 2;
            /// <summary>Total corroborated pinpoints required, so a run of
            /// tiny window-tail batches needs more than two data points.</summary>
            public const int MinimumCorroboratedPinpoints = 4;

            private int _fullyCorroboratedBatches;
            private int _corroboratedPinpoints;
            private bool _inBatch;
            private int _currentBatchSectors;
            private int _currentBatchCorroborated;

            /// <summary>A rejected multi-sector batch is starting its
            /// single-sector decomposition.</summary>
            public void BeginRejectedBatch(int sectors)
            {
                _inBatch = true;
                _currentBatchSectors = sectors;
                _currentBatchCorroborated = 0;
            }

            /// <summary>A child pinpoint ended as corroborated-untrusted
            /// (identical 24/00 after the bounded retry).</summary>
            public void RecordCorroboratedPinpoint()
            {
                if (_inBatch)
                    _currentBatchCorroborated++;
                _corroboratedPinpoints++;
            }

            /// <summary>Any evidence the drive is alive: a successful payload
            /// transfer (batch, child, or retry) or a genuine per-sector
            /// medium-error sense. Resets the storm completely.</summary>
            public void RecordDriveResponsive()
            {
                _fullyCorroboratedBatches = 0;
                _corroboratedPinpoints = 0;
                _inBatch = false;
                _currentBatchSectors = 0;
                _currentBatchCorroborated = 0;
            }

            /// <summary>The decomposition loop finished. Folds the batch into
            /// the storm state when every child was corroborated.</summary>
            public void CompleteBatch()
            {
                if (_inBatch &&
                    _currentBatchSectors > 0 &&
                    _currentBatchCorroborated == _currentBatchSectors)
                {
                    _fullyCorroboratedBatches++;
                }
                _inBatch = false;
                _currentBatchSectors = 0;
                _currentBatchCorroborated = 0;
            }

            public bool IsStorm =>
                _fullyCorroboratedBatches >= MinimumFullyCorroboratedBatches &&
                _corroboratedPinpoints >= MinimumCorroboratedPinpoints;

            /// <summary>Scrubbed counters for the fatal failure context.</summary>
            public string DescribeForFailureContext() =>
                "storm-batches=" + _fullyCorroboratedBatches +
                "; storm-pinpoints=" + _corroboratedPinpoints;
        }
    }
}
