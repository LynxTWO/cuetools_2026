using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CUETools.Codecs;
using CUETools.Processor;

namespace CUETools.Wpf.Services;

/// <summary>One external (command-line) encoder the app knows how to obtain: what it is, where the
/// official download lives, and whether its exe is currently available on this machine.</summary>
public sealed class ExternalEncoderInfo
{
    public string EncoderName = "";     // the engine encoder entry name (e.g. "mpcenc.exe")
    public string Extension = "";       // output format (e.g. "mpc")
    public string FormatName = "";      // human name (e.g. "Musepack")
    public bool Lossless;
    public string ExeName = "";         // the file the user downloads (e.g. "mpcenc.exe")
    public string DownloadUrl = "";     // OFFICIAL project download page
    public string ResolvedPath = "";    // where we found it ("" = not installed)
    public bool Found => ResolvedPath.Length > 0;
}

/// <summary>
/// App-level encoder catalog. Three jobs:
///  1. register formats/encoders the engine does not carry by default (Musepack .mpc, OptimFROG
///     .ofr) as command-line encoders, so importing the official exe makes them fully usable;
///  2. resolve external encoder exes (the app's own encoders folder, the configured path, PATH)
///     and import a user-picked exe into %AppData%\CUETools2026\encoders;
///  3. the single "is this encoder actually usable" rule shared by the format lists: in-process
///     always; command-line only when its exe is really present (offering a format whose encoder
///     is missing would fail at encode time - a lie).
/// </summary>
public sealed class EncoderCatalog
{
    private readonly IDiagnosticLog _log;
    public static string EncodersDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CUETools2026", "encoders");

    /// <summary>Raised when an encoder was imported, so format dropdowns can rebuild live.</summary>
    public event EventHandler? Changed;

    public EncoderCatalog(IDiagnosticLog log) => _log = log;

    // The externally-obtainable encoders this app curates. Download links are the OFFICIAL project
    // pages (never mirrors), and the import copies the exe under the app's own folder.
    public static readonly (string enc, string ext, string name, bool lossless, string exe, string url)[] Known =
    {
        ("mpcenc.exe",  "mpc",  "Musepack",       false, "mpcenc.exe",  "https://www.musepack.net/index.php?pg=win"),
        ("ofr.exe",     "ofr",  "OptimFROG",      true,  "ofr.exe",     "https://losslessaudio.org/"),
        ("takc.exe",    "tak",  "TAK",            true,  "takc.exe",    "http://thbeck.de/Tak/Tak.html"),
        ("oggenc.exe",  "ogg",  "Ogg Vorbis",     false, "oggenc.exe",  "https://www.rarewares.org/ogg-oggenc.php"),
        ("opusenc.exe", "opus", "Opus",           false, "opusenc.exe", "https://opus-codec.org/downloads/"),
        ("qaac.exe (tvbr)", "m4a", "AAC (qaac)",  false, "qaac.exe",    "https://github.com/nu774/qaac/releases"),
    };

    /// <summary>Register the formats/encoders the engine lacks (idempotent; call after config
    /// load). Musepack and OptimFROG become real command-line encoder entries with sensible
    /// quality scales; they surface in the format lists once their exe is imported.</summary>
    public void EnsureRegistered(CUEConfig config)
    {
        try
        {
            // add to BOTH lists: advanced.encoders is the persisted model (serialized into the
            // Advanced JSON blob); config.Encoders is the live BindingList the app reads
            void AddEncoder(IAudioEncoderSettings s)
            {
                config.advanced.encoders.Add(s);
                config.Encoders.Add(new AudioEncoderSettingsViewModel(s));
            }
            bool HasEncoder(string ext, string name)
            {
                foreach (var e in config.Encoders)
                    if (e.Extension == ext && e.Name == name) return true;
                return false;
            }

            // migration: an earlier build registered Musepack under the old SV7 name (mppenc.exe);
            // the current official encoder is mpcenc.exe (SV8). Drop the stale entry so the format
            // re-wires to the right one below.
            for (int i = config.Encoders.Count - 1; i >= 0; i--)
                if (config.Encoders[i].Extension == "mpc" && config.Encoders[i].Name == "mppenc.exe")
                {
                    var stale = config.Encoders[i].Settings;
                    config.Encoders.RemoveAt(i);
                    config.advanced.encoders.Remove(stale);
                    if (config.formats.TryGetValue("mpc", out var mf) && mf.encoderLossy?.Name == "mppenc.exe")
                        mf.encoderLossy = null;
                    _log.Info("encoders", "migrated stale mppenc.exe entry to mpcenc.exe");
                }

            if (!HasEncoder("mpc", "mpcenc.exe"))
                // Musepack SV8 (mpcenc, the current official encoder): --quality 0..10 (5 = standard,
                // 7 ~ archival sweet spot). Reads WAV from stdin - verified against the official
                // 2009-04-02 build.
                AddEncoder(new CUETools.Codecs.CommandLine.EncoderSettings(
                    "mpcenc.exe", "mpc", false, "0 1 2 3 4 5 6 7 8 9 10", "7", "mpcenc.exe",
                    "--silent --overwrite --quality %M - %O"));
            if (!config.formats.ContainsKey("mpc"))
            {
                config.formats.Add("mpc", new CUEToolsFormat("mpc", CUEToolsTagger.APEv2, false, true, false, true,
                    null, config.Encoders.GetDefault("mpc", false), null));
                _log.Info("encoders", "registered Musepack (mpc) format");
            }
            else if (config.formats["mpc"].encoderLossy == null)
                config.formats["mpc"].encoderLossy = config.Encoders.GetDefault("mpc", false);   // re-wire after load/migration

            if (!HasEncoder("ofr", "ofr.exe"))
                // OptimFROG: lossless, --preset 0..10 (higher = smaller + slower). Reads WAV from stdin.
                AddEncoder(new CUETools.Codecs.CommandLine.EncoderSettings(
                    "ofr.exe", "ofr", true, "0 1 2 3 4 5 6 7 8 9 10", "10", "ofr.exe",
                    "--encode - --preset %M --output %O"));
            if (!config.formats.ContainsKey("ofr"))
            {
                config.formats.Add("ofr", new CUEToolsFormat("ofr", CUEToolsTagger.APEv2, true, false, false, true,
                    config.Encoders.GetDefault("ofr", true), null, null));
                _log.Info("encoders", "registered OptimFROG (ofr) format");
            }
            else if (config.formats["ofr"].encoderLossless == null)
                config.formats["ofr"].encoderLossless = config.Encoders.GetDefault("ofr", true);   // re-wire after load
        }
        catch (Exception ex) { _log.Warn("encoders", "external format registration failed: " + ex.GetType().Name); }
    }

    /// <summary>The single usability rule: in-process encoders always work; a command-line encoder
    /// works only when its exe actually resolves on this machine.</summary>
    public bool IsUsable(AudioEncoderSettingsViewModel? enc)
    {
        if (enc == null) return false;
        if (enc.Settings is not CUETools.Codecs.CommandLine.EncoderSettings) return true;
        return ResolveExe(enc) != null;
    }

    /// <summary>The app's lossy-ness rule for a format: it encodes lossy when it has a USABLE lossy
    /// encoder (mp3, wma; mpc once its exe is imported). One meaning per dropdown entry.</summary>
    public bool IsLossyFormat(CUEToolsFormat f) => f.allowLossy && IsUsable(f.encoderLossy);

    /// <summary>Find a command-line encoder's exe: an absolute configured path, the app's encoders
    /// folder, next to the app, then PATH. Fixes up the configured path when found locally.</summary>
    public string? ResolveExe(AudioEncoderSettingsViewModel enc)
    {
        try
        {
            string exe = enc.Path ?? "";
            if (exe.Length > 0 && Path.IsPathRooted(exe) && File.Exists(exe)) return exe;
            string name = exe.Length > 0 ? Path.GetFileName(exe) : enc.Name.Split(' ')[0];
            string local = Path.Combine(EncodersDir, name);
            if (File.Exists(local)) { enc.Path = local; return local; }
            string beside = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(beside)) return beside;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try { string p = Path.Combine(dir.Trim(), name); if (File.Exists(p)) return p; } catch { }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Copy a user-picked encoder exe into the app's encoders folder and point the engine
    /// entry at it. Returns the error text, or null on success.</summary>
    public string? Import(CUEConfig config, ExternalEncoderInfo info, string pickedFile)
    {
        try
        {
            if (!File.Exists(pickedFile)) return "File not found.";
            if (!Path.GetFileName(pickedFile).Equals(info.ExeName, StringComparison.OrdinalIgnoreCase))
                return $"Expected {info.ExeName} (got {Path.GetFileName(pickedFile)}).";
            Directory.CreateDirectory(EncodersDir);
            string dest = Path.Combine(EncodersDir, info.ExeName);
            File.Copy(pickedFile, dest, overwrite: true);
            var enc = FindEncoder(config, info);
            if (enc != null) enc.Path = dest;
            _log.Info("encoders", $"imported {info.ExeName} for {info.Extension}");
            Changed?.Invoke(this, EventArgs.Empty);
            return null;
        }
        catch (Exception ex)
        {
            _log.Warn("encoders", $"import {info.ExeName} failed: {ex.GetType().Name}");
            return ex.Message;
        }
    }

    public AudioEncoderSettingsViewModel? FindEncoder(CUEConfig config, ExternalEncoderInfo info)
    {
        try
        {
            foreach (var e in config.Encoders)
                if (e.Name == info.EncoderName && e.Extension == info.Extension) return e;
        }
        catch { }
        return null;
    }

    /// <summary>Snapshot of every curated external encoder with its current install status.</summary>
    public List<ExternalEncoderInfo> Snapshot(CUEConfig config)
    {
        var list = new List<ExternalEncoderInfo>();
        foreach (var (enc, ext, name, lossless, exe, url) in Known)
        {
            var info = new ExternalEncoderInfo
            { EncoderName = enc, Extension = ext, FormatName = name, Lossless = lossless, ExeName = exe, DownloadUrl = url };
            var vm = FindEncoder(config, info);
            if (vm != null) info.ResolvedPath = ResolveExe(vm) ?? "";
            list.Add(info);
        }
        return list;
    }

    /// <summary>The owner's default policy: maximum archival compression for lossless, the
    /// efficiency sweet spot leaning archival for lossy. Applied ONCE per profile (the caller
    /// tracks a persisted flag) - encoder settings objects pre-fill their stock default mode at
    /// construction, so "never touched" cannot be detected per-encoder; after this one shift,
    /// every user choice sticks.</summary>
    public void ApplyArchivalDefaults(CUEConfig config)
    {
        // (extension|encoder name) -> mode; Flake and ALAC are both named "cuetools", so the key
        // includes the extension. Lossless: strongest mode. Lossy: documented sweet spots.
        var wanted = new Dictionary<string, string>
        {
            ["flac|cuetools"] = "8",           // FLAC: max subset compression (9..11 are non-subset)
            ["m4a|cuetools"] = "10",           // ALAC: max
            ["mp3|libmp3lame-VBR"] = "V0",     // MP3: top VBR quality - the archival-lean lossy choice
            ["wma|wma lossy"] = "90",          // WMA: quality 90 VBR - efficiency point below max 98
            ["tak|takc.exe"] = "4m",           // TAK: strongest preset (if the exe is imported)
            ["mpc|mpcenc.exe"] = "7",          // Musepack: above-standard archival sweet spot
            ["ofr|ofr.exe"] = "10",            // OptimFROG: max preset
        };
        try
        {
            foreach (var e in config.Encoders)
            {
                if (e.Settings == null) continue;
                if (!wanted.TryGetValue(e.Extension + "|" + e.Name, out var mode)) continue;
                // mode names can be PCM-dependent (WMA enumerates long names until PCM is known);
                // evaluate against CD audio, the only PCM this app encodes from
                try { e.Settings.PCM = AudioPCMConfig.RedBook; } catch { }
                var modes = (e.Settings.SupportedModes ?? "").Split(' ');
                if (Array.IndexOf(modes, mode) < 0) continue;                  // not offered for this PCM/build
                e.Settings.EncoderMode = mode;
                _log.Info("encoders", $"archival default applied: {e.Name} -> {mode}");
            }
        }
        catch (Exception ex) { _log.Warn("encoders", "archival defaults failed: " + ex.GetType().Name); }
    }
}
