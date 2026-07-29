using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    public string[] AcceptedExeNames = Array.Empty<string>();
    public string DownloadUrl = "";     // OFFICIAL project download page
    public string ResolvedPath = "";    // where we found it ("" = not installed)
    public bool Found => ResolvedPath.Length > 0;
}

/// <summary>
/// App-level encoder catalog. Three jobs:
///  1. register formats/encoders the engine does not carry by default when their output assurance
///     contract is complete (currently Musepack .mpc);
///  2. resolve external encoder exes (the app's own encoders folder, the configured path, PATH)
///     and import a user-picked exe into %AppData%\CUETools2026\encoders;
///  3. the single "is this encoder actually usable" rule shared by the format lists: in-process
///     always; command-line only when its exe is really present (offering a format whose encoder
///     is missing would fail at encode time - a lie).
/// </summary>
public sealed class EncoderCatalog
{
    private readonly IDiagnosticLog _log;
    private readonly AppSettings _app;
    private readonly string _encodersDir;
    public static string EncodersDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CUETools2026", "encoders");

    /// <summary>Raised when an encoder was imported or a format's lossless/lossy type changed,
    /// so format dropdowns can rebuild live.</summary>
    public event EventHandler? Changed;

    public EncoderCatalog(IDiagnosticLog log, AppSettings app)
        : this(log, app, EncodersDir) { }

    internal EncoderCatalog(IDiagnosticLog log, AppSettings app, string encodersDir)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _encodersDir = Path.GetFullPath(
            encodersDir ?? throw new ArgumentNullException(nameof(encodersDir)));
    }

    // The externally-obtainable encoders this app curates. Download links are the OFFICIAL project
    // pages (never mirrors), and the import copies the exe under the app's own folder.
    public static readonly (string enc, string ext, string name, bool lossless, string exe, string url)[] Known =
    {
        ("mpcenc.exe",  "mpc",  "Musepack",       false, "mpcenc.exe",  "https://www.musepack.net/index.php?pg=win"),
        ("takc.exe",    "tak",  "TAK",            true,  "takc.exe",    "http://thbeck.de/Tak/Tak.html"),
        ("oggenc.exe",  "ogg",  "Ogg Vorbis",     false, "oggenc.exe",  "https://www.rarewares.org/ogg-oggenc.php"),
        ("opusenc.exe", "opus", "Opus",           false, "opusenc.exe", "https://opus-codec.org/downloads/"),
        ("qaac.exe (tvbr)", "m4a", "AAC (qaac)",  false, "qaac.exe",    "https://github.com/nu774/qaac/releases"),
    };

    private static string[] AcceptedNames(string encoderName, string preferredName) =>
        encoderName switch
        {
            "qaac.exe (tvbr)" => new[] { "qaac.exe", "qaac64.exe" },
            "oggenc.exe" => new[] { "oggenc.exe", "oggenc2.exe" },
            _ => new[] { preferredName },
        };

    /// <summary>Register engine-external formats and migrate evidence-backed verifier contracts
    /// (idempotent; call after config load). Formats surface only when their executable and, for
    /// lossless command encoders, independent decoder contract are both usable.</summary>
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

            // Older profiles predate independent command-output verification. Migrate only exact
            // built-in identities whose self-decoder syntax is already evidenced in this repo.
            foreach (var encoder in config.Encoders)
            {
                if (encoder.Settings is not CUETools.Codecs.CommandLine.EncoderSettings cli ||
                    !cli.Lossless ||
                    (cli.VerificationRequired && cli.HasLosslessVerifier))
                    continue;
                if (cli.Extension == "flac" && cli.Name == "flac.exe")
                    ConfigureSelfVerifier(
                        cli, "--decode --stdout --totally-silent %I");
                else if (cli.Extension == "tak" && cli.Name == "takc.exe")
                    ConfigureSelfVerifier(cli, "-d %I -");
                else if (cli.Extension == "m4a" && cli.Name == "ffmpeg.exe")
                    ConfigureSelfVerifier(cli, "-v error -i %I -f wav -");
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

            // OptimFROG is intentionally not registered. The local SDK is decode-only and the
            // repository has no primary evidence for a pipe-to-WAV decoder invocation. Advertising
            // its encoder would bypass the mandatory independent lossless verification contract.
        }
        catch (Exception ex) { _log.Warn("encoders", "external format registration failed: " + ex.GetType().Name); }
    }

    private static void ConfigureSelfVerifier(
        CUETools.Codecs.CommandLine.EncoderSettings settings,
        string parameters)
    {
        settings.VerificationUsesEncoder = true;
        settings.VerificationPath = "";
        settings.VerificationParameters = parameters;
        settings.VerificationRequired = true;
    }

    /// <summary>The single usability rule: in-process encoders always work; a command-line encoder
    /// must resolve on this machine. New lossless commands require a decoder contract whose
    /// executable also resolves on this machine (either the encoder itself or a separate decoder).
    /// Pre-contract custom profiles remain usable but are explicitly labeled unverified until the
    /// user supplies a decoder contract.</summary>
    public bool IsUsable(AudioEncoderSettingsViewModel? enc)
    {
        if (enc == null) return false;
        if (enc.Settings is not CUETools.Codecs.CommandLine.EncoderSettings cli) return true;
        if (ResolveExe(enc) == null)
            return false;
        if (!cli.VerificationRequired)
            return true;
        if (!cli.HasLosslessVerifier)
            return false;
        return cli.VerificationUsesEncoder ||
            ResolveVerificationExe(cli) != null;
    }

    /// <summary>
    /// Resolve and freeze a separately configured verification decoder. Unlike imported encoders,
    /// this is an explicitly user-managed compatibility path: it is never copied into or silently
    /// loaded from the app-managed encoders directory, whose files require host-owned receipts.
    /// </summary>
    internal string? ResolveVerificationExe(
        CUETools.Codecs.CommandLine.EncoderSettings settings)
    {
        try
        {
            string configured = settings.VerificationPath ?? "";
            if (string.IsNullOrWhiteSpace(configured))
                return null;

            bool hasDirectory =
                Path.IsPathRooted(configured) ||
                configured.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                configured.IndexOf(Path.AltDirectorySeparatorChar) >= 0;
            if (hasDirectory)
            {
                string absolute = Path.GetFullPath(configured);
                if (!File.Exists(absolute) ||
                    IsWithinManagedDirectory(absolute))
                    return null;
                settings.VerificationPath = absolute;
                return absolute;
            }

            if (!IsSimpleExecutableName(configured))
                return null;

            string beside = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, configured));
            if (File.Exists(beside))
            {
                settings.VerificationPath = beside;
                return beside;
            }

            foreach (string dir in
                (Environment.GetEnvironmentVariable("PATH") ?? "")
                    .Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                try
                {
                    string candidate = Path.GetFullPath(
                        Path.Combine(dir.Trim().Trim('"'), configured));
                    if (!File.Exists(candidate) ||
                        IsWithinManagedDirectory(candidate))
                        continue;
                    settings.VerificationPath = candidate;
                    return candidate;
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn("encoders",
                "verification decoder resolution failed: " +
                ex.GetType().Name);
        }
        return null;
    }

    /// <summary>The app's lossy-ness rule for a format, one meaning per dropdown entry. The USER'S
    /// explicit type choice (the picker in the encoder dialog, persisted) wins when that side is
    /// actually usable. Otherwise the default priority: an IN-PROCESS lossy encoder wins (mp3,
    /// wma); a usable command-line lossy encoder wins only when the format has no in-process
    /// lossless alternative (mpc/ogg/opus have none - m4a keeps ALAC even if qaac is imported).</summary>
    public bool IsLossyFormat(CUEToolsFormat f)
    {
        bool lossyUsable = f.allowLossy && IsUsable(f.encoderLossy);
        bool losslessUsable = f.allowLossless && IsUsable(f.encoderLossless);
        bool? choice = _app.GetFormatTypeOverride(f.extension);
        if (choice == true && lossyUsable) return true;
        if (choice == false && losslessUsable) return false;

        if (!lossyUsable) return false;
        bool lossyInProcess = f.encoderLossy!.Settings is not CUETools.Codecs.CommandLine.EncoderSettings;
        if (lossyInProcess) return true;
        bool losslessInProcess = f.allowLossless && f.encoderLossless != null
            && f.encoderLossless.Settings is not CUETools.Codecs.CommandLine.EncoderSettings;
        return !losslessInProcess;
    }

    /// <summary>A format where BOTH faces are genuinely usable (wma always: WMA Lossless and WMA
    /// Standard are both in-process; m4a once an AAC encoder is imported) - these get the
    /// lossless/lossy type picker in the encoder dialog.</summary>
    public bool HasBothTypes(CUEToolsFormat f) =>
        f.allowLossy && f.allowLossless && IsUsable(f.encoderLossy) && IsUsable(f.encoderLossless);

    /// <summary>Persist the user's type choice for a two-faced format and rebuild every format list.</summary>
    public void SetFormatType(string ext, bool lossy)
    {
        _app.SetFormatTypeOverride(ext, lossy);
        _log.Info("encoders", $"format {ext}: type set to {(lossy ? "lossy" : "lossless")}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Find a command-line encoder's exe. Files copied into the app-managed encoders directory
    /// require an exact import receipt match. An explicitly configured absolute path outside that
    /// directory, an app-adjacent tool, and PATH remain user-managed compatibility paths; the app
    /// makes no publisher or origin claim for them.
    /// </summary>
    public string? ResolveExe(AudioEncoderSettingsViewModel enc)
    {
        try
        {
            ClearRuntimeApproval(enc);
            string exe = enc.Path ?? "";
            if (exe.Length > 0 && Path.IsPathRooted(exe) && File.Exists(exe))
            {
                string absolute = Path.GetFullPath(exe);
                if (IsWithinManagedDirectory(absolute) &&
                    !ValidateManagedEncoder(absolute, enc))
                    return null;
                enc.Path = absolute;
                return absolute;
            }

            string name = exe.Length > 0 ? Path.GetFileName(exe) : enc.Name.Split(' ')[0];
            if (!IsSimpleExecutableName(name))
                return null;

            string local = Path.Combine(_encodersDir, name);
            if (File.Exists(local))
            {
                // A present but unapproved/changed managed file is a hard refusal. Falling through
                // to PATH here could silently run a different executable than the user imported.
                if (!ValidateManagedEncoder(local, enc))
                    return null;
                enc.Path = local;
                return local;
            }

            string beside = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, name));
            if (File.Exists(beside))
            {
                enc.Path = beside;
                return beside;
            }
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string p = Path.GetFullPath(
                        Path.Combine(dir.Trim().Trim('"'), name));
                    if (!File.Exists(p))
                        continue;
                    enc.Path = p;
                    return p;
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn("encoders", "encoder resolution failed: " + ex.GetType().Name);
        }
        return null;
    }

    /// <summary>Copy a user-picked encoder exe into the app's encoders folder and point the engine
    /// entry at it. Returns the error text, or null on success.</summary>
    public string? Import(CUEConfig config, ExternalEncoderInfo info, string pickedFile)
    {
        string? stagePath = null;
        try
        {
            if (!File.Exists(pickedFile))
                return "File not found.";
            string pickedName = Path.GetFileName(pickedFile);
            string[] accepted = info.AcceptedExeNames.Length == 0
                ? new[] { info.ExeName }
                : info.AcceptedExeNames;
            if (!accepted.Any(
                    name => pickedName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return $"Expected {string.Join(" or ", accepted)} (got {pickedName}).";
            if (!IsSimpleExecutableName(pickedName))
                return "The expected executable name is invalid.";
            if (IsReparsePoint(pickedFile))
                return "Symbolic-link and reparse-point imports are not allowed.";

            EnsureManagedDirectory();
            string dest = Path.GetFullPath(Path.Combine(_encodersDir, pickedName));
            if (!IsDirectChild(_encodersDir, dest))
                return "The import destination is invalid.";
            if (File.Exists(dest) && IsReparsePoint(dest))
                return "The managed encoder destination is a reparse point.";

            stagePath = Path.Combine(
                _encodersDir,
                "." + Path.GetFileNameWithoutExtension(pickedName) + "." +
                Guid.NewGuid().ToString("N") + ".importing.exe");
            CopyToOwnedStage(pickedFile, stagePath);

            FileIdentity stagedIdentity = ReadIdentity(stagePath);
            var approval = CreateApproval(
                info, pickedName, pickedFile, stagePath, stagedIdentity);
            string updatedApprovals = ExternalEncoderApprovalCodec.Upsert(
                _app.ExternalEncoderApprovals, approval);
            PublishStage(stagePath, dest);
            stagePath = null;

            FileIdentity publishedIdentity = ReadIdentity(dest);
            if (publishedIdentity.Length != stagedIdentity.Length ||
                !string.Equals(
                    publishedIdentity.Sha256,
                    stagedIdentity.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("The imported executable changed during publication.");

            _app.ExternalEncoderApprovals = updatedApprovals;
            var enc = FindEncoder(config, info);
            if (enc != null) enc.Path = dest;
            _log.Info("encoders", $"imported {pickedName} for {info.Extension}");
            Changed?.Invoke(this, EventArgs.Empty);
            return null;
        }
        catch (Exception ex)
        {
            _log.Warn("encoders", $"import {info.ExeName} failed: {ex.GetType().Name}");
            return "The executable could not be imported safely (" + ex.GetType().Name + ").";
        }
        finally
        {
            if (!string.IsNullOrEmpty(stagePath))
            {
                try { File.Delete(stagePath); }
                catch (Exception ex)
                {
                    _log.Warn(
                        "encoders",
                        "temporary encoder import cleanup failed: " + ex.GetType().Name);
                }
            }
        }
    }

    private bool ValidateManagedEncoder(string path, AudioEncoderSettingsViewModel enc)
    {
        string fileName = Path.GetFileName(path);
        string reason;
        try
        {
            EnsureManagedDirectory(allowCreate: false);
            string fullPath = Path.GetFullPath(path);
            if (!IsDirectChild(_encodersDir, fullPath))
                reason = "path";
            else if (IsReparsePoint(fullPath))
                reason = "reparse";
            else if (!_app.TryGetExternalEncoderApproval(fileName, out var approval) ||
                approval == null)
                reason = "approval";
            else if (!string.Equals(
                approval.FileName, fileName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    approval.EncoderName, enc.Name, StringComparison.Ordinal) ||
                !string.Equals(
                    approval.Extension, enc.Extension, StringComparison.Ordinal))
                reason = "identity";
            else
            {
                FileIdentity identity = ReadIdentity(fullPath);
                if (identity.Length != approval.Length)
                    reason = "size";
                else if (!string.Equals(
                    identity.Sha256, approval.Sha256, StringComparison.OrdinalIgnoreCase))
                    reason = "hash";
                else
                {
                    BindRuntimeApproval(enc, identity);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            reason = ex.GetType().Name;
        }

        // Log only the curated file name and a bounded reason code. The user-selected source path
        // is provenance stored in reduced form, not diagnostic-log content.
        _log.Warn("encoders", $"managed encoder {fileName} refused ({reason})");
        return false;
    }

    private static void ClearRuntimeApproval(AudioEncoderSettingsViewModel enc)
    {
        if (enc.Settings is not CUETools.Codecs.CommandLine.EncoderSettings settings)
            return;
        settings.ApprovedExecutableSha256 = "";
        settings.ApprovedExecutableLength = 0;
    }

    private static void BindRuntimeApproval(
        AudioEncoderSettingsViewModel enc,
        FileIdentity identity)
    {
        if (enc.Settings is not CUETools.Codecs.CommandLine.EncoderSettings settings)
            return;
        settings.ApprovedExecutableSha256 = identity.Sha256;
        settings.ApprovedExecutableLength = identity.Length;
    }

    private ExternalEncoderApproval CreateApproval(
        ExternalEncoderInfo info,
        string publishedFileName,
        string pickedFile,
        string stagedFile,
        FileIdentity identity)
    {
        FileVersionInfo? versionInfo = null;
        try
        {
            versionInfo = FileVersionInfo.GetVersionInfo(stagedFile);
        }
        catch
        {
            // Version resources are optional. The receipt records "unavailable" while the hash
            // and size remain authoritative.
        }

        return new ExternalEncoderApproval
        {
            FileName = publishedFileName,
            EncoderName = info.EncoderName,
            Extension = info.Extension,
            Sha256 = identity.Sha256,
            Length = identity.Length,
            FileVersion = ExternalEncoderApprovalCodec.Normalize(versionInfo?.FileVersion),
            ProductName = ExternalEncoderApprovalCodec.Normalize(versionInfo?.ProductName),
            OriginalFileName = ExternalEncoderApprovalCodec.Normalize(
                Path.GetFileName(versionInfo?.OriginalFilename ?? "")),
            SourceFileName = ExternalEncoderApprovalCodec.Normalize(Path.GetFileName(pickedFile)),
            OriginKind = "user-selected-local-file",
            ImportedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };
    }

    private static void CopyToOwnedStage(string sourcePath, string stagePath)
    {
        using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var stage = new FileStream(
            stagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(stage);
        stage.Flush(flushToDisk: true);
    }

    private static void PublishStage(string stagePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
            File.Replace(stagePath, destinationPath, null);
        else
            File.Move(stagePath, destinationPath);
    }

    private void EnsureManagedDirectory(bool allowCreate = true)
    {
        if (allowCreate)
            Directory.CreateDirectory(_encodersDir);
        FileAttributes attributes = File.GetAttributes(_encodersDir);
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The managed encoder directory is not a regular directory.");
    }

    private bool IsWithinManagedDirectory(string path)
    {
        string root = _encodersDir.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectChild(string directory, string path)
    {
        string? parent = Path.GetDirectoryName(path);
        return parent != null &&
            string.Equals(
                Path.GetFullPath(parent).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(directory).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSimpleExecutableName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal) &&
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static FileIdentity ReadIdentity(string path)
    {
        if (IsReparsePoint(path))
            throw new IOException("Reparse-point executables are not allowed.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using SHA256 sha = SHA256.Create();
        byte[] digest = sha.ComputeHash(stream);
        return new FileIdentity(stream.Length, Convert.ToHexString(digest));
    }

    private readonly struct FileIdentity
    {
        public FileIdentity(long length, string sha256)
        {
            Length = length;
            Sha256 = sha256;
        }

        public long Length { get; }
        public string Sha256 { get; }
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
            {
                EncoderName = enc,
                Extension = ext,
                FormatName = name,
                Lossless = lossless,
                ExeName = exe,
                AcceptedExeNames = AcceptedNames(enc, exe),
                DownloadUrl = url
            };
            var vm = FindEncoder(config, info);
            if (vm != null)
            {
                string originalPath = vm.Path ?? "";
                info.ResolvedPath = ResolveExe(vm) ?? "";
                if (info.ResolvedPath.Length == 0 &&
                    MayDiscoverManagedAlias(originalPath, info.ExeName))
                {
                    foreach (string alias in info.AcceptedExeNames)
                    {
                        string candidate = Path.Combine(_encodersDir, alias);
                        if (!File.Exists(candidate))
                            continue;
                        vm.Path = candidate;
                        info.ResolvedPath = ResolveExe(vm) ?? "";
                        if (info.ResolvedPath.Length > 0)
                            break;
                    }
                    if (info.ResolvedPath.Length == 0)
                        vm.Path = originalPath;
                }
            }
            list.Add(info);
        }
        return list;
    }

    private bool MayDiscoverManagedAlias(
        string configuredPath,
        string preferredName)
    {
        string configured = (configuredPath ?? "").Trim();
        if (configured.Length > 0 &&
            !configured.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
            return false;
        string preferred = Path.Combine(_encodersDir, preferredName);
        // A present configured managed file that failed its receipt is a hard
        // refusal. Do not silently substitute another approved alias.
        return !File.Exists(preferred);
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
