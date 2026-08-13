using System;
using System.IO;
using CUETools.Processor;
using CUETools.Processor.Settings;

namespace CUETools.Wpf.Services;

/// <summary>
/// Persists ALL user settings across restarts: the engine's CUEConfig (its own battle-tested
/// Save/Load covers every option the Settings page exposes) plus the app-level AppSettings, in one
/// file - %AppData%\CUETools2026\settings.txt (the same key=value profile format the classic
/// CUETools apps use). Loaded once at startup, saved on exit. First run has no file, so the
/// factory defaults stand and the first exit writes them.
/// </summary>
public sealed class SettingsStore
{
    private const string AppName = "CUETools2026";
    private const string FileName = "settings.txt";
    private const string ProtectedProxyPasswordKey = "WpfProxyPasswordProtected";
    private const string ProtectedTheAudioDbApiKey = "WpfTheAudioDbApiKeyProtected";
    private readonly IDiagnosticLog _log;
    private readonly string? _appPath;   // null = the real %AppData% profile; set only by tests
    private readonly ISecretProtector _proxySecretProtector;
    private readonly ISecretProtector _theAudioDbSecretProtector;

    /// <summary><paramref name="appPath"/> follows the engine's own portable-mode convention
    /// (see SettingsShared.GetProfileDir): it is a FILE path, and the profile is redirected to
    /// &lt;that file's directory&gt;\CUETools2026 unless a "user_profiles_enabled" marker sits beside it.
    /// Null means the real ApplicationData CUETools2026 settings.txt. Each head supplies its
    /// platform's secret protectors (WPF: DPAPI; heads without protected storage: a declining
    /// protector, keeping secrets out of the profile).</summary>
    public SettingsStore(IDiagnosticLog log, string? appPath, ISecretProtector secretProtector)
        : this(log, appPath, secretProtector, secretProtector) { }

    public SettingsStore(
        IDiagnosticLog log,
        string? appPath,
        ISecretProtector proxySecretProtector,
        ISecretProtector theAudioDbSecretProtector)
    {
        _log = log;
        _appPath = appPath;
        _proxySecretProtector = proxySecretProtector ??
            throw new ArgumentNullException(nameof(proxySecretProtector));
        _theAudioDbSecretProtector = theAudioDbSecretProtector ??
            throw new ArgumentNullException(nameof(theAudioDbSecretProtector));
    }

    /// <summary>The file this store reads and writes. Taken from the reader's OWN resolved profile
    /// directory rather than recomputed here, so the path Load checks can never disagree with the path
    /// Save wrote (they did when this was resolved independently: appPath is a file path fed through the
    /// engine's portable-mode rules, not a directory).</summary>
    public string SettingsFilePath =>
        Path.Combine(new SettingsReader(AppName, FileName, _appPath).ProfilePath, FileName);

    public void Load(CUEConfig config, AppSettings app)
    {
        try
        {
            string path = SettingsFilePath;
            // only Load over an existing file: CUEConfig.Load resets absent keys to ENGINE defaults,
            // which would wipe the app's own factory defaults on a first run
            if (!File.Exists(path)) { _log.Info("settings", "no saved settings (first run) - using defaults"); return; }

            var sr = new SettingsReader(AppName, FileName, _appPath);
            config.Load(sr);
            if (config.AdvancedSettingsRejected)
                _log.Warn("settings", "advanced settings were rejected; previous/default values retained");
            LoadProxyCredential(sr, config);
            app.TheAudioDbEnabled =
                sr.LoadBoolean("WpfTheAudioDbEnabled") ?? false;
            LoadTheAudioDbCredential(sr, app);
            HealEncoderChoices(config);
            app.PreventSleepDuringRip = sr.LoadBoolean("WpfPreventSleep") ?? app.PreventSleepDuringRip;
            app.LockTrayDuringRip = sr.LoadBoolean("WpfLockTray") ?? app.LockTrayDuringRip;
            app.StopOnUnrecoverable = sr.LoadBoolean("WpfStopOnUnrecoverable") ?? app.StopOnUnrecoverable;
            app.OutputBaseDir = sr.Load("WpfOutputBaseDir") ?? "";
            app.SelectedFormat = sr.Load("WpfSelectedFormat") ?? "";
            // 0-2 = engine qualities; 3 = the Salvage capture selection (R119).
            app.CorrectionQuality = Math.Max(0, Math.Min(3, sr.LoadInt32("WpfCorrectionQuality", 0, 3) ?? 1));
            app.ArchivalDefaultsApplied = sr.LoadBoolean("WpfArchivalDefaultsApplied") ?? false;
            app.DefaultsV2Applied = sr.LoadBoolean("WpfDefaultsV2Applied") ?? false;
            app.DefaultsV3Applied = sr.LoadBoolean("WpfDefaultsV3Applied") ?? false;
            app.RipOutputLayout =
                Enum.TryParse(
                    sr.Load("WpfRipOutputLayout"),
                    ignoreCase: false,
                    out RipOutputLayout outputLayout) &&
                Enum.IsDefined(typeof(RipOutputLayout), outputLayout)
                    ? outputLayout
                    : RipOutputLayout.Tracks;
            app.FormatTypeOverrides = sr.Load("WpfFormatTypeOverrides") ?? "";
            app.ExternalEncoderApprovals = sr.Load("WpfExternalEncoderApprovals") ?? "";
            app.AdaptiveReadSpeed = sr.LoadBoolean("WpfAdaptiveReadSpeed") ?? true;
            // Deep recovery defaults ON (proven bit-exact, engages only on stuck windows / caching
            // drives). A profile with no saved value gets the new default; once the user toggles it,
            // that choice persists.
            app.DeepRecovery = sr.LoadBoolean("WpfDeepRecovery") ?? true;
            app.NamingTemplate = sr.Load("WpfNamingTemplate") ?? app.NamingTemplate;
            app.NamingExtractFeatured = sr.LoadBoolean("WpfNamingExtractFeatured") ?? true;
            app.NamingUnifySeparators = sr.LoadBoolean("WpfNamingUnifySeparators") ?? true;
            app.NamingHandleArticles = sr.LoadBoolean("WpfNamingHandleArticles") ?? true;
            app.NamingStripIllegal = sr.LoadBoolean("WpfNamingStripIllegal") ?? true;
            app.NamingReleaseDescriptor = sr.LoadBoolean("WpfNamingReleaseDescriptor") ?? true;
            _log.Info("settings", "settings loaded");
        }
        catch (Exception ex)
        {
            // a corrupt file must not stop the app from launching; defaults stand
            _log.Warn("settings", "settings load failed - using defaults: " + ex.GetType().Name);
        }
        finally
        {
            // CopyAlbumArt is a legacy engine switch with no WPF control. Every engine art path
            // treats it as an alternative to the visible embed/extract choices, so allowing the
            // engine's true default (or a persisted true value) to survive would silently defeat
            // those choices. Keep this WPF-only invariant after every load outcome, including an
            // old profile with no CopyAlbumArt key and a corrupt/partially loaded profile.
            config.CopyAlbumArt = false;
        }
    }

    private void LoadProxyCredential(SettingsReader reader, CUEConfig config)
    {
        string legacyPlaintext = config.advanced.ProxyPassword ?? "";
        string protectedValue = reader.Load(ProtectedProxyPasswordKey);

        if (!string.IsNullOrEmpty(protectedValue))
        {
            try
            {
                string secret = _proxySecretProtector.Unprotect(protectedValue);
                config.advanced.ProxyPassword = secret;
                _log.Redact(secret);
            }
            catch (Exception ex)
            {
                // Never fall back to a stale plaintext copy when a protected value exists. A
                // corrupt or wrong-user value is treated as no credential until the user sets it.
                config.advanced.ProxyPassword = "";
                _log.Warn("settings", "protected proxy credential unavailable; clear and set it again (" +
                    ex.GetType().Name + ")");
            }
            return;
        }

        if (!string.IsNullOrEmpty(legacyPlaintext))
        {
            _log.Redact(legacyPlaintext);
            _log.Info("settings", "legacy proxy credential loaded; it will be protected on next save");
        }
    }

    private void LoadTheAudioDbCredential(SettingsReader reader, AppSettings app)
    {
        string protectedValue = reader.Load(ProtectedTheAudioDbApiKey);
        app.TheAudioDbApiKey = "";
        if (string.IsNullOrEmpty(protectedValue))
            return;
        try
        {
            app.TheAudioDbApiKey =
                _theAudioDbSecretProtector.Unprotect(protectedValue);
            _log.Redact(app.TheAudioDbApiKey);
        }
        catch (Exception ex)
        {
            app.TheAudioDbEnabled = false;
            _log.Warn(
                "settings",
                "protected TheAudioDB API key unavailable; clear and set it again (" +
                ex.GetType().Name + ")");
        }
    }

    // A settings file saved by an older build can pin a format's encoder to an external
    // command-line exe (e.g. mp3 -> "lame.exe") from before the in-process codec was bundled.
    // That exe does not ship, so the choice would fail at encode time and hide the format from
    // the lossy list. Heal: when the saved choice is a CLI encoder but the fresh defaults now
    // prefer an in-process one, take the in-process one.
    private void HealEncoderChoices(CUEConfig config)
    {
        try
        {
            foreach (var kv in config.formats)
            {
                var f = kv.Value;
                if (f.encoderLossy != null && f.encoderLossy.Settings is CUETools.Codecs.CommandLine.EncoderSettings)
                {
                    var d = config.Encoders.GetDefault(kv.Key, false);
                    if (d != null && d.Settings is not CUETools.Codecs.CommandLine.EncoderSettings)
                    {
                        f.encoderLossy = d;
                        _log.Info("settings", $"format {kv.Key}: lossy encoder healed to {d.Name}");
                    }
                }
                if (f.encoderLossless != null && f.encoderLossless.Settings is CUETools.Codecs.CommandLine.EncoderSettings)
                {
                    var d = config.Encoders.GetDefault(kv.Key, true);
                    if (d != null && d.Settings is not CUETools.Codecs.CommandLine.EncoderSettings)
                    {
                        f.encoderLossless = d;
                        _log.Info("settings", $"format {kv.Key}: lossless encoder healed to {d.Name}");
                    }
                }
            }
        }
        catch (Exception ex) { _log.Warn("settings", "encoder heal failed: " + ex.GetType().Name); }
    }

    public void Save(CUEConfig config, AppSettings app)
    {
        try
        {
            using SettingsFileLease settingsLease =
                SettingsFileLease.Acquire(SettingsFilePath);
            string proxyPassword = config.advanced.ProxyPassword ?? "";
            _log.Redact(proxyPassword);
            string protectedProxyPassword = string.IsNullOrEmpty(proxyPassword)
                ? ""
                : _proxySecretProtector.Protect(proxyPassword);
            string theAudioDbApiKey = app.TheAudioDbApiKey ?? "";
            _log.Redact(theAudioDbApiKey);
            string protectedTheAudioDbApiKey = string.IsNullOrEmpty(theAudioDbApiKey)
                ? ""
                : _theAudioDbSecretProtector.Protect(theAudioDbApiKey);

            using var sw = new SettingsWriter(AppName, FileName, _appPath);
            // CUEConfig owns the legacy JSON shape, so clear only while it serializes. The live
            // in-memory credential remains available to the proxy after the save completes.
            config.advanced.ProxyPassword = "";
            try
            {
                config.Save(sw);
            }
            finally
            {
                config.advanced.ProxyPassword = proxyPassword;
            }
            if (!string.IsNullOrEmpty(protectedProxyPassword))
                sw.Save(ProtectedProxyPasswordKey, protectedProxyPassword);
            if (!string.IsNullOrEmpty(protectedTheAudioDbApiKey))
                sw.Save(ProtectedTheAudioDbApiKey, protectedTheAudioDbApiKey);
            sw.Save("WpfPreventSleep", app.PreventSleepDuringRip);
            sw.Save("WpfLockTray", app.LockTrayDuringRip);
            sw.Save("WpfStopOnUnrecoverable", app.StopOnUnrecoverable);
            sw.Save("WpfOutputBaseDir", app.OutputBaseDir);
            sw.Save("WpfSelectedFormat", app.SelectedFormat);
            sw.Save("WpfCorrectionQuality", app.CorrectionQuality);
            sw.Save("WpfArchivalDefaultsApplied", app.ArchivalDefaultsApplied);
            sw.Save("WpfDefaultsV2Applied", app.DefaultsV2Applied);
            sw.Save("WpfDefaultsV3Applied", app.DefaultsV3Applied);
            sw.Save("WpfRipOutputLayout", app.RipOutputLayout.ToString());
            sw.Save("WpfFormatTypeOverrides", app.FormatTypeOverrides);
            sw.Save("WpfExternalEncoderApprovals", app.ExternalEncoderApprovals);
            sw.Save("WpfAdaptiveReadSpeed", app.AdaptiveReadSpeed);
            sw.Save("WpfDeepRecovery", app.DeepRecovery);
            sw.Save("WpfNamingTemplate", app.NamingTemplate);
            sw.Save("WpfNamingExtractFeatured", app.NamingExtractFeatured);
            sw.Save("WpfNamingUnifySeparators", app.NamingUnifySeparators);
            sw.Save("WpfNamingHandleArticles", app.NamingHandleArticles);
            sw.Save("WpfNamingStripIllegal", app.NamingStripIllegal);
            sw.Save("WpfNamingReleaseDescriptor", app.NamingReleaseDescriptor);
            sw.Save("WpfTheAudioDbEnabled",
                app.TheAudioDbEnabled && !string.IsNullOrEmpty(theAudioDbApiKey));
            sw.Close();
            _log.Info("settings", "settings saved");
        }
        catch (Exception ex)
        {
            _log.Warn("settings", "settings save failed: " + ex.GetType().Name);
        }
    }
}
