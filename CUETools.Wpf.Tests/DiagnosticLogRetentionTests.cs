using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// F-30: one log file per launch accumulated forever. Retention keeps whichever is more,
/// the newest RetainedLogCount launches or everything from the last RetainedLogDays, and
/// an archival opt-in keeps everything.
/// </summary>
[TestClass]
public sealed class DiagnosticLogRetentionTests
{
    private static readonly MethodInfo Prune =
        typeof(DiagnosticLog).GetMethod("PruneOldLogs",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("PruneOldLogs not found");

    private static void Run(string dir) => Prune.Invoke(null, new object[] { dir });

    private static string Dir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "log-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string dir, string name, int ageDays)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-ageDays));
    }

    private static int Count(string dir) => Directory.GetFiles(dir, "cuetools-*.log").Length;

    [TestCleanup]
    public void ResetArchivalFlag() => DiagnosticLog.KeepLogsForever = false;

    [TestMethod]
    public void RecentLogsSurviveHoweverManyThereAre()
    {
        string dir = Dir();
        try
        {
            int many = DiagnosticLog.RetainedLogCount + 50;
            for (int i = 0; i < many; i++)
                Write(dir, $"cuetools-recent-{i}.log", ageDays: 1);

            Run(dir);

            Assert.AreEqual(many, Count(dir),
                "logs inside the day window must survive even past the count");
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void OldLogsSurviveWhileTheyAreWithinTheRetainedCount()
    {
        string dir = Dir();
        try
        {
            for (int i = 0; i < 10; i++)
                Write(dir, $"cuetools-ancient-{i}.log", ageDays: 900);

            Run(dir);

            Assert.AreEqual(10, Count(dir),
                "a quiet history must not be truncated by the day limit");
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void OnlyLogsFailingBothRulesAreRemoved()
    {
        string dir = Dir();
        try
        {
            for (int i = 0; i < DiagnosticLog.RetainedLogCount; i++)
                Write(dir, $"cuetools-keep-{i:D4}.log", ageDays: 1);
            for (int i = 0; i < 25; i++)
                Write(dir, $"cuetools-drop-{i:D4}.log", ageDays: DiagnosticLog.RetainedLogDays + 10);

            Run(dir);

            Assert.AreEqual(DiagnosticLog.RetainedLogCount, Count(dir));
            Assert.IsFalse(Directory.GetFiles(dir).Any(f => f.Contains("drop")),
                "logs both older than the day limit and outside the count are removed");
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void TheArchivalOptInKeepsEverything()
    {
        string dir = Dir();
        try
        {
            for (int i = 0; i < DiagnosticLog.RetainedLogCount + 30; i++)
                Write(dir, $"cuetools-old-{i:D4}.log", ageDays: 900);

            DiagnosticLog.KeepLogsForever = true;
            Run(dir);

            Assert.AreEqual(DiagnosticLog.RetainedLogCount + 30, Count(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void OtherFilesInTheDirectoryAreNeverTouched()
    {
        string dir = Dir();
        try
        {
            for (int i = 0; i < DiagnosticLog.RetainedLogCount + 5; i++)
                Write(dir, $"cuetools-old-{i:D4}.log", ageDays: 900);
            string keep = Path.Combine(dir, "notes.txt");
            File.WriteAllText(keep, "mine");
            File.SetLastWriteTimeUtc(keep, DateTime.UtcNow.AddDays(-900));

            Run(dir);

            Assert.IsTrue(File.Exists(keep), "only cuetools-*.log files are pruned");
        }
        finally { Directory.Delete(dir, true); }
    }
}
