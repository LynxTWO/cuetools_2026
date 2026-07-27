using System;
using System.IO;
using CUETools.Processor;
using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class CopyAlbumArtMigrationTests
{
    private sealed class FakeLog : IDiagnosticLog
    {
        public string LogPath => "unused.log";
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message, Exception ex = null) { }
        public void Redact(params string[] sensitive) { }
    }

    private sealed class IsolatedProfile : IDisposable
    {
        public string DirectoryPath { get; }
        public SettingsStore Store { get; }

        public IsolatedProfile()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "cuetools-copy-art-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Store = new SettingsStore(
                new FakeLog(),
                Path.Combine(DirectoryPath, "CUETools.Wpf.exe"));
        }

        public void WriteSettings(string contents)
        {
            string path = Store.SettingsFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            try { Directory.Delete(DirectoryPath, true); } catch { }
        }
    }

    [TestMethod]
    public void FirstRunWithoutProfileStillEnforcesCopyAlbumArtOff()
    {
        using var profile = new IsolatedProfile();
        var config = new CUEConfig();
        Assert.IsTrue(
            config.CopyAlbumArt,
            "The engine default must remain true for classic-app compatibility; WPF owns this override.");

        profile.Store.Load(config, new AppSettings());

        Assert.IsFalse(config.CopyAlbumArt);
    }

    [TestMethod]
    public void LegacyProfileWithoutCopyAlbumArtKeyCannotRestoreEngineDefault()
    {
        using var profile = new IsolatedProfile();
        profile.WriteSettings("WpfPreventSleep=1" + Environment.NewLine);
        var config = new CUEConfig { CopyAlbumArt = false };

        profile.Store.Load(config, new AppSettings());

        Assert.IsFalse(
            config.CopyAlbumArt,
            "CUEConfig.Load treats a missing legacy key as true; the WPF invariant must run afterward.");
    }

    [TestMethod]
    public void PersistedTrueCopyAlbumArtValueIsMigratedOff()
    {
        using var profile = new IsolatedProfile();
        profile.WriteSettings("CopyAlbumArt=1" + Environment.NewLine);
        var config = new CUEConfig { CopyAlbumArt = false };

        profile.Store.Load(config, new AppSettings());

        Assert.IsFalse(
            config.CopyAlbumArt,
            "A legacy true value must not bypass WPF's visible embed/extract choices.");
    }
}
