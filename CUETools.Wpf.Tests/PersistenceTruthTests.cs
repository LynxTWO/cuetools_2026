using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CUETools.Wpf.Accuracy;
using CUETools.Wpf.Models;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class PersistenceTruthTests
    {
        [TestMethod]
        public void VerifyHistoryDoesNotReportSuccessWhenSaveFails()
        {
            WithBlockedDirectory(path =>
            {
                var store = new VerifyHistoryStore(path);
                var record = new VerifyRecord
                {
                    DiscId = "disc",
                    Tracks = new[] { new TrackCrc { ArV1 = 1, ArV2 = 2, Crc32 = 3 } }
                };

                Assert.ThrowsException<IOException>(() => store.CompareAndUpsert(record));
            });
        }

        [TestMethod]
        public void DriveCalibrationDoesNotReportSuccessWhenSaveFails()
        {
            WithBlockedDirectory(path =>
            {
                var store = new DriveCalibrationStore(path);
                var calibration = new DriveCalibration { DriveSignature = "TEST" };

                Assert.ThrowsException<IOException>(() => store.Save(calibration));
            });
        }

        [TestMethod]
        public void RecentHistoryDoesNotPublishFailedWriteToMemory()
        {
            WithBlockedDirectory(path =>
            {
                var log = new RecordingLog();
                var store = new HistoryStore(log, path);

                store.Add(new RipReport
                {
                    Timestamp = DateTime.Now,
                    Album = "Must not appear",
                    Mode = "Rip",
                    TrackCount = 1
                });

                Assert.AreEqual(0, store.Recent(10).Count);
                Assert.AreEqual(1, log.WarningCount);
            });
        }

        [TestMethod]
        public void CorruptVerifyHistoryIsNotOverwritten()
        {
            string path = TempStorePath("verify-history");
            byte[] corrupt = { 0x1f, 0x8b, 0x01, 0x02, 0x03 };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, corrupt);
                var store = new VerifyHistoryStore(path);

                Assert.ThrowsException<InvalidDataException>(() =>
                    store.CompareAndUpsert(new VerifyRecord
                    {
                        DiscId = "disc",
                        Tracks = new[]
                        {
                            new TrackCrc { ArV1 = 1, ArV2 = 2, Crc32 = 3 }
                        }
                    }));
                CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(path));
            }
            finally
            {
                DeleteStoreDirectory(path);
            }
        }

        [TestMethod]
        public void CorruptDriveCalibrationIsNotOverwritten()
        {
            string path = TempStorePath("drive-calibration");
            byte[] corrupt = System.Text.Encoding.UTF8.GetBytes("{not valid json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, corrupt);
                var store = new DriveCalibrationStore(path);

                Assert.ThrowsException<InvalidDataException>(() =>
                    store.Save(new DriveCalibration { DriveSignature = "TEST" }));
                CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(path));
            }
            finally
            {
                DeleteStoreDirectory(path);
            }
        }

        [TestMethod]
        public void CorruptRecentHistoryIsBackedUpAndNeverOverwritten()
        {
            string path = TempStorePath("recent-history");
            byte[] corrupt = System.Text.Encoding.UTF8.GetBytes("[truncated");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, corrupt);
                var log = new RecordingLog();
                var store = new HistoryStore(log, path);

                store.Add(new RipReport
                {
                    Timestamp = DateTime.Now,
                    Album = "Must not replace corrupt evidence",
                    Mode = "Rip",
                    TrackCount = 1
                });

                CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(path));
                CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(path + ".bak"));
                Assert.AreEqual(0, store.Recent(10).Count);
                Assert.AreEqual(2, log.WarningCount);
            }
            finally
            {
                DeleteStoreDirectory(path);
            }
        }

        [DataTestMethod]
        [DataRow("[null]")]
        [DataRow("[{\"Mode\":null}]")]
        public void SemanticallyInvalidRecentHistoryIsPreserved(string json)
        {
            string path = TempStorePath("recent-history-semantic");
            byte[] corrupt = System.Text.Encoding.UTF8.GetBytes(json);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, corrupt);
                var log = new RecordingLog();
                var store = new HistoryStore(log, path);

                store.Add(NewReport("Must not replace semantic evidence"));

                CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(path));
                CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(path + ".bak"));
                Assert.AreEqual(0, store.Recent(10).Count);
                Assert.AreEqual(2, log.WarningCount);
            }
            finally
            {
                DeleteStoreDirectory(path);
            }
        }

        [TestMethod]
        public async Task ConcurrentCalibrationWritersPreserveEveryDrive()
        {
            string path = TempStorePath("drive-calibration-concurrent");
            try
            {
                const int DriveCount = 16;
                await Task.WhenAll(
                    Enumerable.Range(0, DriveCount)
                        .Select(index => Task.Run(
                            () => new DriveCalibrationStore(path).Save(
                                new DriveCalibration
                                {
                                    DriveSignature = "DRIVE-" + index,
                                    MaxSpeedKbps = index
                                }))));

                var reader = new DriveCalibrationStore(path);
                for (int index = 0; index < DriveCount; index++)
                {
                    DriveCalibration calibration =
                        reader.Get("DRIVE-" + index);
                    Assert.IsNotNull(calibration);
                    Assert.AreEqual(index, calibration.MaxSpeedKbps);
                }
            }
            finally
            {
                DeleteStoreDirectory(path);
            }
        }

        [TestMethod]
        public async Task ConcurrentVerifyHistoryWritersPreserveTheFiveRecordCap()
        {
            string path = TempStorePath("verify-history-concurrent");
            try
            {
                await Task.WhenAll(
                    Enumerable.Range(1, 24)
                        .Select(index => Task.Run(
                            () => new VerifyHistoryStore(path).CompareAndUpsert(
                                new VerifyRecord
                                {
                                    DiscId = "disc",
                                    Tracks = new[]
                                    {
                                        new TrackCrc
                                        {
                                            ArV1 = (uint)index,
                                            ArV2 = (uint)index,
                                            Crc32 = (uint)index
                                        }
                                    }
                                }))));

                VerifyOutcome outcome = new VerifyHistoryStore(path)
                    .CompareAndUpsert(
                        new VerifyRecord
                        {
                            DiscId = "disc",
                            Tracks = new[]
                            {
                                new TrackCrc
                                {
                                    ArV1 = 100,
                                    ArV2 = 100,
                                    Crc32 = 100
                                }
                            }
                        });
                Assert.AreEqual(5, outcome.PriorReads);
            }
            finally
            {
                DeleteStoreDirectory(path);
            }
        }

        [TestMethod]
        public async Task ConcurrentRecentHistoryWritersMergeInsteadOfClobbering()
        {
            string path = TempStorePath("recent-history-concurrent");
            try
            {
                var first = new HistoryStore(new RecordingLog(), path);
                var second = new HistoryStore(new RecordingLog(), path);
                await Task.WhenAll(
                    Task.Run(() => first.Add(NewReport("Album one"))),
                    Task.Run(() => second.Add(NewReport("Album two"))));

                var reopened = new HistoryStore(new RecordingLog(), path);
                string[] titles = reopened.Recent(10)
                    .Select(row => row.Title)
                    .OrderBy(title => title, StringComparer.Ordinal)
                    .ToArray();
                CollectionAssert.AreEqual(
                    new[] { "Album one", "Album two" },
                    titles);
            }
            finally
            {
                DeleteStoreDirectory(path);
            }
        }

        [TestMethod]
        public async Task ConcurrentAddsOnOneHistoryStorePublishTheNewestDiskSnapshot()
        {
            string path = TempStorePath("recent-history-same-instance");
            try
            {
                var store = new HistoryStore(new RecordingLog(), path);
                await Task.WhenAll(
                    Enumerable.Range(0, 32)
                        .Select(index => Task.Run(
                            () => store.Add(NewReport("Album " + index)))));

                Assert.AreEqual(32, store.Recent(50).Count);
                var reopened = new HistoryStore(new RecordingLog(), path);
                Assert.AreEqual(32, reopened.Recent(50).Count);
            }
            finally
            {
                DeleteStoreDirectory(path);
            }
        }

        private static RipReport NewReport(string album) => new RipReport
        {
            Timestamp = DateTime.Now,
            Album = album,
            Mode = "Rip",
            TrackCount = 1
        };

        private static string TempStorePath(string label)
            => Path.Combine(Path.GetTempPath(),
                label + "-" + Guid.NewGuid().ToString("N"), "store.json.gz");

        private static void DeleteStoreDirectory(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (directory != null && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }

        private static void WithBlockedDirectory(Action<string> assertion)
        {
            string blocker = Path.Combine(Path.GetTempPath(), "persistence-blocker-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "block directory creation");
            try
            {
                assertion(Path.Combine(blocker, "store.json.gz"));
            }
            finally
            {
                File.Delete(blocker);
            }
        }

        private sealed class RecordingLog : IDiagnosticLog
        {
            public int WarningCount { get; private set; }
            public string LogPath => "";
            public void Info(string category, string message) { }
            public void Warn(string category, string message) => WarningCount++;
            public void Error(string category, string message, Exception ex = null) { }
            public void Redact(params string[] sensitive) { }
        }
    }
}
