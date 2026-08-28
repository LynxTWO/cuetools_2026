using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class OpticalDriveLeaseTests
{
    [TestMethod]
    public void SameLogicalOperationMayNestButAnotherThreadIsDenied()
    {
        using var sandbox = new LeaseSandbox();
        using OpticalDriveLease outer =
            OpticalDriveLease.TryAcquireForTest("drive-a", sandbox.Path);
        Assert.IsNotNull(outer);

        using OpticalDriveLease nested =
            OpticalDriveLease.TryAcquireForTest("drive-a", sandbox.Path);
        Assert.IsNotNull(
            nested,
            "Calibration and child reads in one job must share its lease.");

        OpticalDriveLease competing = null;
        Exception threadFailure = null;
        var competingDenial = OpticalDriveLease.LeaseDenial.None;
        var thread = new Thread(() =>
        {
            try
            {
                competing = OpticalDriveLease.TryAcquireForTest(
                    "drive-a",
                    sandbox.Path,
                    out competingDenial);
            }
            catch (Exception ex)
            {
                threadFailure = ex;
            }
        });
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsNull(threadFailure);
        Assert.IsNull(
            competing,
            "A different operation in this process must not inherit drive ownership.");
        Assert.AreEqual(
            OpticalDriveLease.LeaseDenial.SameProcess,
            competingDenial,
            "This process holds the key, so the refusal must not name another CUETools job.");
    }

    [TestMethod]
    public async Task DifferentDriveOwnersCanProceedConcurrently()
    {
        using var sandbox = new LeaseSandbox();
        using var firstReady = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        OpticalDriveLease first = null;
        Exception firstFailure = null;
        var firstThread = new Thread(() =>
        {
            try
            {
                first = OpticalDriveLease.TryAcquireForTest(
                    "drive-a",
                    sandbox.Path);
                firstReady.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                firstFailure = ex;
                firstReady.Set();
            }
            finally
            {
                first?.Dispose();
            }
        });
        firstThread.Start();
        Assert.IsTrue(firstReady.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsNull(firstFailure);
        Assert.IsNotNull(first);

        using OpticalDriveLease second =
            OpticalDriveLease.TryAcquireForTest("drive-b", sandbox.Path);
        Assert.IsNotNull(
            second,
            "A job on one physical drive must not serialize an independent drive.");

        releaseFirst.Set();
        Assert.IsTrue(await Task.Run(
            () => firstThread.Join(TimeSpan.FromSeconds(5))));
    }

    [TestMethod]
    public async Task AnotherProcessOwnerIsDeniedAndReleaseMakesDriveAvailable()
    {
        using var sandbox = new LeaseSandbox();
        string releasePath = System.IO.Path.Combine(sandbox.Path, "release");
        string lockPath = OpticalDriveLease.GetLockPathForTest(
            "drive-b",
            sandbox.Path);
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
            "$d=[System.IO.Path]::GetDirectoryName($env:CUETOOLS_TEST_DRIVE_LOCK);" +
            "[System.IO.Directory]::CreateDirectory($d)|Out-Null;" +
            "$f=[System.IO.File]::Open($env:CUETOOLS_TEST_DRIVE_LOCK," +
            "[System.IO.FileMode]::OpenOrCreate," +
            "[System.IO.FileAccess]::ReadWrite," +
            "[System.IO.FileShare]::None);" +
            "try{" +
            "[Console]::Out.WriteLine('locked');[Console]::Out.Flush();" +
            "while(-not [System.IO.File]::Exists($env:CUETOOLS_TEST_DRIVE_RELEASE)){" +
            "Start-Sleep -Milliseconds 10}}" +
            "finally{$f.Dispose()}");
        helper.StartInfo.Environment["CUETOOLS_TEST_DRIVE_LOCK"] = lockPath;
        helper.StartInfo.Environment["CUETOOLS_TEST_DRIVE_RELEASE"] =
            releasePath;

        try
        {
            helperStarted = helper.Start();
            Assert.IsTrue(helperStarted);
            string ready = await helper.StandardOutput
                .ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (!string.Equals("locked", ready, StringComparison.Ordinal))
                Assert.Fail(
                    "Drive-lock helper did not start: " +
                    await helper.StandardError.ReadToEndAsync());

            using OpticalDriveLease denied =
                OpticalDriveLease.TryAcquireForTest(
                    "drive-b",
                    sandbox.Path,
                    out OpticalDriveLease.LeaseDenial denial);
            Assert.IsNull(denied);
            Assert.AreEqual(
                OpticalDriveLease.LeaseDenial.AnotherProcess,
                denial,
                "A handle outside this process is the case the warning exists for.");

            File.WriteAllText(releasePath, "release");
            await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, helper.ExitCode);

            using OpticalDriveLease acquired =
                OpticalDriveLease.TryAcquireForTest("drive-b", sandbox.Path);
            Assert.IsNotNull(acquired);
        }
        finally
        {
            if (helperStarted && !helper.HasExited)
            {
                helper.Kill(entireProcessTree: true);
                helper.WaitForExit(5000);
            }
        }
    }

    [TestMethod]
    public void TheTrayWatcherDoesNotReportItsOwnLostRaces()
    {
        // GetTrayState is polled every two seconds for the life of a job. It competes with the
        // rip that owns the drive and loses sometimes, which is expected and self-correcting: on
        // 2026-08-27 that produced seven "already owned by another CUETools job" warnings in four
        // minutes with exactly one CUETools process running (R126). The watcher is an ancillary
        // observer, so it asks silently and reports Unknown.
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        string source = File.ReadAllText(Path.Combine(
            root, "CUETools.App.Core", "Services", "DriveService.cs"));
        int tray = source.IndexOf("public DriveTrayState GetTrayState(", StringComparison.Ordinal);
        Assert.IsTrue(tray > 0, "GetTrayState should still be the tray poll entry point");
        int nextMethod = source.IndexOf("public void OpenTray(", tray, StringComparison.Ordinal);
        Assert.IsTrue(nextMethod > tray, "expected OpenTray to follow GetTrayState");

        string body = source.Substring(tray, nextMethod - tray);
        StringAssert.Contains(
            body,
            "TryAcquire(drive, _log, reportDenial: false)",
            "the tray poll must claim the drive silently");
    }

    [TestMethod]
    public async Task CrashedProcessReleasesDriveForNextOwner()
    {
        using var sandbox = new LeaseSandbox();
        string lockPath = OpticalDriveLease.GetLockPathForTest(
            "drive-crash",
            sandbox.Path);
        using var helper = CreateLockHelper(lockPath);
        bool helperStarted = false;
        try
        {
            helperStarted = helper.Start();
            Assert.IsTrue(helperStarted);
            string ready = await helper.StandardOutput
                .ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (!string.Equals("locked", ready, StringComparison.Ordinal))
                Assert.Fail(
                    "Drive-lock helper did not start: " +
                    await helper.StandardError.ReadToEndAsync());

            using OpticalDriveLease denied =
                OpticalDriveLease.TryAcquireForTest("drive-crash", sandbox.Path);
            Assert.IsNull(denied);

            helper.Kill(entireProcessTree: true);
            await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

            using OpticalDriveLease acquired =
                OpticalDriveLease.TryAcquireForTest("drive-crash", sandbox.Path);
            Assert.IsNotNull(
                acquired,
                "Windows must release the cross-process lease if a worker crashes.");
        }
        finally
        {
            if (helperStarted && !helper.HasExited)
            {
                helper.Kill(entireProcessTree: true);
                helper.WaitForExit(5000);
            }
        }
    }

    private static Process CreateLockHelper(string lockPath)
    {
        var helper = new Process
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
            "$d=[System.IO.Path]::GetDirectoryName($env:CUETOOLS_TEST_DRIVE_LOCK);" +
            "[System.IO.Directory]::CreateDirectory($d)|Out-Null;" +
            "$f=[System.IO.File]::Open($env:CUETOOLS_TEST_DRIVE_LOCK," +
            "[System.IO.FileMode]::OpenOrCreate," +
            "[System.IO.FileAccess]::ReadWrite," +
            "[System.IO.FileShare]::None);" +
            "try{[Console]::Out.WriteLine('locked');[Console]::Out.Flush();" +
            "Start-Sleep -Seconds 30}finally{$f.Dispose()}");
        helper.StartInfo.Environment["CUETOOLS_TEST_DRIVE_LOCK"] = lockPath;
        return helper;
    }

    private sealed class LeaseSandbox : IDisposable
    {
        internal LeaseSandbox()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cuetools-drive-lease-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Test cleanup must not hide a lease assertion.
            }
        }
    }
}
