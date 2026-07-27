using System;
using System.IO;
using System.Threading.Tasks;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class SettingsFileLeaseTests
{
    [TestMethod]
    public async Task CompetingWriterWaitsUntilSettingsPublicationLeaseIsReleased()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "cuetools-settings-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            string settingsPath = Path.Combine(sandbox, "settings.txt");
            string lockPath = SettingsFileLease.GetLockPath(settingsPath);
            using var owner = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            Task<SettingsFileLease> waiting =
                Task.Run(() => SettingsFileLease.Acquire(settingsPath));
            await Task.Delay(100);
            Assert.IsFalse(
                waiting.IsCompleted,
                "A second window must not publish settings while the first owns the file.");

            owner.Dispose();
            using SettingsFileLease acquired =
                await waiting.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsNotNull(acquired);
        }
        finally
        {
            try
            {
                if (Directory.Exists(sandbox))
                    Directory.Delete(sandbox, recursive: true);
            }
            catch
            {
                // Cleanup must not hide the synchronization assertion.
            }
        }
    }
}
