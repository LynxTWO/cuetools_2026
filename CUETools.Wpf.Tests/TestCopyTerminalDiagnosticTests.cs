using System;
using System.Collections.Generic;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class TestCopyTerminalDiagnosticTests
{
    [TestMethod]
    public void EarlyFailurePublishesPhaseBoundTerminalDiagnostic()
    {
        var log = new RecordingLog();

        TestCopyRunResult result = RipService.BuildTestCopyFailure(
            log,
            TimeSpan.FromSeconds(12),
            "copy",
            "cache defeat failed");

        Assert.AreEqual("cache defeat failed", result.Error);
        CollectionAssert.AreEqual(
            new[] { "rip|test&copy failed after 12s phase=copy" },
            log.Warnings);
    }

    [TestMethod]
    public void EarlyStopIsNotReportedAsFailure()
    {
        var log = new RecordingLog();

        TestCopyRunResult result = RipService.BuildTestCopyFailure(
            log,
            TimeSpan.FromSeconds(9),
            "confirm",
            "Stopped.");

        Assert.AreEqual("Stopped.", result.Error);
        CollectionAssert.AreEqual(
            new[] { "rip|test&copy stopped after 9s phase=confirm" },
            log.Warnings);
    }

    [TestMethod]
    public void DiagnosticFailureCannotReplaceOriginalResult()
    {
        TestCopyRunResult result = RipService.BuildTestCopyFailure(
            new ThrowingLog(),
            TimeSpan.FromSeconds(3),
            "test",
            "read failed");

        Assert.AreEqual("read failed", result.Error);
    }

    private sealed class RecordingLog : IDiagnosticLog
    {
        internal List<string> Warnings { get; } = new();
        public string LogPath => "";

        public void Info(string category, string message)
        {
        }

        public void Warn(string category, string message) =>
            Warnings.Add(category + "|" + message);

        public void Error(
            string category,
            string message,
            Exception ex = null)
        {
        }

        public void Redact(params string[] sensitive)
        {
        }
    }

    private sealed class ThrowingLog : IDiagnosticLog
    {
        public string LogPath => "";

        public void Info(string category, string message) =>
            throw new InvalidOperationException();

        public void Warn(string category, string message) =>
            throw new InvalidOperationException();

        public void Error(
            string category,
            string message,
            Exception ex = null) =>
            throw new InvalidOperationException();

        public void Redact(params string[] sensitive)
        {
        }
    }
}
