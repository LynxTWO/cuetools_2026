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
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
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
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.IllegalRequest,
                0x21,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
                16,
                Device.CommandStatus.DeviceFailed,
                Device.SenseKeyType.MediumError,
                0x24,
                0x00));
            Assert.IsFalse(PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
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
    }
}
