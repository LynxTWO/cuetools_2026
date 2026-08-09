using System;
using CUETools.Wpf.Accuracy;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class TestAndCopyResolverTests
    {
        private static TrackCrc T(uint v2, uint v1 = 0, uint c32 = 0) =>
            new TrackCrc { ArV2 = v2, ArV1 = v1, Crc32 = c32 };

        [TestMethod]
        public void HistoryComparator_MatchesOnV2()
        {
            Assert.IsTrue(VerifyHistoryStore.SameAudioForHistory(T(10), T(10)));
            Assert.IsFalse(VerifyHistoryStore.SameAudioForHistory(T(10), T(11)));
        }

        [TestMethod]
        public void HistoryComparator_FallsBackToV1WhenV2Absent()
        {
            Assert.IsTrue(VerifyHistoryStore.SameAudioForHistory(T(0, 5), T(0, 5)));
            Assert.IsFalse(VerifyHistoryStore.SameAudioForHistory(T(0, 5), T(0, 6)));
        }

        [TestMethod]
        public void HistoryComparator_NullIsNeverEqual()
        {
            Assert.IsFalse(VerifyHistoryStore.SameAudioForHistory(null, T(10)));
            Assert.IsFalse(VerifyHistoryStore.SameAudioForHistory(T(10), null));
            Assert.IsFalse(VerifyHistoryStore.SameAudioForHistory(null, null));
        }

        [TestMethod]
        public void HistoryComparator_AllZeroArCrcIsNeverEqual()
        {
            Assert.IsFalse(VerifyHistoryStore.SameAudioForHistory(T(0), T(0)));
        }

        [TestMethod]
        public void HistoryComparator_PreservesCrossDriveArSemantics()
        {
            Assert.IsTrue(VerifyHistoryStore.SameAudioForHistory(T(10, c32: 101), T(10, c32: 202)));
        }

        [TestMethod]
        public void HistoryComparator_FallsBackWhenOnlyOneReadHasV2()
        {
            Assert.IsTrue(
                VerifyHistoryStore.SameAudioForHistory(
                    T(10, v1: 5),
                    T(0, v1: 5)));
            Assert.IsFalse(
                VerifyHistoryStore.SameAudioForHistory(
                    T(10, v1: 5),
                    T(0, v1: 6)));
        }

        [TestMethod]
        public void TestAndCopyComparatorRequiresBothRecordsAndBothChecksums()
        {
            TrackCrc complete = T(10, c32: 100);
            Assert.IsFalse(VerifyHistoryStore.SameAudioForTestAndCopy(null, null));
            Assert.IsFalse(VerifyHistoryStore.SameAudioForTestAndCopy(complete, null));
            Assert.IsFalse(VerifyHistoryStore.SameAudioForTestAndCopy(null, complete));
            Assert.IsFalse(
                VerifyHistoryStore.SameAudioForTestAndCopy(
                    T(10, c32: 0), complete));
            Assert.IsFalse(
                VerifyHistoryStore.SameAudioForTestAndCopy(
                    complete, T(10, c32: 0)));
        }

        private static VerifyRecord Read(params uint[] v2)
        {
            var t = new TrackCrc[v2.Length];
            for (int i = 0; i < v2.Length; i++) t[i] = new TrackCrc { ArV2 = v2[i], Crc32 = v2[i] };
            return new VerifyRecord { Tracks = t };
        }
        private static VerifyRecord Record(params TrackCrc[] tracks) => new VerifyRecord { Tracks = tracks };
        // staging flags: Test read is not staged (index 0); Copy/third reads are staged.
        private static bool[] Staged(int n) { var s = new bool[n]; for (int i = 1; i < n; i++) s[i] = true; return s; }

        [TestMethod]
        public void TwoReadsAgree_PassesAndSourcesTheCopyRead()
        {
            var reads = new[] { Read(10, 20, 30), Read(10, 20, 30) };
            var r = TestAndCopyResolver.Resolve(reads, Staged(2));
            Assert.AreEqual(TestCopyOutcome.Passed, r.Outcome);
            Assert.AreEqual(0, r.HeldTracks.Length);
            foreach (var v in r.Tracks) { Assert.IsTrue(v.Agreed); Assert.AreEqual(1, v.SourceReadIndex); }
        }

        [TestMethod]
        public void TwoReadsDiffer_Holds()
        {
            var reads = new[] { Read(10, 20, 30), Read(10, 99, 30) }; // track 2 differs
            var r = TestAndCopyResolver.Resolve(reads, Staged(2));
            Assert.AreEqual(TestCopyOutcome.Held, r.Outcome);
            CollectionAssert.AreEqual(new[] { 1 }, r.HeldTracks);
            Assert.IsFalse(r.Tracks[1].Agreed);
            Assert.AreEqual(-1, r.Tracks[1].SourceReadIndex);
        }

        [TestMethod]
        public void EqualArButDifferentFullCrc_HoldsAndHasNoVerifiedRead()
        {
            var reads = new[] { Record(T(10, c32: 100)), Record(T(10, c32: 200)) };
            var result = TestAndCopyResolver.Resolve(reads, Staged(2));

            Assert.AreEqual(TestCopyOutcome.Held, result.Outcome);
            CollectionAssert.AreEqual(new[] { 0 }, result.HeldTracks);
            Assert.AreEqual(-1, TestAndCopyResolver.FullyVerifiedReadIndex(reads, Staged(2)));
        }

        [TestMethod]
        public void MatchingFullCrcAndAr_Passes()
        {
            var reads = new[] { Record(T(10, c32: 100)), Record(T(10, c32: 100)) };
            var result = TestAndCopyResolver.Resolve(reads, Staged(2));

            Assert.AreEqual(TestCopyOutcome.Passed, result.Outcome);
            Assert.AreEqual(1, TestAndCopyResolver.FullyVerifiedReadIndex(reads, Staged(2)));
        }

        [TestMethod]
        public void MatchingFullCrcButDifferentAr_Holds()
        {
            var reads = new[] { Record(T(10, c32: 100)), Record(T(20, c32: 100)) };
            var result = TestAndCopyResolver.Resolve(reads, Staged(2));

            Assert.AreEqual(TestCopyOutcome.Held, result.Outcome);
            Assert.AreEqual(-1, TestAndCopyResolver.FullyVerifiedReadIndex(reads, Staged(2)));
        }

        [TestMethod]
        public void MissingFullCrc_HoldsAndHasNoVerifiedRead()
        {
            var reads = new[] { Record(T(10, c32: 0)), Record(T(10, c32: 0)) };
            var result = TestAndCopyResolver.Resolve(reads, Staged(2));

            Assert.AreEqual(TestCopyOutcome.Held, result.Outcome);
            CollectionAssert.AreEqual(new[] { 0 }, result.HeldTracks);
            Assert.AreEqual(-1, TestAndCopyResolver.FullyVerifiedReadIndex(reads, Staged(2)));
        }

        [TestMethod]
        public void ThirdReadResolvesAMismatch_SourcesTheAgreeingStagedRead()
        {
            // track 2: Test(20) != Copy(99); third read(20) agrees with Test -> source must be read 2
            var reads = new[] { Read(10, 20, 30), Read(10, 99, 30), Read(10, 20, 30) };
            var r = TestAndCopyResolver.Resolve(reads, Staged(3));
            Assert.AreEqual(TestCopyOutcome.Passed, r.Outcome);
            Assert.AreEqual(1, r.Tracks[0].SourceReadIndex);   // all three agree -> Copy (1) preferred
            Assert.AreEqual(2, r.Tracks[1].SourceReadIndex);   // only Test+third agree -> third (2)
            CollectionAssert.AreEqual(new[] { 0, 2 }, r.Tracks[1].AgreeingReads);
        }

        [TestMethod]
        public void ThirdReadStillDisagrees_HoldsThatTrack()
        {
            // track 1: all three different -> held; track 2: Copy+third agree -> committed from Copy
            var reads = new[] { Read(1, 50), Read(2, 50), Read(3, 50) };
            var r = TestAndCopyResolver.Resolve(reads, Staged(3));
            Assert.AreEqual(TestCopyOutcome.Held, r.Outcome);
            CollectionAssert.AreEqual(new[] { 0 }, r.HeldTracks);
            Assert.AreEqual(1, r.Tracks[1].SourceReadIndex);
        }

        [TestMethod]
        public void CopyDisagreesButTestAndThirdAgree_SourcesThird()
        {
            var reads = new[] { Read(7), Read(8), Read(7) };  // Copy(8) is the odd one out
            var r = TestAndCopyResolver.Resolve(reads, Staged(3));
            Assert.AreEqual(TestCopyOutcome.Passed, r.Outcome);
            Assert.AreEqual(2, r.Tracks[0].SourceReadIndex);
        }

        [TestMethod]
        public void FullyVerified_TwoReadsAgree_ReturnsCopy()
        {
            var reads = new[] { Read(10, 20), Read(10, 20) };
            Assert.AreEqual(1, TestAndCopyResolver.FullyVerifiedReadIndex(reads, Staged(2)));
        }

        [TestMethod]
        public void FullyVerified_ThirdReadCleanThroughout_ReturnsIt()
        {
            // Copy (index 1) has a blip on track 2 only; the third read (index 2) agrees with
            // Test (index 0) on both tracks, so it alone is clean throughout.
            var reads = new[] { Read(10, 20), Read(10, 99), Read(10, 20) };
            Assert.AreEqual(2, TestAndCopyResolver.FullyVerifiedReadIndex(reads, Staged(3)));
        }

        [TestMethod]
        public void FullyVerified_ScatteredErrors_ReturnsMinusOne()
        {
            // Track 1: read0=1, read1=1 agree; read2=2 is alone.
            // Track 2: read0=50, read2=50 agree; read1=60 is alone.
            // Read1 fails track 2, read2 fails track 1 - no staged read is clean on both tracks.
            var reads = new[] { Read(1, 50), Read(1, 60), Read(2, 50) };
            Assert.AreEqual(-1, TestAndCopyResolver.FullyVerifiedReadIndex(reads, Staged(3)));
        }

        [TestMethod]
        public void FullyVerified_InvalidOrEmptyInputsReturnMinusOne()
        {
            Assert.AreEqual(-1,
                TestAndCopyResolver.FullyVerifiedReadIndex(null, Array.Empty<bool>()));
            Assert.AreEqual(-1,
                TestAndCopyResolver.FullyVerifiedReadIndex(Array.Empty<VerifyRecord>(), null));
            Assert.AreEqual(-1,
                TestAndCopyResolver.FullyVerifiedReadIndex(
                    new[] { Read(1) }, Array.Empty<bool>()));
            Assert.AreEqual(-1,
                TestAndCopyResolver.FullyVerifiedReadIndex(
                    Array.Empty<VerifyRecord>(), Array.Empty<bool>()));
        }

        [TestMethod]
        public void FullyVerified_RaggedReadsCannotClaimMissingTracks()
        {
            var reads = new[] { Read(10), Read(10, 20) };

            Assert.AreEqual(
                -1,
                TestAndCopyResolver.FullyVerifiedReadIndex(reads, Staged(2)));
        }

        [TestMethod]
        public void NamedCrcEvidenceKeepsTestAndCopyWhenThirdReadIsCommitted()
        {
            var reads = new[]
            {
                Record(T(10, c32: 0x11111111)),
                Record(T(20, c32: 0x22222222)),
                Record(T(10, c32: 0x33333333)),
            };

            TrackCrc[] evidence =
                TestAndCopyResolver.BuildCrcEvidence(reads, sourceReadIndex: 2);

            Assert.AreEqual(0x33333333u, evidence[0].Crc32);
            Assert.AreEqual(0x11111111u, evidence[0].TestCrc32);
            Assert.AreEqual(0x22222222u, evidence[0].CopyCrc32);
        }

        [TestMethod]
        public void NamedCrcFallbackSurvivesRecordsWithoutCurrentReadCrc()
        {
            var reads = new[]
            {
                Record(new TrackCrc { TestCrc32 = 0x11111111 }),
                Record(new TrackCrc { CopyCrc32 = 0x22222222 }),
            };

            TrackCrc[] evidence =
                TestAndCopyResolver.BuildCrcEvidence(reads, sourceReadIndex: 1);

            Assert.AreEqual(0x11111111u, evidence[0].TestCrc32);
            Assert.AreEqual(0x22222222u, evidence[0].CopyCrc32);
        }

        [TestMethod]
        public void CompletedTestReadCanPublishBeforeCopyAndPreservesPriorCopy()
        {
            var reads = new[]
            {
                Record(new TrackCrc
                {
                    Crc32 = 0xAAAAAAAA,
                    TestCrc32 = 0xAAAAAAAA,
                    CopyCrc32 = 0x22222222,
                }),
            };

            TrackCrc[] evidence =
                TestAndCopyResolver.BuildCrcEvidence(reads, sourceReadIndex: 0);

            Assert.AreEqual(0xAAAAAAAAu, evidence[0].TestCrc32);
            Assert.AreEqual(0x22222222u, evidence[0].CopyCrc32);
        }

        [TestMethod]
        public void NamedCrcEvidence_InvalidSourceKeepsNamedReadsWithoutInventingCurrentCrc()
        {
            var reads = new[]
            {
                Record(T(10, c32: 0x11111111)),
                Record(T(20, c32: 0x22222222)),
            };

            TrackCrc[] negative = TestAndCopyResolver.BuildCrcEvidence(reads, -1);
            TrackCrc[] pastEnd = TestAndCopyResolver.BuildCrcEvidence(reads, reads.Length);

            foreach (TrackCrc evidence in new[] { negative[0], pastEnd[0] })
            {
                Assert.AreEqual(0u, evidence.Crc32);
                Assert.AreEqual(0x11111111u, evidence.TestCrc32);
                Assert.AreEqual(0x22222222u, evidence.CopyCrc32);
            }
        }

        [TestMethod]
        public void NamedCrcEvidence_UsesLongestReadAndPreservesSelectedArChecksums()
        {
            var reads = new[]
            {
                Record(
                    new TrackCrc { TestCrc32 = 0x10, CopyCrc32 = 0x11 },
                    new TrackCrc { TestCrc32 = 0x20, CopyCrc32 = 0x21 }),
                Record(new TrackCrc { CopyCrc32 = 0x30 }),
                Record(new TrackCrc { ArV1 = 7, ArV2 = 8, Crc32 = 0x40 }),
            };

            TrackCrc[] evidence = TestAndCopyResolver.BuildCrcEvidence(reads, 2);

            Assert.AreEqual(2, evidence.Length);
            Assert.AreEqual(7u, evidence[0].ArV1);
            Assert.AreEqual(8u, evidence[0].ArV2);
            Assert.AreEqual(0x40u, evidence[0].Crc32);
            Assert.AreEqual(0x10u, evidence[0].TestCrc32);
            Assert.AreEqual(0x30u, evidence[0].CopyCrc32);
            Assert.AreEqual(0u, evidence[1].Crc32);
            Assert.AreEqual(0x20u, evidence[1].TestCrc32);
            Assert.AreEqual(0x21u, evidence[1].CopyCrc32);
        }

        [TestMethod]
        public void NamedCrcEvidence_EmptyAndNullRecordsProduceNoTracks()
        {
            Assert.AreEqual(
                0,
                TestAndCopyResolver.BuildCrcEvidence(
                    Array.Empty<VerifyRecord>(), 0).Length);
            Assert.AreEqual(
                0,
                TestAndCopyResolver.BuildCrcEvidence(
                    new VerifyRecord[] { null, null }, 0).Length);
        }
    }
}
