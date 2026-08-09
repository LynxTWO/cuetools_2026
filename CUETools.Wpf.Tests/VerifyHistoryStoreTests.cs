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
            Assert.AreEqual(5, o.PriorReads);
        }

        [TestMethod]
        public void PreviewAgreementHandlesMissingKnownAndCorruptStores()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "vh-" + System.Guid.NewGuid().ToString("N") + ".json.gz");
            var store = new VerifyHistoryStore(path);
            try
            {
                Assert.ThrowsException<System.ArgumentNullException>(() =>
                    store.PreviewAgreement(null));

                VerifyRecord missing = Rec("missing", 10);
                missing.ReadKind = "Test";
                missing.Tracks[0].TestCrc32 = 100;
                store.PreviewAgreement(missing);
                Assert.AreEqual(1, missing.Tracks[0].TestMatchCount);

                VerifyRecord prior = Rec("known", 10);
                prior.Drive = "Drive A";
                prior.ReadKind = "Test";
                prior.Tracks[0].TestCrc32 = 100;
                store.CompareAndUpsert(prior);

                VerifyRecord known = Rec(" known ", 10);
                known.Drive = "Drive B";
                known.ReadKind = "Test";
                known.Tracks[0].TestCrc32 = 100;
                store.PreviewAgreement(known);
                Assert.AreEqual(2, known.Tracks[0].TestMatchCount);
                Assert.AreEqual(2, known.Tracks[0].TestDriveCount);

                File.WriteAllText(path, "not json");
                Assert.ThrowsException<InvalidDataException>(() =>
                    store.PreviewAgreement(Rec("known", 10)));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
            }
        }

        [TestMethod]
        public void LegacyNamedCrcsInferReadRolesAndPreserveBlankDriveEvidence()
        {
            var store = NewStore();
            VerifyRecord legacy = Rec("D1", 10);
            legacy.Drive = "";
            legacy.ReadKind = "";
            legacy.Tracks[0].TestCrc32 = 100;
            legacy.Tracks[0].CopyCrc32 = 100;

            store.CompareAndUpsert(legacy);
            TrackCrc evidence = store.GetLatestCrcEvidence("D1")[0];

            Assert.AreEqual(1, evidence.TestMatchCount);
            Assert.AreEqual(1, evidence.CopyMatchCount);
            Assert.AreEqual(0, evidence.TestDriveCount);
            Assert.AreEqual(0, evidence.CopyDriveCount);
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

        [TestMethod]
        public void AgreementCountsJobsByRoleAndDistinctDrive()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "vh-" + System.Guid.NewGuid().ToString("N") + ".json.gz");
            try
            {
                var store = new VerifyHistoryStore(path);
                var test = Rec("D1", 10);
                test.Drive = "Drive A";
                test.ReadKind = "Test";
                test.Tracks[0].TestCrc32 = 0x12345678;
                store.CompareAndUpsert(test);
                AssertEvidence(store, testCount: 1, copyCount: 0, testDrives: 1, copyDrives: 0);

                var copy = Rec("D1", 10);
                copy.Drive = "Drive A";
                copy.ReadKind = "Copy";
                copy.Tracks[0].CopyCrc32 = 0x12345678;
                store.CompareAndUpsert(copy);
                AssertEvidence(store, testCount: 1, copyCount: 1, testDrives: 1, copyDrives: 1);

                var sameDrivePair = Rec("D1", 10);
                sameDrivePair.Drive = "Drive A";
                sameDrivePair.ReadKind = "TestAndCopy";
                sameDrivePair.Tracks[0].TestCrc32 = 0x12345678;
                sameDrivePair.Tracks[0].CopyCrc32 = 0x12345678;
                store.CompareAndUpsert(sameDrivePair);
                AssertEvidence(store, testCount: 2, copyCount: 2, testDrives: 1, copyDrives: 1);

                var otherDrivePair = Rec("D1", 10);
                otherDrivePair.Drive = "Drive B";
                otherDrivePair.ReadKind = "TestAndCopy";
                otherDrivePair.Tracks[0].TestCrc32 = 0x12345678;
                otherDrivePair.Tracks[0].CopyCrc32 = 0x12345678;
                store.CompareAndUpsert(otherDrivePair);
                AssertEvidence(store, testCount: 3, copyCount: 3, testDrives: 2, copyDrives: 2);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
            }
        }

        [TestMethod]
        public void ChangedRoleCrcResetsOnlyThatRolesAgreement()
        {
            var store = NewStore();
            var pair = Rec("D1", 10);
            pair.Drive = "Drive A";
            pair.ReadKind = "TestAndCopy";
            pair.Tracks[0].TestCrc32 = 10;
            pair.Tracks[0].CopyCrc32 = 10;
            store.CompareAndUpsert(pair);

            var changedCopy = Rec("D1", 10);
            changedCopy.Drive = "Drive B";
            changedCopy.ReadKind = "Copy";
            changedCopy.Tracks[0].CopyCrc32 = 11;
            store.CompareAndUpsert(changedCopy);

            TrackCrc evidence = store.GetLatestCrcEvidence("D1")[0];
            Assert.AreEqual(1, evidence.TestMatchCount);
            Assert.AreEqual(1, evidence.TestDriveCount);
            Assert.AreEqual(1, evidence.CopyMatchCount);
            Assert.AreEqual(1, evidence.CopyDriveCount);
            Assert.AreEqual(10u, evidence.TestCrc32);
            Assert.AreEqual(11u, evidence.CopyCrc32);
        }

        private static void AssertEvidence(
            VerifyHistoryStore store,
            int testCount,
            int copyCount,
            int testDrives,
            int copyDrives)
        {
            TrackCrc evidence = store.GetLatestCrcEvidence("D1")[0];
            Assert.AreEqual(testCount, evidence.TestMatchCount);
            Assert.AreEqual(copyCount, evidence.CopyMatchCount);
            Assert.AreEqual(testDrives, evidence.TestDriveCount);
            Assert.AreEqual(copyDrives, evidence.CopyDriveCount);
        }
    }
}
