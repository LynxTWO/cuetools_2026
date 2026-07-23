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
    private readonly IDiagnosticLog _log;

    public SettingsStore(IDiagnosticLog log) => _log = log;

    public void Load(CUEConfig config, AppSettings app)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, FileName);
            // only Load over an existing file: CUEConfig.Load resets absent keys to ENGINE defaults,
            // which would wipe the app's own factory defaults on a first run
            if (!File.Exists(path)) { _log.Info("settings", "no saved settings (first run) - using defaults"); return; }

            var sr = new SettingsReader(AppName, FileName, null);
            config.Load(sr);
            HealEncoderChoices(config);
            app.PreventSleepDuringRip = sr.LoadBoolean("WpfPreventSleep") ?? app.PreventSleepDuringRip;
            app.LockTrayDuringRip = sr.LoadBoolean("WpfLockTray") ?? app.LockTrayDuringRip;
            app.StopOnUnrecoverable = sr.LoadBoolean("WpfStopOnUnrecoverable") ?? app.StopOnUnrecoverable;
            app.OutputBaseDir = sr.Load("WpfOutputBaseDir") ?? "";
            app.SelectedFormat = sr.Load("WpfSelectedFormat") ?? "";
            app.CorrectionQuality = Math.Max(0, Math.Min(2, sr.LoadInt32("WpfCorrectionQuality", 0, 2) ?? 1));
            app.ArchivalDefaultsApplied = sr.LoadBoolean("WpfArchivalDefaultsApplied") ?? false;
            _log.Info("settings", "settings loaded");
        }
        catch (Exception ex)
        {
            // a corrupt file must not stop the app from launching; defaults stand
            _log.Warn("settings", "settings load failed - using defaults: " + ex.GetType().Name);
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
            var sw = new SettingsWriter(AppName, FileName, null);
            config.Save(sw);
            sw.Save("WpfPreventSleep", app.PreventSleepDuringRip);
            sw.Save("WpfLockTray", app.LockTrayDuringRip);
            sw.Save("WpfStopOnUnrecoverable", app.StopOnUnrecoverable);
            sw.Save("WpfOutputBaseDir", app.OutputBaseDir);
            sw.Save("WpfSelectedFormat", app.SelectedFormat);
            sw.Save("WpfCorrectionQuality", app.CorrectionQuality);
            sw.Save("WpfArchivalDefaultsApplied", app.ArchivalDefaultsApplied);
            sw.Close();
            _log.Info("settings", "settings saved");
        }
        catch (Exception ex)
        {
            _log.Warn("settings", "settings save failed: " + ex.GetType().Name);
        }
    }
}
