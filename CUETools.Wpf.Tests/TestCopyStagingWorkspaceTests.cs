using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class TestCopyStagingWorkspaceTests
    {
        private string _root;
        private readonly List<string> _junctions = new();

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(),
                "cuetools-testcopy-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            foreach (string junction in _junctions)
                try { if (Directory.Exists(junction)) Directory.Delete(junction); } catch { }
            try { Directory.Delete(_root, true); } catch { }
        }

        [TestMethod]
        public void LiveLeaseProtectsWorkspaceEvenWhenTimestampIsOld()
        {
            using var workspace = TestCopyStagingWorkspace.Create(_root);
            File.WriteAllText(Path.Combine(workspace.CopyBaseDirectory, "read.flac"), "audio");
            Directory.SetLastWriteTimeUtc(workspace.WorkspaceDirectory,
                DateTime.UtcNow.AddDays(-10));

            var result = TestCopyStagingWorkspace.SweepOrphans(
                _root, TimeSpan.FromHours(24));

            Assert.AreEqual(0, result.Directories);
            Assert.IsTrue(Directory.Exists(workspace.WorkspaceDirectory));
        }

        [TestMethod]
        public void OldOwnedOrphanIsSweptAfterLeaseCloses()
        {
            var workspace = TestCopyStagingWorkspace.Create(_root);
            string path = workspace.WorkspaceDirectory;
            File.WriteAllBytes(Path.Combine(workspace.CopyBaseDirectory, "read.flac"),
                new byte[] { 1, 2, 3, 4 });
            workspace.PreserveForRecovery();
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));

            var result = TestCopyStagingWorkspace.SweepOrphans(
                _root, TimeSpan.FromHours(24));

            Assert.AreEqual(1, result.Directories);
            Assert.IsTrue(result.Bytes >= 4);
            Assert.IsFalse(Directory.Exists(path));
            workspace.Dispose();
        }

        [TestMethod]
        public void RecentOwnedOrphanIsNotSwept()
        {
            var workspace = TestCopyStagingWorkspace.Create(_root);
            string path = workspace.WorkspaceDirectory;
            workspace.PreserveForRecovery();

            var result = TestCopyStagingWorkspace.SweepOrphans(
                _root, TimeSpan.FromHours(24));

            Assert.AreEqual(0, result.Directories);
            Assert.IsTrue(Directory.Exists(path));
            Assert.IsTrue(TestCopyStagingWorkspace.TryDeleteOwnedWorkspace(path));
            workspace.Dispose();
        }

        [TestMethod]
        public void ForeignLookalikeWithoutMarkerIsUntouched()
        {
            string path = Path.Combine(_root,
                TestCopyStagingWorkspace.DirectoryPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            string sentinel = Path.Combine(path, "foreign.txt");
            File.WriteAllText(sentinel, "foreign");
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));

            var result = TestCopyStagingWorkspace.SweepOrphans(
                _root, TimeSpan.FromHours(24));

            Assert.AreEqual(0, result.Directories);
            Assert.AreEqual("foreign", File.ReadAllText(sentinel));
            Assert.IsFalse(TestCopyStagingWorkspace.TryDeleteOwnedWorkspace(path));
        }

        [TestMethod]
        public void MalformedOwnershipMarkerIsUntouched()
        {
            string path = Path.Combine(_root,
                TestCopyStagingWorkspace.DirectoryPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path,
                TestCopyStagingWorkspace.OwnershipMarkerName), "not an ownership receipt");
            string sentinel = Path.Combine(path, "foreign.txt");
            File.WriteAllText(sentinel, "foreign");
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));

            var result = TestCopyStagingWorkspace.SweepOrphans(
                _root, TimeSpan.FromHours(24));

            Assert.AreEqual(0, result.Directories);
            Assert.AreEqual("foreign", File.ReadAllText(sentinel));
        }

        [TestMethod]
        public void ReplacedWorkspaceIsUntouched()
        {
            string path;
            using (var workspace = TestCopyStagingWorkspace.Create(_root))
                path = workspace.WorkspaceDirectory;

            Directory.CreateDirectory(path);
            string sentinel = Path.Combine(path, "replacement.txt");
            File.WriteAllText(sentinel, "replacement");
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));

            var result = TestCopyStagingWorkspace.SweepOrphans(
                _root, TimeSpan.FromHours(24));

            Assert.AreEqual(0, result.Directories);
            Assert.AreEqual("replacement", File.ReadAllText(sentinel));
        }

        [TestMethod]
        public void OwnedInstanceDoesNotDeleteAfterMarkerReplacement()
        {
            var workspace = TestCopyStagingWorkspace.Create(_root);
            string path = workspace.WorkspaceDirectory;
            File.WriteAllText(Path.Combine(path,
                TestCopyStagingWorkspace.OwnershipMarkerName), "replacement marker");
            string sentinel = Path.Combine(path, "replacement.txt");
            File.WriteAllText(sentinel, "replacement");

            workspace.Dispose();

            Assert.IsTrue(Directory.Exists(path));
            Assert.AreEqual("replacement", File.ReadAllText(sentinel));
        }

        [TestMethod]
        public void ReparseStagingRootIsRejected()
        {
            string realRoot = Path.Combine(_root, "real");
            string linkRoot = Path.Combine(_root, "linked");
            Directory.CreateDirectory(realRoot);
            CreateJunction(linkRoot, realRoot);

            Assert.ThrowsException<IOException>(() =>
                TestCopyStagingWorkspace.Create(linkRoot));

            var workspace = TestCopyStagingWorkspace.Create(realRoot);
            string path = workspace.WorkspaceDirectory;
            workspace.PreserveForRecovery();
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));

            var result = TestCopyStagingWorkspace.SweepOrphans(
                linkRoot, TimeSpan.FromHours(24));

            Assert.AreEqual(0, result.Directories);
            Assert.IsTrue(Directory.Exists(path));
            Assert.IsTrue(TestCopyStagingWorkspace.TryDeleteOwnedWorkspace(path));
            workspace.Dispose();
        }

        [TestMethod]
        public void WorkspaceContainingReparseChildIsUntouched()
        {
            string external = Path.Combine(_root, "external");
            Directory.CreateDirectory(external);
            File.WriteAllText(Path.Combine(external, "sentinel.txt"), "outside");

            var workspace = TestCopyStagingWorkspace.Create(_root);
            string path = workspace.WorkspaceDirectory;
            string link = Path.Combine(workspace.CopyBaseDirectory, "linked");
            CreateJunction(link, external);
            workspace.PreserveForRecovery();
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));

            var result = TestCopyStagingWorkspace.SweepOrphans(
                _root, TimeSpan.FromHours(24));

            Assert.AreEqual(0, result.Directories);
            Assert.IsTrue(Directory.Exists(path));
            Assert.AreEqual("outside",
                File.ReadAllText(Path.Combine(external, "sentinel.txt")));

            Directory.Delete(link);
            _junctions.Remove(link);
            Assert.IsTrue(TestCopyStagingWorkspace.TryDeleteOwnedWorkspace(path));
            workspace.Dispose();
        }

        private void CreateJunction(string link, string target)
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
            _junctions.Add(link);
        }
    }
}
