using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CUETools.Processor;
using CUETools.Processor.Settings;
using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class SettingsSecurityTests
{
    private const string Secret = "stream-secret-7eC!9";

    private sealed class FakeProtector : ISecretProtector
    {
        private const string Prefix = "test-v1:";

        public string Protect(string secret)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(secret);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] ^= 0x5a;
            return Prefix + Convert.ToBase64String(bytes);
        }

        public string Unprotect(string protectedValue)
        {
            if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
                throw new CryptographicException("test protector rejected the value");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(protectedValue.Substring(Prefix.Length));
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("test protector rejected corrupt data", ex);
            }

            for (int i = 0; i < bytes.Length; i++)
                bytes[i] ^= 0x5a;
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private sealed class FakeLog : IDiagnosticLog
    {
        public readonly List<string> Messages = new();
        public readonly List<string> Redactions = new();
        public string LogPath => "unused.log";
        public void Info(string category, string message) => Messages.Add(category + ": " + message);
        public void Warn(string category, string message) => Messages.Add(category + ": " + message);
        public void Error(string category, string message, Exception ex = null) =>
            Messages.Add(category + ": " + message + " (" + ex?.GetType().Name + ")");
        public void Redact(params string[] sensitive) =>
            Redactions.AddRange(sensitive.Where(value => !string.IsNullOrEmpty(value)));
    }

    private sealed class EncoderSettingsFixture
    {
        public int Effort { get; set; } = 4;
        public DayOfWeek Mode { get; set; } = DayOfWeek.Monday;
        public string[] UnsupportedArray { get; set; } = Array.Empty<string>();
    }

    private sealed class IsolatedProfile : IDisposable
    {
        public string DirectoryPath { get; }
        public string AppPath { get; }

        public IsolatedProfile()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "cuetools-security-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            AppPath = Path.Combine(DirectoryPath, "CUETools.Wpf.exe");
        }

        public SettingsStore CreateStore(FakeLog log = null) =>
            new SettingsStore(log ?? new FakeLog(), AppPath, new FakeProtector());

        public void Dispose()
        {
            try { Directory.Delete(DirectoryPath, true); } catch { }
        }
    }

    [TestMethod]
    public void ProtectedCredential_RoundTripsWithoutPlaintextInSettings()
    {
        using var profile = new IsolatedProfile();
        var log = new FakeLog();
        SettingsStore store = profile.CreateStore(log);
        var config = new CUEConfig();
        config.advanced.ProxyPassword = Secret;

        store.Save(config, new AppSettings());

        byte[] saved = File.ReadAllBytes(store.SettingsFilePath);
        Assert.IsFalse(ContainsSequence(saved, Encoding.UTF8.GetBytes(Secret)));
        Assert.IsFalse(File.ReadAllText(store.SettingsFilePath).Contains("\"ProxyPassword\"", StringComparison.Ordinal));
        Assert.IsTrue(File.ReadAllText(store.SettingsFilePath).Contains("WpfProxyPasswordProtected=", StringComparison.Ordinal));
        Assert.AreEqual(Secret, config.advanced.ProxyPassword, "saving must not clear the live credential");

        var loaded = new CUEConfig();
        store.Load(loaded, new AppSettings());

        Assert.AreEqual(Secret, loaded.advanced.ProxyPassword);
        CollectionAssert.Contains(log.Redactions, Secret);
        Assert.IsFalse(string.Join("\n", log.Messages).Contains(Secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public void LegacyPlaintext_IsMigratedAndRemovedOnNextSave()
    {
        using var profile = new IsolatedProfile();
        var log = new FakeLog();
        SettingsStore store = profile.CreateStore(log);
        var legacy = new CUEConfig();
        legacy.advanced.ProxyPassword = Secret;

        using (var writer = new SettingsWriter("CUETools2026", "settings.txt", profile.AppPath))
        {
            legacy.Save(writer);
            writer.Close();
        }
        string legacyText = File.ReadAllText(store.SettingsFilePath);
        legacyText = Regex.Replace(
            legacyText,
            "^ProxyPasswordProtected=.*(?:\\r?\\n|$)",
            "",
            RegexOptions.Multiline);
        legacyText = legacyText.Replace(
            "Advanced={",
            "Advanced={\r\n=  \"ProxyPassword\": \"" + Secret + "\",",
            StringComparison.Ordinal);
        File.WriteAllText(store.SettingsFilePath, legacyText);
        Assert.IsTrue(File.ReadAllText(store.SettingsFilePath).Contains(Secret, StringComparison.Ordinal));

        var migrated = new CUEConfig();
        store.Load(migrated, new AppSettings());
        Assert.AreEqual(Secret, migrated.advanced.ProxyPassword);
        Assert.IsTrue(log.Messages.Any(message => message.Contains("will be protected on next save", StringComparison.Ordinal)));

        store.Save(migrated, new AppSettings());

        string saved = File.ReadAllText(store.SettingsFilePath);
        Assert.IsFalse(saved.Contains(Secret, StringComparison.Ordinal));
        Assert.IsFalse(saved.Contains("\"ProxyPassword\"", StringComparison.Ordinal));
        Assert.IsTrue(saved.Contains("WpfProxyPasswordProtected=", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ClassicConfigCredential_RoundTripsProtectedWithoutPlaintext()
    {
        using var profile = new IsolatedProfile();
        const string appName = "CUEToolsClassicSecurityTest";
        const string fileName = "settings.txt";
        var config = new CUEConfig();
        config.advanced.ProxyPassword = Secret;

        using (var writer = new SettingsWriter(appName, fileName, profile.AppPath))
        {
            config.Save(writer);
            writer.Close();
        }

        var reader = new SettingsReader(appName, fileName, profile.AppPath);
        string path = Path.Combine(reader.ProfilePath, fileName);
        string saved = File.ReadAllText(path);
        Assert.IsFalse(saved.Contains(Secret, StringComparison.Ordinal));
        Assert.IsFalse(saved.Contains("\"ProxyPassword\"", StringComparison.Ordinal));
        Assert.IsTrue(saved.Contains("ProxyPasswordProtected=dpapi-v1:", StringComparison.Ordinal));
        Assert.AreEqual(Secret, config.advanced.ProxyPassword);

        var loaded = new CUEConfig();
        loaded.Load(reader);
        Assert.AreEqual(Secret, loaded.advanced.ProxyPassword);
        Assert.IsFalse(loaded.ProxyCredentialRejected);
    }

    [TestMethod]
    public void ClassicConfigUnsupportedCredentialProtection_PreservesExistingSettings()
    {
        using var profile = new IsolatedProfile();
        const string appName = "CUEToolsClassicUnsupportedProtectionTest";
        const string fileName = "settings.txt";
        string path;

        using (var initial = new SettingsWriter(appName, fileName, profile.AppPath))
        {
            initial.Save("Sentinel", "original");
            initial.Close();
        }
        path = Path.Combine(
            new SettingsReader(appName, fileName, profile.AppPath).ProfilePath,
            fileName);
        byte[] original = File.ReadAllBytes(path);

        var config = new CUEConfig();
        config.advanced.ProxyPassword = Secret;
        Assert.ThrowsException<PlatformNotSupportedException>(() =>
        {
            using var attempted = new SettingsWriter(appName, fileName, profile.AppPath);
            config.Save(attempted, canProtectCurrentUser: false);
            attempted.Close();
        });

        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.AreEqual(Secret, config.advanced.ProxyPassword);
        Assert.AreEqual(0, Directory.GetFiles(
            Path.GetDirectoryName(path)!, "*.tmp", SearchOption.TopDirectoryOnly).Length);
    }

    [TestMethod]
    public void ClassicConfigCorruptProtectedCredential_FailsClosed()
    {
        using var profile = new IsolatedProfile();
        const string appName = "CUEToolsClassicSecurityTest";
        const string fileName = "settings.txt";
        var config = new CUEConfig();
        config.advanced.ProxyPassword = Secret;

        using (var writer = new SettingsWriter(appName, fileName, profile.AppPath))
        {
            config.Save(writer);
            writer.Close();
        }

        var initialReader = new SettingsReader(appName, fileName, profile.AppPath);
        string path = Path.Combine(initialReader.ProfilePath, fileName);
        string saved = Regex.Replace(
            File.ReadAllText(path),
            "^ProxyPasswordProtected=.*$",
            "ProxyPasswordProtected=dpapi-v1:not-base64",
            RegexOptions.Multiline);
        File.WriteAllText(path, saved);

        var loaded = new CUEConfig();
        loaded.advanced.ProxyPassword = "must-be-cleared";
        loaded.Load(new SettingsReader(appName, fileName, profile.AppPath));

        Assert.AreEqual("", loaded.advanced.ProxyPassword);
        Assert.IsTrue(loaded.ProxyCredentialRejected);
    }

    [TestMethod]
    public void ClearingCredential_RemovesProtectedValue()
    {
        using var profile = new IsolatedProfile();
        SettingsStore store = profile.CreateStore();
        var config = new CUEConfig();
        config.advanced.ProxyPassword = Secret;
        store.Save(config, new AppSettings());

        config.advanced.ProxyPassword = "";
        store.Save(config, new AppSettings());

        Assert.IsFalse(File.ReadAllText(store.SettingsFilePath)
            .Contains("WpfProxyPasswordProtected=", StringComparison.Ordinal));
        var loaded = new CUEConfig();
        store.Load(loaded, new AppSettings());
        Assert.AreEqual("", loaded.advanced.ProxyPassword);
    }

    [TestMethod]
    public void CorruptProtectedCredential_FailsClosedWithoutLeakingValues()
    {
        using var profile = new IsolatedProfile();
        var log = new FakeLog();
        SettingsStore store = profile.CreateStore(log);
        var config = new CUEConfig();
        config.advanced.ProxyPassword = Secret;
        store.Save(config, new AppSettings());

        string text = File.ReadAllText(store.SettingsFilePath);
        text = Regex.Replace(text, "^WpfProxyPasswordProtected=.*$",
            "WpfProxyPasswordProtected=test-v1:not-base64", RegexOptions.Multiline);
        File.WriteAllText(store.SettingsFilePath, text);

        var loaded = new CUEConfig();
        loaded.advanced.ProxyPassword = "must-be-cleared";
        store.Load(loaded, new AppSettings());

        Assert.AreEqual("", loaded.advanced.ProxyPassword);
        string messages = string.Join("\n", log.Messages);
        Assert.IsTrue(messages.Contains("protected proxy credential unavailable", StringComparison.Ordinal));
        Assert.IsFalse(messages.Contains(Secret, StringComparison.Ordinal));
        Assert.IsFalse(messages.Contains("not-base64", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AdvancedSettings_IgnoreUnknownPropertyButRejectWrongTypeAsAWhole()
    {
        using var profile = new IsolatedProfile();
        SettingsStore store = profile.CreateStore();
        var source = new CUEConfig();
        source.advanced.ProxyPort = 8123;
        store.Save(source, new AppSettings());
        string valid = File.ReadAllText(store.SettingsFilePath);

        string unknown = valid.Replace("Advanced={", "Advanced={\r\n=  \"UnexpectedSetting\": true,");
        File.WriteAllText(store.SettingsFilePath, unknown);
        var unknownTarget = new CUEConfig();
        unknownTarget.advanced.ProxyPort = 4567;
        store.Load(unknownTarget, new AppSettings());
        Assert.IsFalse(unknownTarget.AdvancedSettingsRejected);
        Assert.AreEqual(8123, unknownTarget.advanced.ProxyPort,
            "a newer unknown key must not discard known Advanced settings");

        string wrongType = valid.Replace("\"ProxyPort\": 8123", "\"ProxyPort\": { \"value\": 8123 }");
        Assert.AreNotEqual(valid, wrongType, "test setup did not locate ProxyPort in the serialized JSON");
        File.WriteAllText(store.SettingsFilePath, wrongType);
        var wrongTypeTarget = new CUEConfig();
        wrongTypeTarget.advanced.ProxyPort = 7654;
        store.Load(wrongTypeTarget, new AppSettings());
        Assert.IsTrue(wrongTypeTarget.AdvancedSettingsRejected);
        Assert.AreEqual(7654, wrongTypeTarget.advanced.ProxyPort,
            "a wrong-typed value must reject the entire Advanced object");
    }

    [TestMethod]
    public void AdvancedSettings_AllowKnownCodecTypesAndRejectUnknownTypeMetadata()
    {
        using var profile = new IsolatedProfile();
        SettingsStore store = profile.CreateStore();
        var source = new CUEConfig();
        store.Save(source, new AppSettings());
        string valid = File.ReadAllText(store.SettingsFilePath);
        Assert.IsTrue(valid.Contains("\"$type\"", StringComparison.Ordinal),
            "test requires at least one discovered codec settings type");

        var roundTripped = new CUEConfig();
        store.Load(roundTripped, new AppSettings());
        CollectionAssert.AreEquivalent(
            source.advanced.encoders.Select(item => item.GetType().FullName).ToArray(),
            roundTripped.advanced.encoders.Select(item => item.GetType().FullName).ToArray());

        string unknownType = Regex.Replace(valid,
            "\"\\$type\":\\s*\"[^\"]+\"",
            "\"$type\": \"System.IO.FileInfo, System.Private.CoreLib\"",
            RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.AreNotEqual(valid, unknownType, "test setup did not replace type metadata");
        File.WriteAllText(store.SettingsFilePath, unknownType);

        var target = new CUEConfig();
        int knownCount = target.advanced.encoders.Count;
        store.Load(target, new AppSettings());
        Assert.IsTrue(target.AdvancedSettingsRejected);
        Assert.AreEqual(knownCount, target.advanced.encoders.Count);
        Assert.IsFalse(target.advanced.encoders.Any(item => item.GetType().FullName == "System.IO.FileInfo"));
    }

    [TestMethod]
    public void DisposedUncommittedWriter_LeavesPreviousSettingsIntact()
    {
        using var profile = new IsolatedProfile();
        using (var initial = new SettingsWriter("CUETools2026", "settings.txt", profile.AppPath))
        {
            initial.Save("marker", "original");
            initial.Close();
        }

        string path = profile.CreateStore().SettingsFilePath;
        using (var interrupted = new SettingsWriter("CUETools2026", "settings.txt", profile.AppPath))
            interrupted.Save("marker", "partial");

        Assert.IsTrue(File.ReadAllText(path).Contains("marker=original", StringComparison.Ordinal));
        Assert.IsFalse(File.ReadAllText(path).Contains("partial", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Length);
    }

    [TestMethod]
    public void EncoderSettingRows_UseAnExplicitTypeAllowlistAndRejectBadValues()
    {
        var fixture = new EncoderSettingsFixture();
        var effortProperty = System.ComponentModel.TypeDescriptor.GetProperties(fixture)[nameof(EncoderSettingsFixture.Effort)];
        var modeProperty = System.ComponentModel.TypeDescriptor.GetProperties(fixture)[nameof(EncoderSettingsFixture.Mode)];
        var arrayProperty = System.ComponentModel.TypeDescriptor.GetProperties(fixture)[nameof(EncoderSettingsFixture.UnsupportedArray)];

        Assert.IsTrue(EncoderSettingRow.Supports(effortProperty));
        Assert.IsTrue(EncoderSettingRow.Supports(modeProperty));
        Assert.IsFalse(EncoderSettingRow.Supports(arrayProperty));

        var effort = new EncoderSettingRow(fixture, effortProperty, "");
        effort.TextValue = "not-an-integer";
        Assert.AreEqual(4, fixture.Effort);
        Assert.IsTrue(effort.HasValidationError);
        effort.TextValue = "9";
        Assert.AreEqual(9, fixture.Effort);
        Assert.IsFalse(effort.HasValidationError);

        var mode = new EncoderSettingRow(fixture, modeProperty, "");
        mode.TextValue = "NotADay";
        Assert.AreEqual(DayOfWeek.Monday, fixture.Mode);
        Assert.IsTrue(mode.HasValidationError);
    }

    [TestMethod]
    public void AdvancedViewModel_ExposesOnlySetClearAndHasCredentialSemantics()
    {
        var config = new CUEConfig();
        var log = new FakeLog();
        var viewModel = new AdvancedViewModel(config, log);

        Assert.IsNull(typeof(AdvancedViewModel).GetProperty("ProxyPassword"),
            "the UI must not have a readable or bindable plaintext credential property");
        Assert.IsFalse(viewModel.HasProxyPassword);
        Assert.IsTrue(viewModel.SetProxyPassword(Secret));
        Assert.IsTrue(viewModel.HasProxyPassword);
        Assert.AreEqual(Secret, config.advanced.ProxyPassword);
        CollectionAssert.Contains(log.Redactions, Secret);

        viewModel.ClearProxyPassword();
        Assert.IsFalse(viewModel.HasProxyPassword);
        Assert.AreEqual("", config.advanced.ProxyPassword);
    }

    [TestMethod]
    public void WindowsDpapiProtector_RoundTripsForCurrentUser()
    {
        var protector = new WindowsDpapiSecretProtector();

        string protectedValue = protector.Protect(Secret);

        Assert.IsFalse(protectedValue.Contains(Secret, StringComparison.Ordinal));
        Assert.AreEqual(Secret, protector.Unprotect(protectedValue));
        Assert.ThrowsException<CryptographicException>(() => protector.Unprotect("dpapi-v1:not-base64"));
        Assert.ThrowsException<CryptographicException>(() =>
            protector.Protect(new string('x', 16385)));
        Assert.ThrowsException<CryptographicException>(() =>
            protector.Unprotect("dpapi-v1:" + new string('A', 131072)));
    }

    [TestMethod]
    public void LegacyIcecastUiAndSavePath_DoNotPersistOrRedisplayPlaintext()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(repoRoot, "could not locate the repository root");

        string designer = File.ReadAllText(Path.Combine(repoRoot, "CUEPlayer", "IcecastSettings.Designer.cs"));
        string player = File.ReadAllText(Path.Combine(repoRoot, "CUEPlayer", "Icecast.cs"));
        string store = File.ReadAllText(Path.Combine(repoRoot, "CUEPlayer", "IcecastCredentialStore.cs"));

        Assert.IsFalse(designer.Contains(
            "DataBindings.Add(new System.Windows.Forms.Binding(\"Text\", this.icecastSettingsDataBindingSource, \"Password\"",
            StringComparison.Ordinal));
        Assert.IsFalse(player.Contains("Properties.Settings.Default.Save()", StringComparison.Ordinal));
        Assert.IsFalse(player.Contains("Trace.WriteLine(ex.Message)", StringComparison.Ordinal));
        Assert.IsTrue(store.Contains("DataProtectionScope.CurrentUser", StringComparison.Ordinal));
        Assert.IsTrue(store.Contains(
            "Legacy Icecast credential migrated to current-user protection.",
            StringComparison.Ordinal));
        Assert.IsTrue(player.Contains("icecastWriter.Delete();", StringComparison.Ordinal));

        int clearIndex = store.IndexOf("item.Password = \"\";", StringComparison.Ordinal);
        int saveIndex = store.IndexOf("Properties.Settings.Default.Save();", StringComparison.Ordinal);
        Assert.IsTrue(clearIndex >= 0 && saveIndex > clearIndex,
            "every Icecast settings object must be cleared before ApplicationSettingsBase serializes it");
    }

    [TestMethod]
    public void LegacyIcecastCleanupFailuresAreContainedAndTypeOnlyLogged()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(repoRoot, "could not locate the repository root");

        string player = File.ReadAllText(
            Path.Combine(repoRoot, "CUEPlayer", "Icecast.cs"));
        int workerStart = player.IndexOf(
            "private void FlushThread()",
            StringComparison.Ordinal);
        int workerEnd = player.IndexOf(
            "void Mixer_AudioRead",
            workerStart,
            StringComparison.Ordinal);
        Assert.IsTrue(workerStart >= 0 && workerEnd > workerStart);
        string worker = player.Substring(
            workerStart,
            workerEnd - workerStart);
        int clearWriter = worker.IndexOf(
            "_icecastWriter = null;",
            StringComparison.Ordinal);
        int deleteWriter = worker.IndexOf(
            "writer.Delete();",
            StringComparison.Ordinal);
        Assert.IsTrue(
            clearWriter >= 0 && deleteWriter > clearWriter,
            "the stopped state must be published before finalizing the writer");
        const string workerCleanupPattern =
            @"try\s*\{\s*if\s*\(abort\)\s*writer\.Delete\(\);\s*" +
            @"else\s*writer\.Close\(\);\s*\}\s*" +
            @"catch\s*\(Exception\s+cleanupException\)\s*\{\s*" +
            @"Trace\.WriteLine\(\s*""Icecast streaming cleanup failed: ""\s*\+\s*" +
            @"cleanupException\.GetType\(\)\.Name\);\s*\}\s*" +
            @"finally\s*\{\s*abortClose\s*=\s*false;\s*\}";
        const string connectionCleanupPattern =
            @"IcecastWriter\s+icecastWriter\s*=\s*null;\s*try\s*\{\s*" +
            @"icecastWriter\s*=\s*new\s+IcecastWriter\(\s*" +
            @"_mixer\.PCM,\s*_icecastSettings\);\s*" +
            @"icecastWriter\.Connect\(\);[\s\S]*?" +
            @"catch\s*\(Exception\s+ex\)\s*\{\s*" +
            @"Trace\.WriteLine\(""Icecast connection failed: ""\s*\+\s*" +
            @"ex\.GetType\(\)\.Name\);\s*" +
            @"if\s*\(icecastWriter\s*!=\s*null\)\s*\{\s*try\s*\{\s*" +
            @"icecastWriter\.Close\(\);\s*\}\s*" +
            @"catch\s*\(Exception\s+cleanupException\)\s*\{\s*" +
            @"Trace\.WriteLine\(\s*""Icecast connection cleanup failed: ""\s*\+\s*" +
            @"cleanupException\.GetType\(\)\.Name\);\s*\}\s*\}\s*" +
            @"SetTransmitStoppedState\(\s*null,\s*" +
            @"""Connection failed"",\s*" +
            @"""Connection failed\. Check the server and credential settings\.""\s*\);";

        Assert.IsTrue(
            Regex.IsMatch(
                worker,
                workerCleanupPattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            "the background cleanup must contain exceptions and reset abort state");
        Assert.IsTrue(
            Regex.IsMatch(
                player,
                connectionCleanupPattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            "construction and connection cleanup must preserve the primary UI error path");
        Assert.IsFalse(player.Contains(
            "cleanupException.Message", StringComparison.Ordinal));
        Assert.IsFalse(player.Contains(
            "cleanupException.ToString", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ClassicMotdUsesTheLiveBoundedHttpsTextEndpoint()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(repoRoot, "could not locate the repository root");

        string source = File.ReadAllText(
            Path.Combine(repoRoot, "CUETools", "frmCUETools.cs"));

        Assert.IsTrue(source.Contains(
            "https://cue.tools/motd/motd.txt",
            StringComparison.Ordinal));
        Assert.IsFalse(source.Contains(
            "https://cuetools.net/motd/",
            StringComparison.Ordinal));
        Assert.IsTrue(source.Contains(
            "ReadBoundedUtf8(respStream, 262144)",
            StringComparison.Ordinal));
        Assert.IsFalse(source.Contains(
            "Image.FromStream",
            StringComparison.Ordinal));
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j])
                j++;
            if (j == needle.Length)
                return true;
        }
        return false;
    }
}
