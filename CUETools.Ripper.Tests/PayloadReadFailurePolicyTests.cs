using Bwg.Scsi;
using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    [TestClass]
    public class PayloadReadFailurePolicyTests
    {
        [TestMethod]
        public void MediumErrorCanBecomeUnreadableSectorEvidence()
        {
            Assert.IsTrue(PayloadReadFailurePolicy.IsMediumError(
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.MediumError));
            Assert.IsTrue(PayloadReadFailurePolicy.ShouldSplitBatch(
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.MediumError,
                0x02,
                0x00));
        }

        [TestMethod]
        public void HardwareAndNotReadyFailuresRemainFatal()
        {
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldSplitBatch(
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x02,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldSplitBatch(
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.NotReady,
                0x3A,
                0x00));
        }

        [TestMethod]
        public void TransportFailureRemainsFatal()
        {
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldSplitBatch(
                Device.CommandStatus.IoctlFailed,
                Device.SenseKeyType.MediumError,
                0x02,
                0x00));
        }

        [TestMethod]
        public void LegacyTrackModeStillSplitsWithoutBecomingMediumError()
        {
            Assert.IsTrue(PayloadReadFailurePolicy.ShouldSplitBatch(
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x64,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.IsMediumError(
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest));
        }

        [TestMethod]
        public void InvalidFieldBatchCanDecomposeOnlyWhenItContainsMultipleSectors()
        {
            Assert.IsTrue(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
                false,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
                false,
                1,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
        }

        [TestMethod]
        public void BatchDecompositionDoesNotCoverOtherPayloadFailures()
        {
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
                false,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x21,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
                false,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.MediumError,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
                false,
                16,
                Device.CommandStatus.IoctlFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
        }

        [TestMethod]
        public void RejectedBatchPinpointGetsOneBoundedRetry()
        {
            Assert.IsTrue(
                PayloadReadFailurePolicy
                    .ShouldRetryPinpointAfterRejectedPayloadBatch(
                        16,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00));
            Assert.IsFalse(
                PayloadReadFailurePolicy
                    .ShouldRetryPinpointAfterRejectedPayloadBatch(
                        1,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00));
            Assert.IsFalse(
                PayloadReadFailurePolicy
                    .ShouldRetryPinpointAfterRejectedPayloadBatch(
                        16,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.HardwareError,
                        0x44,
                        0x00));
        }

        [TestMethod]
        public void RepeatedRejectedBatchPinpointBecomesUntrustedOnlyForSameFailure()
        {
            Assert.IsTrue(
                PayloadReadFailurePolicy
                    .IsCorroboratedRejectedBatchPinpoint(
                        16,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00));
            Assert.IsFalse(
                PayloadReadFailurePolicy
                    .IsCorroboratedRejectedBatchPinpoint(
                        16,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.NotReady,
                        0x04,
                        0x01));
        }

        [TestMethod]
        public void PinpointInvalidFieldRetriesOnlyAfterParentMediumError()
        {
            Assert.IsTrue(PayloadReadFailurePolicy.ShouldRetryPinpointAfterMediumBatch(
                true,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldRetryPinpointAfterMediumBatch(
                false,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldRetryPinpointAfterMediumBatch(
                true,
                1,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
        }

        [TestMethod]
        public void RepeatedCorroboratedPinpointCanBecomeUnreadableEvidence()
        {
            Assert.IsTrue(PayloadReadFailurePolicy.IsCorroboratedUnreadablePinpoint(
                true,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.IsCorroboratedUnreadablePinpoint(
                false,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.IsCorroboratedUnreadablePinpoint(
                true,
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x44,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.IsCorroboratedUnreadablePinpoint(
                true,
                1,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
        }

        [TestMethod]
        public void InvalidFieldCanRetryOnceOnlyAfterAControlTransition()
        {
            Assert.IsTrue(PayloadReadFailurePolicy.ShouldRetryAfterControlTransition(
                true,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldRetryAfterControlTransition(
                false,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
        }

        [TestMethod]
        public void ControlTransitionDoesNotRetryOtherCommandOrDeviceFailures()
        {
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldRetryAfterControlTransition(
                true,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x21,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldRetryAfterControlTransition(
                true,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldRetryAfterControlTransition(
                true,
                Device.CommandStatus.IoctlFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
        }

        [TestMethod]
        public void ObservedReadCommunicationFailureGetsOneExactRetry()
        {
            Assert.IsTrue(
                PayloadReadFailurePolicy
                    .ShouldRetryObservedReadCommunicationFailure(
                        0,
                        isObservedDrive: true,
                        isReadCdBeh: true,
                        sectorCount: 16,
                        speedTransitionPending: false,
                        cacheTransitionPending: false,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.HardwareError,
                        0x08,
                        0x0A));
            Assert.IsFalse(
                PayloadReadFailurePolicy
                    .ShouldRetryObservedReadCommunicationFailure(
                        1,
                        isObservedDrive: true,
                        isReadCdBeh: true,
                        sectorCount: 16,
                        speedTransitionPending: false,
                        cacheTransitionPending: false,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.HardwareError,
                        0x08,
                        0x0A));
        }

        [TestMethod]
        public void ReadCommunicationRetryRejectsEveryUnobservedAncestry()
        {
            Assert.IsFalse(RetryObservedCommunication(
                isObservedDrive: false,
                isReadCdBeh: true,
                sectorCount: 16,
                speedTransitionPending: false,
                cacheTransitionPending: false,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x08,
                0x0A));
            Assert.IsFalse(RetryObservedCommunication(
                isObservedDrive: true,
                isReadCdBeh: false,
                sectorCount: 16,
                speedTransitionPending: false,
                cacheTransitionPending: false,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x08,
                0x0A));
            Assert.IsFalse(RetryObservedCommunication(
                isObservedDrive: true,
                isReadCdBeh: true,
                sectorCount: 1,
                speedTransitionPending: false,
                cacheTransitionPending: false,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x08,
                0x0A));
            Assert.IsFalse(RetryObservedCommunication(
                isObservedDrive: true,
                isReadCdBeh: true,
                sectorCount: 16,
                speedTransitionPending: true,
                cacheTransitionPending: false,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x08,
                0x0A));
            Assert.IsFalse(RetryObservedCommunication(
                isObservedDrive: true,
                isReadCdBeh: true,
                sectorCount: 16,
                speedTransitionPending: false,
                cacheTransitionPending: true,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x08,
                0x0A));
            Assert.IsFalse(RetryObservedCommunication(
                isObservedDrive: true,
                isReadCdBeh: true,
                sectorCount: 16,
                speedTransitionPending: false,
                cacheTransitionPending: false,
                Device.CommandStatus.IoctlFailed,
                Device.SenseKeyType.HardwareError,
                0x08,
                0x0A));
            Assert.IsFalse(RetryObservedCommunication(
                isObservedDrive: true,
                isReadCdBeh: true,
                sectorCount: 16,
                speedTransitionPending: false,
                cacheTransitionPending: false,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.MediumError,
                0x08,
                0x0A));
            Assert.IsFalse(RetryObservedCommunication(
                isObservedDrive: true,
                isReadCdBeh: true,
                sectorCount: 16,
                speedTransitionPending: false,
                cacheTransitionPending: false,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x08,
                0x00));
            Assert.IsFalse(RetryObservedCommunication(
                isObservedDrive: true,
                isReadCdBeh: true,
                sectorCount: 16,
                speedTransitionPending: false,
                cacheTransitionPending: false,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x44,
                0x0A));
        }

        [TestMethod]
        public void UnassignedCommunicationQualifierKeepsFamilyAndRawIdentity()
        {
            string description = Device.LookupSenseError(0x08, 0x0A);

            StringAssert.Contains(
                description,
                "LOGICAL UNIT COMMUNICATION FAILURE");
            StringAssert.Contains(description, "UNASSIGNED QUALIFIER 0A");
            StringAssert.Contains(description, "ASC=08, ASCQ=0A");
            Assert.IsFalse(description.Contains("NO SENSE STRING"));
            Assert.AreEqual(
                "LOGICAL UNIT COMMUNICATION TIME-OUT",
                Device.LookupSenseError(0x08, 0x01));
        }

        private static bool RetryObservedCommunication(
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
            return PayloadReadFailurePolicy
                .ShouldRetryObservedReadCommunicationFailure(
                    0,
                    isObservedDrive,
                    isReadCdBeh,
                    sectorCount,
                    speedTransitionPending,
                    cacheTransitionPending,
                    status,
                    senseKey,
                    asc,
                    ascq);
        }

        [TestMethod]
        public void CacheDefeatRetriesOnlyTheObservedTransientInvalidField()
        {
            Assert.IsTrue(PayloadReadFailurePolicy.ShouldRetryCacheDefeatRead(
                0,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldRetryCacheDefeatRead(
                0,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.MediumError,
                0x11,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldRetryCacheDefeatRead(
                0,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.NotReady,
                0x04,
                0x01));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldRetryCacheDefeatRead(
                0,
                Device.CommandStatus.IoctlFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsTrue(PayloadReadFailurePolicy.IsCacheDefeatInvalidField(
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.IsCacheDefeatInvalidField(
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.HardwareError,
                0x24,
                0x00));
        }

        [TestMethod]
        public void CacheDefeatRetryIsScopedToOneExactCommand()
        {
            Assert.IsTrue(PayloadReadFailurePolicy.ShouldRetryCacheDefeatRead(
                0,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldRetryCacheDefeatRead(
                1,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));

            // A different LBA or transfer shape creates a new command-local counter.
            Assert.IsTrue(PayloadReadFailurePolicy.ShouldRetryCacheDefeatRead(
                0,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
        }

        [TestMethod]
        public void CacheDefeatTransitionRetriesOnlyExactInvalidFieldOnce()
        {
            Assert.IsTrue(
                PayloadReadFailurePolicy.ShouldRetryAfterCacheDefeatTransition(
                    true,
                    Device.CommandStatus.DeviceFailed,
                    Device.SenseKeyType.IllegalRequest,
                    0x24,
                    0x00));
            Assert.IsFalse(
                PayloadReadFailurePolicy.ShouldRetryAfterCacheDefeatTransition(
                    false,
                    Device.CommandStatus.DeviceFailed,
                    Device.SenseKeyType.IllegalRequest,
                    0x24,
                    0x00));
            Assert.IsFalse(
                PayloadReadFailurePolicy.ShouldRetryAfterCacheDefeatTransition(
                    true,
                    Device.CommandStatus.DeviceFailed,
                    Device.SenseKeyType.MediumError,
                    0x11,
                    0x00));
            Assert.IsFalse(
                PayloadReadFailurePolicy.ShouldRetryAfterCacheDefeatTransition(
                    true,
                    Device.CommandStatus.IoctlFailed,
                    Device.SenseKeyType.IllegalRequest,
                    0x24,
                    0x00));
        }

        [TestMethod]
        public void CacheDefeatSectorCountCannotWrapAtMaximumSetting()
        {
            Assert.AreEqual(
                1,
                PayloadReadFailurePolicy.RequiredCacheDefeatSectors(1));
            Assert.AreEqual(
                int.MaxValue / 2352 + 1,
                PayloadReadFailurePolicy.RequiredCacheDefeatSectors(
                    int.MaxValue));
            Assert.AreEqual(
                0,
                PayloadReadFailurePolicy.RequiredCacheDefeatSectors(0));
        }

        [TestMethod]
        public void CacheDefeatWakeRequiresFullExactExhaustionAndOccursOnce()
        {
            Assert.IsTrue(
                PayloadReadFailurePolicy.ShouldAttemptCacheDefeatWake(
                    0,
                    exhaustedInvalidFieldShapes: true));
            Assert.IsFalse(
                PayloadReadFailurePolicy.ShouldAttemptCacheDefeatWake(
                    0,
                    exhaustedInvalidFieldShapes: false));
            Assert.IsFalse(
                PayloadReadFailurePolicy.ShouldAttemptCacheDefeatWake(
                    1,
                    exhaustedInvalidFieldShapes: true));
        }

        [TestMethod]
        public void CacheDefeatWakeReadinessRetriesOnlyFirstExactTransition()
        {
            Assert.IsTrue(
                PayloadReadFailurePolicy
                    .ShouldRetryCacheDefeatWakeReadiness(
                        0,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00));
            Assert.IsFalse(
                PayloadReadFailurePolicy
                    .ShouldRetryCacheDefeatWakeReadiness(
                        1,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00));
            Assert.IsFalse(
                PayloadReadFailurePolicy
                    .ShouldRetryCacheDefeatWakeReadiness(
                        0,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.NotReady,
                        0x04,
                        0x01));
            Assert.IsFalse(
                PayloadReadFailurePolicy
                    .ShouldRetryCacheDefeatWakeReadiness(
                        0,
                        Device.CommandStatus.IoctlFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00));
        }

        [TestMethod]
        public void CacheDefeatProofMayFollowOnlyTwoExactIndeterminateReadinessResults()
        {
            Assert.IsTrue(
                PayloadReadFailurePolicy
                    .ShouldAttemptCacheDefeatProofAfterIndeterminateReadiness(
                        1,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00));
            Assert.IsFalse(
                PayloadReadFailurePolicy
                    .ShouldAttemptCacheDefeatProofAfterIndeterminateReadiness(
                        0,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00));
            Assert.IsFalse(
                PayloadReadFailurePolicy
                    .ShouldAttemptCacheDefeatProofAfterIndeterminateReadiness(
                        1,
                        Device.CommandStatus.DeviceFailed,
                        Device.SenseKeyType.NotReady,
                        0x04,
                        0x01));
            Assert.IsFalse(
                PayloadReadFailurePolicy
                    .ShouldAttemptCacheDefeatProofAfterIndeterminateReadiness(
                        1,
                        Device.CommandStatus.IoctlFailed,
                        Device.SenseKeyType.IllegalRequest,
                        0x24,
                        0x00));
        }

        [TestMethod]
        public void SafeBatchSectorsSplitsOnlyTheObservedFailingShapes()
        {
            // The PLDS DU8A5SH BU51 aborts exactly 15- and 2-sector reads; the
            // safe first sub-counts leave proven remainders (8+7, 1+1).
            Assert.AreEqual(8, PayloadReadFailurePolicy.SafeBatchSectors(true, 15));
            Assert.AreEqual(1, PayloadReadFailurePolicy.SafeBatchSectors(true, 2));
            foreach (int count in new[] { 16, 14, 10, 8, 7, 4, 3, 1 })
                Assert.AreEqual(count, PayloadReadFailurePolicy.SafeBatchSectors(true, count));
            // Any other drive keeps every shape, including the failing ones.
            Assert.AreEqual(15, PayloadReadFailurePolicy.SafeBatchSectors(false, 15));
            Assert.AreEqual(2, PayloadReadFailurePolicy.SafeBatchSectors(false, 2));
        }

        [TestMethod]
        public void CacheDefeatChunkFallbackTerminatesAtOneSector()
        {
            int chunk = 16;
            int[] expected = { 8, 4, 2, 1, 0 };
            foreach (int next in expected)
            {
                chunk = PayloadReadFailurePolicy.NextCacheDefeatChunkSize(chunk);
                Assert.AreEqual(next, chunk);
            }
        }

        [TestMethod]
        public void UnresponsiveSignatureNeedsExhaustedShapesAndSpentWake()
        {
            // The observed USB stuck state: shapes exhausted after the wake.
            Assert.IsTrue(
                PayloadReadFailurePolicy.IsUnresponsiveDriveSignature(true, 1));
            // Exhausted shapes before the wake ran are not terminal yet.
            Assert.IsFalse(
                PayloadReadFailurePolicy.IsUnresponsiveDriveSignature(true, 0));
            // Any non-invalid-field failure mix never carries the signature,
            // wake or no wake.
            Assert.IsFalse(
                PayloadReadFailurePolicy.IsUnresponsiveDriveSignature(false, 0));
            Assert.IsFalse(
                PayloadReadFailurePolicy.IsUnresponsiveDriveSignature(false, 1));
        }

        [TestMethod]
        public void RejectionStormNeedsTwoFullBatchesAndFourPinpoints()
        {
            var t = new PayloadReadFailurePolicy.PayloadRejectionStormTracker();
            Assert.IsFalse(t.IsStorm);
            t.BeginRejectedBatch(16);
            for (int i = 0; i < 16; i++) t.RecordCorroboratedPinpoint();
            t.CompleteBatch();
            // One fully corroborated batch: plenty of pinpoints, batches short.
            Assert.IsFalse(t.IsStorm);
            t.BeginRejectedBatch(16);
            for (int i = 0; i < 16; i++) t.RecordCorroboratedPinpoint();
            t.CompleteBatch();
            Assert.IsTrue(t.IsStorm);
        }

        [TestMethod]
        public void TinyTailBatchesNeedFourPinpointsTotal()
        {
            var t = new PayloadReadFailurePolicy.PayloadRejectionStormTracker();
            for (int b = 0; b < 2; b++)
            {
                t.BeginRejectedBatch(1);
                t.RecordCorroboratedPinpoint();
                t.CompleteBatch();
            }
            // Two batches but only two data points: not yet a storm.
            Assert.IsFalse(t.IsStorm);
            for (int b = 0; b < 2; b++)
            {
                t.BeginRejectedBatch(1);
                t.RecordCorroboratedPinpoint();
                t.CompleteBatch();
            }
            Assert.IsTrue(t.IsStorm);
        }

        [TestMethod]
        public void AnySignOfDriveLifeResetsTheStorm()
        {
            var t = new PayloadReadFailurePolicy.PayloadRejectionStormTracker();
            t.BeginRejectedBatch(16);
            for (int i = 0; i < 16; i++) t.RecordCorroboratedPinpoint();
            t.CompleteBatch();
            // A successful child read mid-batch resets everything; the batch
            // that contained it can never fold in as fully corroborated.
            t.BeginRejectedBatch(16);
            for (int i = 0; i < 8; i++) t.RecordCorroboratedPinpoint();
            t.RecordDriveResponsive();
            t.CompleteBatch();
            Assert.IsFalse(t.IsStorm);
            // A genuine-sense (medium) interlude between batches resets too.
            t.BeginRejectedBatch(16);
            for (int i = 0; i < 16; i++) t.RecordCorroboratedPinpoint();
            t.CompleteBatch();
            t.RecordDriveResponsive();
            t.BeginRejectedBatch(16);
            for (int i = 0; i < 16; i++) t.RecordCorroboratedPinpoint();
            t.CompleteBatch();
            Assert.IsFalse(t.IsStorm);
        }

        [TestMethod]
        public void PartiallyCorroboratedBatchesNeverFold()
        {
            var t = new PayloadReadFailurePolicy.PayloadRejectionStormTracker();
            for (int b = 0; b < 3; b++)
            {
                t.BeginRejectedBatch(16);
                for (int i = 0; i < 15; i++) t.RecordCorroboratedPinpoint();
                t.CompleteBatch();
            }
            // 45 pinpoints, but no batch was fully corroborated.
            Assert.IsFalse(t.IsStorm);
        }

        [TestMethod]
        public void UnresponsiveGuidanceNamesTheOnlyKnownCure()
        {
            // The message must tell the user the one action that has ever
            // cleared the state, and must scope the claim to what was
            // observed (USB, damaged-media recovery).
            StringAssert.Contains(
                PayloadReadFailurePolicy.UnresponsiveDriveGuidance, "eplug");
            StringAssert.Contains(
                PayloadReadFailurePolicy.UnresponsiveDriveGuidance, "USB");
            StringAssert.Contains(
                PayloadReadFailurePolicy.UnresponsiveDriveGuidance,
                "single sectors");
        }
    }
}
