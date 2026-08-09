using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class GzJsonTests
    {
        private string Temp() => Path.Combine(Path.GetTempPath(), "gzjson-" + System.Guid.NewGuid().ToString("N") + ".json.gz");

        [TestMethod]
        public void RoundTrips()
        {
            string p = Temp();
            var data = new List<int> { 1, 2, 3 };
            GzJson.Save(p, data);
            var back = GzJson.Load<List<int>>(p);
            CollectionAssert.AreEqual(data, back);
            DeleteStore(p);
        }

        [TestMethod]
        public void SavedFileIsGzip()
        {
            string p = Temp();
            GzJson.Save(p, new List<int> { 9 });
            var bytes = File.ReadAllBytes(p);
            Assert.IsTrue(bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b, "must be gzip");
            DeleteStore(p);
        }

        [TestMethod]
        public void LoadsExistingPlainJson()
        {
            string p = Path.Combine(Path.GetTempPath(), "plain-" + System.Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(p, "[4,5,6]");   // an old, uncompressed file
            var back = GzJson.Load<List<int>>(p);
            CollectionAssert.AreEqual(new List<int> { 4, 5, 6 }, back);
            File.Delete(p);
        }

        [TestMethod]
        public void MissingReturnsDefault()
        {
            Assert.IsNull(GzJson.Load<List<int>>(Path.Combine(Path.GetTempPath(), "nope-" + System.Guid.NewGuid().ToString("N") + ".json")));
        }

        [TestMethod]
        public void SaveFailureIsObservable()
        {
            string blocker = Path.Combine(Path.GetTempPath(), "gzjson-blocker-" + System.Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "this file prevents creation of a directory with the same name");
            try
            {
                string path = Path.Combine(blocker, "store.json.gz");
                Assert.ThrowsException<IOException>(() => GzJson.Save(path, new List<int> { 1 }));
                Assert.IsFalse(File.Exists(path));
            }
            finally
            {
                File.Delete(blocker);
            }
        }

        [TestMethod]
        public void PublishFailureCleansItsOwnedTemporaryFile()
        {
            string path = Temp();
            Directory.CreateDirectory(path);
            string parent = Path.GetDirectoryName(path)!;
            try
            {
                Assert.ThrowsException<UnauthorizedAccessException>(() =>
                    GzJson.Save(path, new List<int> { 1 }));
                Assert.AreEqual(
                    0,
                    Directory.GetFiles(parent, Path.GetFileName(path) + ".*.tmp").Length);
            }
            finally
            {
                Directory.Delete(path, recursive: true);
                string lockPath = GzJson.GetStoreLockPath(path);
                if (File.Exists(lockPath))
                    File.Delete(lockPath);
            }
        }

        [TestMethod]
        public async Task ParallelSavesUseOwnedStagesAndPublishOneCoherentFile()
        {
            string path = Temp();
            string directory = Path.GetDirectoryName(path)!;
            try
            {
                await Task.WhenAll(
                    Enumerable.Range(0, 24)
                        .Select(value => Task.Run(
                            () => GzJson.Save(
                                path,
                                Enumerable.Repeat(value, 128).ToList()))));

                List<int> loaded = GzJson.Load<List<int>>(path)!;
                Assert.AreEqual(128, loaded.Count);
                Assert.IsTrue(loaded.All(value => value == loaded[0]));
                Assert.AreEqual(
                    0,
                    Directory.GetFiles(
                        directory,
                        Path.GetFileName(path) + ".*.tmp").Length);
            }
            finally
            {
                DeleteStore(path);
            }
        }

        [TestMethod]
        public async Task SaveWaitsForAnOwnerInAnotherProcess()
        {
            string path = Temp();
            string releasePath = path + ".release";
            bool helperStarted = false;
            using var helper = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            helper.StartInfo.ArgumentList.Add("-NoProfile");
            helper.StartInfo.ArgumentList.Add("-NonInteractive");
            helper.StartInfo.ArgumentList.Add("-Command");
            helper.StartInfo.ArgumentList.Add(
                "$f=[System.IO.File]::Open($env:CUETOOLS_TEST_LOCK,"
                + "[System.IO.FileMode]::OpenOrCreate,"
                + "[System.IO.FileAccess]::ReadWrite,"
                + "[System.IO.FileShare]::None);"
                + "try{"
                + "[Console]::Out.WriteLine('locked');[Console]::Out.Flush();"
                + "while(-not [System.IO.File]::Exists($env:CUETOOLS_TEST_RELEASE)){"
                + "Start-Sleep -Milliseconds 10}}"
                + "finally{$f.Dispose()}");
            helper.StartInfo.Environment["CUETOOLS_TEST_LOCK"] =
                GzJson.GetStoreLockPath(path);
            helper.StartInfo.Environment["CUETOOLS_TEST_RELEASE"] = releasePath;

            try
            {
                helperStarted = helper.Start();
                Assert.IsTrue(helperStarted);
                string ready = await helper.StandardOutput
                    .ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5));
                if (!string.Equals("locked", ready, StringComparison.Ordinal))
                    Assert.Fail(
                        "Lock helper did not start: " +
                        await helper.StandardError.ReadToEndAsync());

                var entered = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Task save = Task.Run(() =>
                {
                    entered.SetResult();
                    GzJson.Save(path, new List<int> { 1, 2, 3 });
                });
                await entered.Task;
                await Task.Delay(150);
                Assert.IsFalse(
                    save.IsCompleted,
                    "Save returned while another process owned the store lock file.");

                File.WriteAllText(releasePath, "release");
                await save.WaitAsync(TimeSpan.FromSeconds(5));
                await helper.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(0, helper.ExitCode);
                CollectionAssert.AreEqual(
                    new List<int> { 1, 2, 3 },
                    GzJson.Load<List<int>>(path));
            }
            finally
            {
                if (helperStarted && !helper.HasExited)
                {
                    helper.Kill(entireProcessTree: true);
                    helper.WaitForExit(5000);
                }
                if (File.Exists(releasePath))
                    File.Delete(releasePath);
                DeleteStore(path);
            }
        }

        [TestMethod]
        public void LoadRejectsStoredAndDecodedSizeLimitViolations()
        {
            string path = Temp();
            try
            {
                GzJson.Save(path, new string('x', 4096));
                long storedLength = new FileInfo(path).Length;

                GzJsonLoadResult<string> storedFailure =
                    GzJson.TryLoadWithLimits<string>(
                        path,
                        storedLength - 1,
                        16 * 1024);
                Assert.AreEqual(GzJsonLoadStatus.Failed, storedFailure.Status);
                Assert.IsInstanceOfType<InvalidDataException>(
                    storedFailure.Error);

                GzJsonLoadResult<string> decodedFailure =
                    GzJson.TryLoadWithLimits<string>(
                        path,
                        storedLength,
                        64);
                Assert.AreEqual(GzJsonLoadStatus.Failed, decodedFailure.Status);
                Assert.IsInstanceOfType<InvalidDataException>(
                    decodedFailure.Error);

                GzJsonLoadResult<string> exactStoredBoundary =
                    GzJson.TryLoadWithLimits<string>(
                        path,
                        storedLength,
                        16 * 1024);
                Assert.AreEqual(GzJsonLoadStatus.Loaded, exactStoredBoundary.Status);
                Assert.AreEqual(new string('x', 4096), exactStoredBoundary.Value);

                foreach ((long stored, long decoded) in new[]
                {
                    (0L, 16L),
                    (-1L, 16L),
                    (16L, 0L),
                    (16L, -1L),
                })
                {
                    GzJsonLoadResult<string> invalidLimits =
                        GzJson.TryLoadWithLimits<string>(path, stored, decoded);
                    Assert.AreEqual(GzJsonLoadStatus.Failed, invalidLimits.Status);
                    Assert.IsInstanceOfType<ArgumentOutOfRangeException>(
                        invalidLimits.Error);
                }
            }
            finally
            {
                DeleteStore(path);
            }
        }

        private static void DeleteStore(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
            string lockPath = GzJson.GetStoreLockPath(path);
            if (File.Exists(lockPath))
                File.Delete(lockPath);
        }
    }
}
