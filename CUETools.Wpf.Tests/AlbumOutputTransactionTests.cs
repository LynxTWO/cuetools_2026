using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class AlbumOutputTransactionTests
    {
        private string _root;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(),
                "cuetools-publication-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        [TestMethod]
        public void ReserveLeavesFinalPathAbsentAndStagesBesideIt()
        {
            using var tx = AlbumOutputTransaction.Reserve(_root, "Artist - Album");

            Assert.IsFalse(Directory.Exists(tx.DestinationDirectory));
            Assert.IsTrue(Directory.Exists(tx.StagingDirectory));
            Assert.AreEqual(Path.GetDirectoryName(tx.DestinationDirectory),
                Path.GetDirectoryName(tx.StagingDirectory));
            Assert.IsTrue(File.Exists(tx.ReservationPath));
        }

        [TestMethod]
        public void PublishMakesCompleteAlbumVisibleInOneMove()
        {
            string destination;
            using (var tx = AlbumOutputTransaction.Reserve(_root, "Artist - Album"))
            {
                destination = tx.DestinationDirectory;
                Directory.CreateDirectory(Path.Combine(tx.StagingDirectory, "Disc 1"));
                File.WriteAllBytes(Path.Combine(tx.StagingDirectory, "Disc 1", "01.flac"),
                    new byte[] { 1, 2, 3 });

                Assert.IsFalse(Directory.Exists(destination));
                Assert.AreEqual(destination, tx.Publish());
                Assert.IsTrue(tx.IsPublished);
            }

            Assert.IsTrue(File.Exists(Path.Combine(destination, "Disc 1", "01.flac")));
            Assert.IsTrue(File.Exists(Path.Combine(destination,
                AlbumOutputTransaction.CompletionMarkerName)));
            Assert.IsFalse(File.Exists(Path.Combine(destination,
                AlbumOutputTransaction.OwnershipMarkerName)));
            Assert.AreEqual("CUETOOLS_OUTPUT_COMPLETE_V1",
                File.ReadLines(Path.Combine(destination,
                    AlbumOutputTransaction.CompletionMarkerName)).First());
        }

        [TestMethod]
        public void ReservationReleaseFailureCannotReclassifyACommittedAlbum()
        {
            using var tx = AlbumOutputTransaction.Reserve(_root, "Artist - Album");
            File.WriteAllText(Path.Combine(tx.StagingDirectory, "01.flac"), "audio");

            FieldInfo reservationField = typeof(AlbumOutputTransaction).GetField(
                "_reservation", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(reservationField);
            var original = (FileStream)reservationField.GetValue(tx);
            Assert.IsNotNull(original);
            original.Dispose();
            reservationField.SetValue(tx, new ThrowingDisposeFileStream(tx.ReservationPath));

            string destination = tx.Publish();

            Assert.AreEqual(tx.DestinationDirectory, destination);
            Assert.IsTrue(tx.IsPublished);
            Assert.IsTrue(File.Exists(Path.Combine(destination, "01.flac")));
        }

        [TestMethod]
        public void EmptyAlbumIsNeverPublished()
        {
            string stage;
            using (var tx = AlbumOutputTransaction.Reserve(_root, "Artist - Album"))
            {
                stage = tx.StagingDirectory;
                Assert.ThrowsException<InvalidDataException>(() => tx.Publish());
                Assert.IsFalse(Directory.Exists(tx.DestinationDirectory));
            }
            Assert.IsFalse(Directory.Exists(stage));
        }

        [TestMethod]
        public void ForeignCompletionMarkerIsNeverOverwritten()
        {
            string stage;
            using (var tx = AlbumOutputTransaction.Reserve(_root, "Artist - Album"))
            {
                stage = tx.StagingDirectory;
                File.WriteAllText(Path.Combine(stage, "01.flac"), "audio");
                string marker = Path.Combine(stage,
                    AlbumOutputTransaction.CompletionMarkerName);
                File.WriteAllText(marker, "foreign");

                Assert.ThrowsException<IOException>(() => tx.Publish());
                Assert.AreEqual("foreign", File.ReadAllText(marker));
                Assert.IsFalse(Directory.Exists(tx.DestinationDirectory));
            }
            Assert.IsFalse(Directory.Exists(stage));
        }

        [TestMethod]
        public void DestinationRaceDoesNotOverwriteAnything()
        {
            string stage;
            using (var tx = AlbumOutputTransaction.Reserve(_root, "Artist - Album"))
            {
                stage = tx.StagingDirectory;
                File.WriteAllText(Path.Combine(stage, "01.flac"), "new");
                Directory.CreateDirectory(tx.DestinationDirectory);
                string sentinel = Path.Combine(tx.DestinationDirectory, "existing.txt");
                File.WriteAllText(sentinel, "old");

                Assert.ThrowsException<IOException>(() => tx.Publish());
                Assert.AreEqual("old", File.ReadAllText(sentinel));
                Assert.IsFalse(File.Exists(Path.Combine(tx.DestinationDirectory,
                    AlbumOutputTransaction.CompletionMarkerName)));
            }
            Assert.IsFalse(Directory.Exists(stage));
        }

        [TestMethod]
        public void FailedRipCanBeQuarantinedWithoutTouchingFinalPath()
        {
            string incomplete;
            string destination;
            using (var tx = AlbumOutputTransaction.Reserve(_root, "Artist - Album"))
            {
                destination = tx.DestinationDirectory;
                File.WriteAllText(Path.Combine(tx.StagingDirectory, "partial.flac"), "partial");
                incomplete = tx.PreserveIncomplete();
            }

            Assert.IsFalse(Directory.Exists(destination));
            Assert.IsTrue(Directory.Exists(incomplete));
            StringAssert.StartsWith(Path.GetFileName(incomplete), ".cuetools-incomplete-");
            Assert.AreEqual("partial", File.ReadAllText(Path.Combine(incomplete, "partial.flac")));
        }

        [TestMethod]
        public void LiveConcurrentReservationsCannotChooseTheSameAlbum()
        {
            var barrier = new Barrier(2);
            var destinations = new string[2];
            Task[] tasks = Enumerable.Range(0, 2).Select(index => Task.Run(() =>
            {
                using var tx = AlbumOutputTransaction.Reserve(_root, "Artist - Album");
                destinations[index] = tx.DestinationDirectory;
                Assert.IsTrue(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            })).ToArray();

            Assert.IsTrue(Task.WaitAll(tasks, TimeSpan.FromSeconds(15)));
            Assert.AreNotEqual(destinations[0], destinations[1]);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    Path.Combine(_root, "Artist - Album"),
                    Path.Combine(_root, "Artist - Album (2)"),
                },
                destinations);
        }

        [TestMethod]
        public void AdvisoryReservationCallbackCannotAbortANewTransaction()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Artist - Album"));

            using var tx = AlbumOutputTransaction.Reserve(
                _root, "Artist - Album",
                _ => throw new InvalidOperationException("diagnostic sink failed"));

            Assert.AreEqual(Path.Combine(_root, "Artist - Album (2)"),
                tx.DestinationDirectory);
            Assert.IsTrue(Directory.Exists(tx.StagingDirectory));
        }

        [TestMethod]
        public void StaleReservationSentinelIsNeverReclaimedByPath()
        {
            string reservationPath;
            string orphanedStage;
            string ownershipMarker;
            using (var original = AlbumOutputTransaction.Reserve(_root, "Artist - Album"))
            {
                reservationPath = original.ReservationPath;
                orphanedStage = original.StagingDirectory;
                ownershipMarker = File.ReadAllText(Path.Combine(orphanedStage,
                    AlbumOutputTransaction.OwnershipMarkerName));
            }

            File.WriteAllText(reservationPath,
                AlbumOutputTransaction.ReservationMagic + Environment.NewLine + "999999");
            Directory.CreateDirectory(orphanedStage);
            File.WriteAllText(Path.Combine(orphanedStage,
                AlbumOutputTransaction.OwnershipMarkerName), ownershipMarker);
            File.WriteAllText(Path.Combine(orphanedStage, "partial.flac"), "partial");

            using var recovered = AlbumOutputTransaction.Reserve(_root, "Artist - Album");
            Assert.AreEqual(Path.Combine(_root, "Artist - Album (2)"),
                recovered.DestinationDirectory);
            Assert.IsTrue(Directory.Exists(orphanedStage));
            Assert.AreEqual("partial",
                File.ReadAllText(Path.Combine(orphanedStage, "partial.flac")));
            StringAssert.StartsWith(
                File.ReadAllText(reservationPath),
                AlbumOutputTransaction.ReservationMagic);
            Assert.AreEqual(0, Directory.GetDirectories(_root,
                ".cuetools-incomplete-recovered-*").Length);
        }

        [TestMethod]
        public void LookalikeStageWithoutOwnershipMarkerIsNotMoved()
        {
            string reservationPath;
            string lookalikeStage;
            using (var original = AlbumOutputTransaction.Reserve(_root, "Artist - Album"))
            {
                reservationPath = original.ReservationPath;
                lookalikeStage = original.StagingDirectory;
            }

            File.WriteAllText(reservationPath,
                AlbumOutputTransaction.ReservationMagic + Environment.NewLine + "999999");
            Directory.CreateDirectory(lookalikeStage);
            string sentinel = Path.Combine(lookalikeStage, "foreign.txt");
            File.WriteAllText(sentinel, "foreign");

            using var recovered = AlbumOutputTransaction.Reserve(_root, "Artist - Album");

            Assert.AreEqual(Path.Combine(_root, "Artist - Album (2)"),
                recovered.DestinationDirectory);
            Assert.AreEqual("foreign", File.ReadAllText(sentinel));
            Assert.AreEqual(0, Directory.GetDirectories(_root,
                ".cuetools-incomplete-recovered-*").Length);
        }

        [TestMethod]
        public void ForeignReservationFileIsNeverDeleted()
        {
            string reservationPath;
            using (var original = AlbumOutputTransaction.Reserve(_root, "Artist - Album"))
                reservationPath = original.ReservationPath;
            File.WriteAllText(reservationPath, "not a CUETools reservation");

            using var next = AlbumOutputTransaction.Reserve(_root, "Artist - Album");
            Assert.AreEqual(Path.Combine(_root, "Artist - Album (2)"),
                next.DestinationDirectory);
            Assert.AreEqual("not a CUETools reservation", File.ReadAllText(reservationPath));
        }

        [TestMethod]
        public void DisposeDoesNotDeleteAReplacedStage()
        {
            string stage;
            string sentinel;
            using (var tx = AlbumOutputTransaction.Reserve(_root, "Artist - Album"))
            {
                stage = tx.StagingDirectory;
                Directory.Delete(stage, true);
                Directory.CreateDirectory(stage);
                sentinel = Path.Combine(stage, "foreign.txt");
                File.WriteAllText(sentinel, "not owned by this transaction");
            }

            Assert.IsTrue(Directory.Exists(stage));
            Assert.AreEqual("not owned by this transaction", File.ReadAllText(sentinel));
        }

        [TestMethod]
        public void MultiSegmentAlbumStaysInsideSelectedBase()
        {
            using var tx = AlbumOutputTransaction.Reserve(_root,
                Path.Combine("Box Set", "Disc 2"));
            Assert.AreEqual(Path.Combine(_root, "Box Set", "Disc 2"),
                tx.DestinationDirectory);
        }

        [TestMethod]
        public void TraversalOutsideSelectedBaseIsRejected()
        {
            Assert.ThrowsException<IOException>(() =>
                AlbumOutputTransaction.Reserve(_root, Path.Combine("..", "escape")));
        }

        [TestMethod]
        public void ExistingJunctionAncestorIsRejected()
        {
            string target = Path.Combine(_root, "junction-target");
            string junction = Path.Combine(_root, "junction");
            Directory.CreateDirectory(target);
            CreateJunction(junction, target);
            try
            {
                Assert.ThrowsException<IOException>(() =>
                    AlbumOutputTransaction.Reserve(_root,
                        Path.Combine("junction", "Artist - Album")));
                Assert.AreEqual(0, Directory.GetFileSystemEntries(target).Length);
            }
            finally
            {
                if (Directory.Exists(junction))
                    Directory.Delete(junction);
            }
        }

        [TestMethod]
        public void SelectedBaseThatIsAJunctionIsRejected()
        {
            string target = Path.Combine(_root, "base-target");
            string junction = Path.Combine(_root, "base-junction");
            Directory.CreateDirectory(target);
            CreateJunction(junction, target);
            try
            {
                Assert.ThrowsException<IOException>(() =>
                    AlbumOutputTransaction.Reserve(junction, "Artist - Album"));
                Assert.AreEqual(0, Directory.GetFileSystemEntries(target).Length);
            }
            finally
            {
                if (Directory.Exists(junction))
                    Directory.Delete(junction);
            }
        }

        [TestMethod]
        public void EncodedOutputInvariantRejectsMissingAndEmptyFiles()
        {
            string missing = Path.Combine(_root, "missing.flac");
            string empty = Path.Combine(_root, "empty.flac");
            File.WriteAllBytes(empty, Array.Empty<byte>());

            Assert.ThrowsException<InvalidDataException>(() =>
                RipService.ValidateEncodedOutputs(Array.Empty<string>(), _root));
            Assert.ThrowsException<InvalidDataException>(() =>
                RipService.ValidateEncodedOutputs(new[] { missing }, _root));
            Assert.ThrowsException<InvalidDataException>(() =>
                RipService.ValidateEncodedOutputs(new[] { empty }, _root));
        }

        [TestMethod]
        public void EncodedOutputInvariantAcceptsEveryNonemptyExpectedFile()
        {
            string first = Path.Combine(_root, "01.flac");
            string second = Path.Combine(_root, "02.flac");
            File.WriteAllBytes(first, new byte[] { 1 });
            File.WriteAllBytes(second, new byte[] { 2, 3 });

            RipService.ValidateEncodedOutputs(new[] { first, second }, _root);
            Assert.ThrowsException<InvalidDataException>(() =>
                RipService.ValidateEncodedOutputs(new[] { first, first }, _root));
        }

        [TestMethod]
        public void EncodedOutputInvariantRejectsAFileOutsideTheOwnedStage()
        {
            string outside = Path.Combine(Path.GetDirectoryName(_root)!,
                "outside-" + Guid.NewGuid().ToString("N") + ".flac");
            File.WriteAllBytes(outside, new byte[] { 1 });
            try
            {
                Assert.ThrowsException<InvalidDataException>(() =>
                    RipService.ValidateEncodedOutputs(new[] { outside }, _root));
            }
            finally
            {
                File.Delete(outside);
            }
        }

        [TestMethod]
        public void EncodedOutputInvariantRejectsAJunctionAncestor()
        {
            string target = Path.Combine(_root, "junction-target");
            string junction = Path.Combine(_root, "junction");
            Directory.CreateDirectory(target);
            string outside = Path.Combine(target, "01.flac");
            File.WriteAllBytes(outside, new byte[] { 1 });
            CreateJunction(junction, target);
            try
            {
                Assert.ThrowsException<InvalidDataException>(() =>
                    RipService.ValidateEncodedOutputs(
                        new[] { Path.Combine(junction, "01.flac") }, _root));
            }
            finally
            {
                if (Directory.Exists(junction))
                    Directory.Delete(junction);
            }
        }

        [TestMethod]
        public void VerifiedDirectoryCopyReadsBackEveryNestedFile()
        {
            string source = Path.Combine(_root, "source");
            string destination = Path.Combine(_root, "destination");
            Directory.CreateDirectory(Path.Combine(source, "Disc 2"));
            byte[] first = Enumerable.Range(0, 4097).Select(i => (byte)(i % 251)).ToArray();
            byte[] second = Enumerable.Range(0, 997).Select(i => (byte)(255 - i % 239)).ToArray();
            File.WriteAllBytes(Path.Combine(source, "album.cue"), first);
            File.WriteAllBytes(Path.Combine(source, "Disc 2", "02.flac"), second);

            RipService.CopyDirectoryRecursiveVerified(source, destination);

            CollectionAssert.AreEqual(first,
                File.ReadAllBytes(Path.Combine(destination, "album.cue")));
            CollectionAssert.AreEqual(second,
                File.ReadAllBytes(Path.Combine(destination, "Disc 2", "02.flac")));
        }

        private static void CreateJunction(string link, string target)
        {
            var start = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("mklink");
            start.ArgumentList.Add("/J");
            start.ArgumentList.Add(link);
            start.ArgumentList.Add(target);
            using Process process = Process.Start(start)
                ?? throw new AssertFailedException("Could not start junction helper.");
            process.WaitForExit();
            if (process.ExitCode != 0)
                Assert.Fail("Could not create test junction: " +
                    process.StandardError.ReadToEnd());
        }

        private sealed class ThrowingDisposeFileStream : FileStream
        {
            private bool _throwOnDispose = true;

            public ThrowingDisposeFileStream(string path)
                : base(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
                    4096, FileOptions.DeleteOnClose)
            {
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing && _throwOnDispose)
                {
                    _throwOnDispose = false;
                    throw new IOException("Injected reservation release failure.");
                }
            }
        }
    }
}
