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
    }
}
