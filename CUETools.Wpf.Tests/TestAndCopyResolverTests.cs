using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class TestAndCopyResolverTests
    {
        private static TrackCrc T(uint v2, uint v1 = 0, uint c32 = 0) =>
            new TrackCrc { ArV2 = v2, ArV1 = v1, Crc32 = c32 };

        [TestMethod]
        public void SameAudio_MatchesOnV2()
        {
            Assert.IsTrue(VerifyHistoryStore.SameAudio(T(10), T(10)));
            Assert.IsFalse(VerifyHistoryStore.SameAudio(T(10), T(11)));
        }

        [TestMethod]
        public void SameAudio_FallsBackToV1WhenV2Absent()
        {
            Assert.IsTrue(VerifyHistoryStore.SameAudio(T(0, 5), T(0, 5)));
            Assert.IsFalse(VerifyHistoryStore.SameAudio(T(0, 5), T(0, 6)));
        }

        [TestMethod]
        public void SameAudio_NullIsNeverEqual()
        {
            Assert.IsFalse(VerifyHistoryStore.SameAudio(null, T(10)));
            Assert.IsFalse(VerifyHistoryStore.SameAudio(T(10), null));
            Assert.IsFalse(VerifyHistoryStore.SameAudio(null, null));
        }

        private static VerifyRecord Read(params uint[] v2)
        {
            var t = new TrackCrc[v2.Length];
            for (int i = 0; i < v2.Length; i++) t[i] = new TrackCrc { ArV2 = v2[i], Crc32 = v2[i] };
            return new VerifyRecord { Tracks = t };
        }
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
    }
}
