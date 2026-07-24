using System.IO;
using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class VerifyHistoryStoreTests
    {
        private static VerifyRecord Rec(string disc, params uint[] v2)
        {
            var t = new TrackCrc[v2.Length];
            for (int i = 0; i < v2.Length; i++) t[i] = new TrackCrc { ArV1 = v2[i] ^ 0x1u, ArV2 = v2[i], Crc32 = v2[i] };
            return new VerifyRecord { DiscId = disc, Tracks = t, Drive = "TEST", Utc = System.DateTime.UtcNow };
        }
        private VerifyHistoryStore NewStore() => new VerifyHistoryStore(Path.Combine(Path.GetTempPath(), "vh-" + System.Guid.NewGuid().ToString("N") + ".json.gz"));

        [TestMethod]
        public void FirstReadIsUnknown()
        {
            var o = NewStore().CompareAndUpsert(Rec("D1", 10, 20, 30));
            Assert.IsFalse(o.KnownDisc);
            Assert.AreEqual(0, o.PriorReads);
        }

        [TestMethod]
        public void SecondIdenticalReadMatches()
        {
            var s = NewStore();
            s.CompareAndUpsert(Rec("D1", 10, 20, 30));
            var o = s.CompareAndUpsert(Rec("D1", 10, 20, 30));
            Assert.IsTrue(o.KnownDisc);
            Assert.IsTrue(o.Matches);
            Assert.AreEqual(0, o.DiffTrackCount);
            Assert.AreEqual(1, o.PriorReads);
        }

        [TestMethod]
        public void DifferingReadFlagsTracks()
        {
            var s = NewStore();
            s.CompareAndUpsert(Rec("D1", 10, 20, 30));
            var o = s.CompareAndUpsert(Rec("D1", 10, 99, 30));   // track 2 differs
            Assert.IsTrue(o.KnownDisc);
            Assert.IsFalse(o.Matches);
            Assert.AreEqual(1, o.DiffTrackCount);
        }

        [TestMethod]
        public void PersistsAcrossInstances()
        {
            string path = Path.Combine(Path.GetTempPath(), "vh-" + System.Guid.NewGuid().ToString("N") + ".json.gz");
            new VerifyHistoryStore(path).CompareAndUpsert(Rec("D1", 10, 20, 30));
            var o = new VerifyHistoryStore(path).CompareAndUpsert(Rec("D1", 10, 20, 30));
            Assert.IsTrue(o.Matches);
            File.Delete(path);
        }

        [TestMethod]
        public void BoundedToFivePerDisc()
        {
            var s = NewStore();
            for (int i = 0; i < 8; i++) s.CompareAndUpsert(Rec("D1", (uint)i, 20, 30));
            // 8 reads in; still known and PriorReads never exceeds the 5-record bound
            var o = s.CompareAndUpsert(Rec("D1", 7, 20, 30));
            Assert.IsTrue(o.PriorReads <= 5);
        }
    }
}
