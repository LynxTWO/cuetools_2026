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

        [TestMethod]
        public void TestAndCopyCrcsPersistAndEachRoleUpdatesIndependently()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "vh-" + System.Guid.NewGuid().ToString("N") + ".json.gz");
            try
            {
                var store = new VerifyHistoryStore(path);
                var initial = Rec("D1", 10, 20);
                initial.Tracks[0].TestCrc32 = 0x11111111;
                initial.Tracks[0].CopyCrc32 = 0x22222222;
                initial.Tracks[1].TestCrc32 = 0x33333333;
                initial.Tracks[1].CopyCrc32 = 0x44444444;
                store.CompareAndUpsert(initial);

                // A future Verify changes only Test; the last Copy values must survive.
                var verify = Rec("D1", 10, 20);
                verify.Tracks[0].TestCrc32 = 0xAAAAAAAA;
                verify.Tracks[1].TestCrc32 = 0xBBBBBBBB;
                store.CompareAndUpsert(verify);
                TrackCrc[] afterVerify = store.GetLatestCrcEvidence("D1");
                Assert.AreEqual(0xAAAAAAAAu, afterVerify[0].TestCrc32);
                Assert.AreEqual(0x22222222u, afterVerify[0].CopyCrc32);
                Assert.AreEqual(0xBBBBBBBBu, afterVerify[1].TestCrc32);
                Assert.AreEqual(0x44444444u, afterVerify[1].CopyCrc32);

                // A future Rip changes only Copy; the latest Test values must survive.
                var rip = Rec("D1", 10, 20);
                rip.Tracks[0].CopyCrc32 = 0xCCCCCCCC;
                rip.Tracks[1].CopyCrc32 = 0xDDDDDDDD;
                store.CompareAndUpsert(rip);
                TrackCrc[] afterRip =
                    new VerifyHistoryStore(path).GetLatestCrcEvidence("D1");
                Assert.AreEqual(0xAAAAAAAAu, afterRip[0].TestCrc32);
                Assert.AreEqual(0xCCCCCCCCu, afterRip[0].CopyCrc32);
                Assert.AreEqual(0xBBBBBBBBu, afterRip[1].TestCrc32);
                Assert.AreEqual(0xDDDDDDDDu, afterRip[1].CopyCrc32);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
            }
        }

        [TestMethod]
        public void UnknownDiscHasNoPersistedCrcEvidence()
        {
            Assert.AreEqual(0, NewStore().GetLatestCrcEvidence("missing").Length);
        }
    }
}
