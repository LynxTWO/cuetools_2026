using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public string Description = "";
    public string History = "";
    public string BestUse = "";
    public string DistributionNote = "";
    public string ResolvedPath = "";    // where we found it ("" = not installed)
    public bool Found => ResolvedPath.Length > 0;
}

/// <summary>
/// App-level encoder catalog. Three jobs:
///  1. register formats/encoders the engine does not carry by default when their output assurance
///     contract is complete;
///  2. resolve external encoder exes (the app's own encoders folder, the configured path, PATH)
///     and import a user-picked exe into %AppData%\CUETools2026\encoders;
///  3. the single "is this encoder actually usable" rule shared by the format lists: in-process
///     always; command-line only when its exe is really present (offering a format whose encoder
///     is missing would fail at encode time - a lie).
/// </summary>
public sealed class EncoderCatalog
{
    private static readonly HashSet<string> NativeEncoderSettingsTypes =
        new(StringComparer.Ordinal)
        {
            "CUETools.Codecs.libFLAC.EncoderSettings",
            "CUETools.Codecs.libwavpack.EncoderSettings",
            "CUETools.Codecs.MACLib.EncoderSettings",
            "CUETools.Codecs.libmp3lame.VBREncoderSettings",
            "CUETools.Codecs.libmp3lame.CBREncoderSettings",
        };
    private readonly IDiagnosticLog _log;
    private readonly AppSettings _app;
    private readonly string _encodersDir;
    private readonly string _bundledEncodersDir;
    private readonly IReadOnlyDictionary<string, string> _packagedEncoderHashes;
    private static readonly IReadOnlyDictionary<string, string> PackagedEncoderHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["opusenc.exe"] =
                "C414AA0B6317AAB4CC73CE659FD9527A42D51FA15E3FF975CD17A1502DA2DDAA",
            ["oggenc2.exe"] =
                "AC2C66F87695501AFD0AA664F2EB38FE84754A978ABB5BC80E90C74E55FF0C19",
            ["mpcenc.exe"] =
                "599771FF43E65439C4B752CD61634063D39A03C196833B2656376C1D2F17DA48",
        };
    public static string EncodersDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CUETools2026", "encoders");
    internal static string BundledEncodersDir =>
        Path.Combine(AppContext.BaseDirectory, "encoders");

    /// <summary>Raised when an encoder was imported or a format's lossless/lossy type changed,
    /// so format dropdowns can rebuild live.</summary>
    public event EventHandler? Changed;

    public EncoderCatalog(IDiagnosticLog log, AppSettings app)
        : this(log, app, EncodersDir, BundledEncodersDir, PackagedEncoderHashes) { }

    internal EncoderCatalog(IDiagnosticLog log, AppSettings app, string encodersDir)
        : this(log, app, encodersDir, BundledEncodersDir, PackagedEncoderHashes) { }

    internal EncoderCatalog(
        IDiagnosticLog log,
        AppSettings app,
        string encodersDir,
        string bundledEncodersDir)
        : this(log, app, encodersDir, bundledEncodersDir, PackagedEncoderHashes) { }

    internal EncoderCatalog(
        IDiagnosticLog log,
        AppSettings app,
        string encodersDir,
        string bundledEncodersDir,
        IReadOnlyDictionary<string, string> packagedEncoderHashes)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _encodersDir = Path.GetFullPath(
            encodersDir ?? throw new ArgumentNullException(nameof(encodersDir)));
        _bundledEncodersDir = Path.GetFullPath(
            bundledEncodersDir ??
                throw new ArgumentNullException(nameof(bundledEncodersDir)));
        _packagedEncoderHashes = packagedEncoderHashes ??
            throw new ArgumentNullException(nameof(packagedEncoderHashes));
    }

    // The externally-obtainable encoders this app curates. Download links are the OFFICIAL project
    // pages (never mirrors), and the import copies the exe under the app's own folder.
    public static readonly (
        string enc,
        string ext,
        string name,
        bool lossless,
        string exe,
        string url,
        string description,
        string history,
        string bestUse,
        string distribution)[] Known =
    {
        (
            "mpcenc.exe", "mpc", "Musepack", false, "mpcenc.exe",
            "https://www.musepack.net/index.php?pg=win",
            "A subband perceptual music codec tuned for transparent stereo audio at moderate and high bitrates.",
            "Musepack evolved from the MPEG Layer II family; the current SV8 bitstream and encoder date from 2009.",
            "Music-focused lossy archives where efficiency matters more than broad hardware-player support.",
            "CUETools packages a deterministic LGPL-2.1-or-later/BSD source build with complete source, patch, and build materials. A receipt-bound user import overrides the packaged fallback."
        ),
        (
            "takc.exe", "tak", "TAK", true, "takc.exe",
            "http://thbeck.de/Tak/Tak.html",
            "Thomas Becker's fast, high-ratio lossless audio codec.",
            "TAK has been developed as proprietary Windows software since the mid-2000s and is known for an unusually strong speed/compression balance.",
            "Personal lossless archives when Windows-only proprietary tooling and limited ecosystem support are acceptable.",
            "TAK is proprietary freeware. CUETools does not bundle it; import a copy obtained from the author's official package."
        ),
        (
            "oggenc.exe", "ogg", "Ogg Vorbis", false, "oggenc.exe",
            "https://xiph.org/vorbis/",
            "An open, royalty-free perceptual codec in the Ogg container.",
            "Xiph.Org introduced Vorbis around 2000 as an open alternative to patent-encumbered music codecs.",
            "Open-format music distribution and players with mature Vorbis support; Opus is usually stronger at low bitrates.",
            "oggenc is GPL-2.0. A packaged build must include the license and corresponding-source offer; imported official or user-built copies remain user controlled."
        ),
        (
            "opusenc.exe", "opus", "Opus", false, "opusenc.exe",
            "https://opus-codec.org/downloads/",
            "A modern open codec spanning speech, music, low delay, and a very wide bitrate range.",
            "The IETF standardized Opus as RFC 6716 in 2012 from Xiph.Org CELT and Skype SILK technology.",
            "Streaming, speech, and compact high-quality music copies; use lossless FLAC or WavPack for preservation masters.",
            "CUETools packages a deterministic source build with libopus 1.6.1 and the BSD notices for every linked component; imported copies override the packaged copy."
        ),
        (
            "qaac.exe (tvbr)", "m4a", "AAC (qaac)", false, "qaac.exe",
            "https://github.com/nu774/qaac/releases",
            "A Windows command-line front end for Apple's CoreAudio AAC encoder, using true-variable-bitrate quality modes.",
            "qaac has exposed Apple's well-regarded AAC implementation to command-line workflows since 2011.",
            "High-quality AAC/M4A files for Apple devices and the broad AAC playback ecosystem.",
            "qaac itself is permissively licensed but requires Apple's CoreAudio components. CUETools never redistributes those Apple components; install them separately and import qaac.exe or qaac64.exe."
        ),
        (
            "exhale.exe", "m4a", "xHE-AAC (exhale)", false, "exhale.exe",
            "https://gitlab.com/ecodis/exhale",
            "An open-source encoder for Extended HE-AAC (MPEG-D USAC) in an M4A container.",
            "exhale was created as a standards-focused xHE-AAC encoder and is strongest in its implemented medium-to-high bitrate modes.",
            "Modern xHE-AAC playback targets at medium and high bitrates; it is not a substitute for lossless preservation and does not implement every low-rate USAC tool.",
            "exhale's source license does not grant patent rights. CUETools supports user-supplied builds but does not bundle or imply patent clearance."
        ),
        (
            "ofr.exe", "ofr", "OptimFROG", true, "ofr.exe",
            "https://losslessaudio.org/Downloads.php",
            "A lossless codec designed to maximize compression ratio, accepting much slower encoding and decoding to do it.",
            "Florin Ghido's OptimFROG has pursued extreme lossless compression since the early 2000s.",
            "Cold archival experiments where storage ratio matters more than speed, portability, or ecosystem support.",
            "The author permits unmodified CLI redistribution with free software only after notification. Until that project-owner notification is completed, import the official ofr.exe yourself."
        ),
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
            void AddFormat(
                string extension,
                CUEToolsTagger tagger,
                bool lossless,
                bool lossy,
                bool allowEmbed)
            {
                if (config.formats.ContainsKey(extension))
                    return;
                config.formats.Add(
                    extension,
                    new CUEToolsFormat(
                        extension,
                        tagger,
                        lossless,
                        lossy,
                        allowEmbed,
                        true,
                        lossless ? config.Encoders.GetDefault(extension, true) : null,
                        lossy ? config.Encoders.GetDefault(extension, false) : null,
                        config.Decoders.GetDefault(extension)));
            }

            // Older profiles predate independent command-output verification. Migrate only exact
            // built-in identities whose self-decoder syntax is already evidenced in this repo.
            foreach (var encoder in config.Encoders)
            {
                if (encoder.Settings is not CUETools.Codecs.CommandLine.EncoderSettings cli)
                    continue;
                if (cli.NormalizeVerificationContract())
                    _log.Info(
                        "encoders",
                        "removed a stale lossless verification contract from " +
                        cli.Name);
                if (
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
                // Musepack SV8: --quality 0..10 (5 = standard, 7 ~ archival sweet spot).
                // Reads WAV from stdin; verified against the reproducible r495 CUETools build.
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

            // exhale 1.2.x accepts Red Book WAV on stdin and writes standards-compliant M4A.
            // Modes a..g are deliberately exposed as advanced choices: 0..9 are the conservative
            // public presets and 9 is the archival-leaning default for a lossy derivative.
            if (!HasEncoder("m4a", "exhale.exe"))
                AddEncoder(new CUETools.Codecs.CommandLine.EncoderSettings(
                    "exhale.exe",
                    "m4a",
                    false,
                    "0 1 2 3 4 5 6 7 8 9 a b c d e f g",
                    "9",
                    "exhale.exe",
                    "%M %O"));

            // The official OptimFROG 5.100 CLI was exercised against this exact contract:
            // encode WAV stdin/file to .ofr, then decode the finalized file to WAV stdout. The
            // latter is the mandatory independent PCM verifier before publication.
            if (!HasEncoder("ofr", "ofr.exe"))
            {
                var optimFrog = new CUETools.Codecs.CommandLine.EncoderSettings(
                    "ofr.exe",
                    "ofr",
                    true,
                    "0 1 2 3 4 5 6 7 8 9 10 max",
                    "max",
                    "ofr.exe",
                    "--encode --silent --overwrite --preset %M --md5 %I --output %O");
                ConfigureSelfVerifier(
                    optimFrog,
                    "--decode --silent --writefreshheader %I --output -");
                AddEncoder(optimFrog);
            }
            AddFormat("ofr", CUEToolsTagger.APEv2, true, false, true);
            if (config.formats["ofr"].encoderLossless == null)
                config.formats["ofr"].encoderLossless =
                    config.Encoders.GetDefault("ofr", true);
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
    public bool IsUsable(AudioEncoderSettingsViewModel? enc) =>
        GetHealth(enc).IsReady;

    /// <summary>
    /// Performs only side-effect-free readiness work: resolve a command executable and verifier,
    /// or read a native wrapper's version property. It never opens a drive or creates output.
    /// </summary>
    public CodecHealth GetHealth(AudioEncoderSettingsViewModel? enc)
    {
        if (enc == null)
            return CodecHealth.SetupRequired("Not configured", "No encoder is selected.");
        if (enc.Settings is not CUETools.Codecs.CommandLine.EncoderSettings cli)
            return ProbeInProcessHealth(enc);

        if (ResolveExe(enc) == null)
            return CodecHealth.SetupRequired(
                "External command",
                "The encoder executable is not installed or did not pass its approval check.");
        if (!cli.Lossless || !cli.VerificationRequired)
            return CodecHealth.Ready(CommandOrigin(enc));
        if (!cli.HasLosslessVerifier)
            return CodecHealth.SetupRequired(
                CommandOrigin(enc),
                "This lossless command encoder needs an independent decode command.");
        if (!cli.VerificationUsesEncoder && ResolveVerificationExe(cli) == null)
            return CodecHealth.SetupRequired(
                CommandOrigin(enc),
                "The independent verification decoder is not available.");
        return CodecHealth.Ready(CommandOrigin(enc));
    }

    private CodecHealth ProbeInProcessHealth(AudioEncoderSettingsViewModel enc)
    {
        string typeName = enc.Settings.GetType().FullName ?? "";
        string origin = NativeEncoderSettingsTypes.Contains(typeName)
            ? "Bundled native"
            : typeName.StartsWith("CUETools.Codecs.WMA.", StringComparison.Ordinal)
                ? "Windows Media runtime"
                : "Built in";
        if (!NativeEncoderSettingsTypes.Contains(typeName))
            return CodecHealth.Ready(origin);

        try
        {
            PropertyInfo? versionProperty = enc.Settings.GetType().GetProperty(
                "Version",
                BindingFlags.Public | BindingFlags.Instance);
            string version = versionProperty?.GetValue(enc.Settings)?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(version))
                return CodecHealth.LoadFailed(
                    origin,
                    "The native library returned no version identity.");
            return CodecHealth.Ready(origin, version.Trim());
        }
        catch (Exception ex)
        {
            Exception failure = ex is TargetInvocationException { InnerException: not null }
                ? ex.InnerException
                : ex;
            _log.Warn(
                "encoders",
                "native codec readiness failed: " + typeName + " (" +
                failure.GetType().Name + ")");
            return CodecHealth.LoadFailed(
                origin,
                "The native library could not initialize (" +
                failure.GetType().Name + ").");
        }
    }

    private string CommandOrigin(AudioEncoderSettingsViewModel enc)
    {
        string path = enc.Path ?? "";
        try
        {
            if (path.Length > 0 && Path.IsPathRooted(path))
            {
                if (IsWithinManagedDirectory(path))
                    return "User import";
                if (IsWithinBundledDirectory(path))
                    return "Bundled command";
            }
        }
        catch { }
        return "External command";
    }

    public IReadOnlyList<CodecChoice> BuildChoices(CUEConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        var choices = new List<CodecChoice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AudioEncoderSettingsViewModel encoder in config.Encoders)
        {
            if (!config.formats.TryGetValue(encoder.Extension, out CUEToolsFormat? format))
                continue;
            if (encoder.Lossless ? !format.allowLossless : !format.allowLossy)
                continue;
            string stableId = encoder.Extension + "|" +
                (encoder.Lossless ? "lossless|" : "lossy|") + encoder.Name;
            if (!seen.Add(stableId))
                continue;

            CodecHealth health = GetHealth(encoder);
            (string name, string description, string history, string bestUse, string distribution) =
                Describe(encoder);
            choices.Add(new CodecChoice
            {
                StableId = stableId,
                Extension = encoder.Extension,
                FormatName = name,
                Implementation = ImplementationName(encoder),
                Lossless = encoder.Lossless,
                Encoder = encoder,
                Health = health,
                Description = description,
                History = history,
                BestUse = bestUse,
                Distribution = distribution,
                RecommendedRank = RecommendedRank(encoder),
                CompressionRank = CompressionRank(encoder),
                EfficiencyRank = EfficiencyRank(encoder),
            });
        }
        return choices
            .OrderBy(choice => choice.CategoryOrder)
            .ThenBy(choice => choice.RecommendedRank)
            .ThenBy(choice => choice.FormatName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Implementation, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public CodecChoice? GetSelectedChoice(CUEConfig config, string extension)
    {
        if (!config.formats.TryGetValue(extension, out CUEToolsFormat? format))
            return null;
        bool lossy = IsLossyFormat(format);
        AudioEncoderSettingsViewModel? selected = lossy
            ? format.encoderLossy
            : format.encoderLossless;
        IReadOnlyList<CodecChoice> choices = BuildChoices(config);
        return choices.FirstOrDefault(choice =>
            ReferenceEquals(choice.Encoder, selected));
    }

    public void SelectCodec(CUEConfig config, CodecChoice choice)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (choice == null)
            throw new ArgumentNullException(nameof(choice));
        CodecHealth health = GetHealth(choice.Encoder);
        if (!health.IsReady ||
            !config.formats.TryGetValue(choice.Extension, out CUEToolsFormat? format))
            throw new InvalidOperationException(
                "The selected codec is not ready on this machine.");
        if (choice.Lossless)
            format.encoderLossless = choice.Encoder;
        else
            format.encoderLossy = choice.Encoder;
        if (format.allowLossless && format.allowLossy)
            _app.SetFormatTypeOverride(choice.Extension, !choice.Lossless);
        _log.Info(
            "encoders",
            "selected " + choice.Extension + " / " + choice.Encoder.Name);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string ImplementationName(AudioEncoderSettingsViewModel encoder) =>
        encoder.Name switch
        {
            "cuetools" => "CUETools managed encoder",
            "libFLAC" => "Reference libFLAC",
            "libwavpack" => "WavPack library",
            "MACLib" => "Monkey's Audio SDK",
            "libmp3lame-VBR" => "LAME VBR",
            "libmp3lame-CBR" => "LAME CBR",
            "wma lossless" => "Windows Media runtime",
            "wma lossy" => "Windows Media runtime",
            _ => encoder.Name,
        };

    private static (string, string, string, string, string) Describe(
        AudioEncoderSettingsViewModel encoder)
    {
        foreach (var known in Known)
        {
            if (string.Equals(known.enc, encoder.Name, StringComparison.Ordinal) &&
                string.Equals(known.ext, encoder.Extension, StringComparison.OrdinalIgnoreCase))
                return (known.name, known.description, known.history, known.bestUse, known.distribution);
        }

        return encoder.Extension.ToLowerInvariant() switch
        {
            "flac" => ("FLAC", "Open lossless audio with strong integrity metadata and broad support.",
                "FLAC has been the dominant open lossless music format since 2001.",
                "Primary archival copies and interoperable lossless libraries.",
                "Bundled in-process implementation."),
            "wv" => ("WavPack", "Open lossless audio with strong compression and optional hybrid modes.",
                "WavPack has been developed since the late 1990s and remains actively maintained.",
                "Lossless archives that value compression and flexible metadata.",
                "Bundled source-built native implementation."),
            "ape" => ("Monkey's Audio", "Lossless audio focused on strong compression on Windows.",
                "Monkey's Audio has been available since 2000 and uses the APE container.",
                "Windows-centric archives where decode support and speed tradeoffs are acceptable.",
                "Bundled from the official SDK with notices."),
            "m4a" when encoder.Lossless => ("Apple Lossless (ALAC)",
                "Lossless audio in the MPEG-4 container with broad Apple ecosystem support.",
                "Apple released ALAC as open source in 2011 after introducing it in 2004.",
                "Lossless libraries centered on Apple hardware and M4A metadata.",
                "Bundled managed implementation."),
            "mp3" => ("MP3", "Mature perceptual audio with nearly universal playback support.",
                "MPEG Layer III became the dominant portable music format in the 1990s.",
                "Compatibility copies; keep a lossless master for preservation.",
                "Bundled LAME native implementation."),
            "wma" when encoder.Lossless => ("WMA Lossless",
                "Microsoft's lossless Windows Media Audio profile.",
                "WMA Lossless is part of the Windows Media codec family introduced in the 2000s.",
                "Windows-specific lossless compatibility; FLAC is more portable.",
                "Uses the installed Windows Media runtime."),
            "wma" => ("Windows Media Audio", "Microsoft's perceptual Windows Media Audio profile.",
                "WMA was introduced in 1999 for Windows media playback and streaming.",
                "Legacy Windows playback compatibility.",
                "Uses the installed Windows Media runtime."),
            "wav" => ("WAV (PCM)", "Uncompressed PCM audio in a RIFF/WAVE container.",
                "RIFF/WAVE has been a standard interchange format since the early 1990s.",
                "Editing, interchange, and maximum simplicity; files are much larger.",
                "Built-in implementation."),
            "tta" => ("True Audio (TTA)", "Open lossless audio with fast, simple decoding.",
                "TTA originated in the early 2000s as a compact lossless codec.",
                "Specialized lossless archives where player support is known.",
                "Source-built plugin when present."),
            _ => (encoder.Extension.ToUpperInvariant(),
                "Audio encoder registered with CUETools.",
                "See the encoder project's documentation for its format history.",
                encoder.Lossless ? "Lossless archival output." : "Lossy compatibility output.",
                "Availability and origin are shown for this installation."),
        };
    }

    private static int RecommendedRank(AudioEncoderSettingsViewModel encoder) =>
        encoder.Extension.ToLowerInvariant() switch
        {
            "flac" => encoder.Name == "cuetools" ? 0 : 1,
            "wv" => 10,
            "m4a" when encoder.Lossless => 20,
            "tak" => 30,
            "ape" => 40,
            "tta" => 50,
            "ofr" => 60,
            "wav" => 90,
            "opus" => 100,
            "m4a" => 110,
            "ogg" => 120,
            "mpc" => 130,
            "mp3" => 140,
            "wma" => 150,
            _ => 500,
        };

    private static int CompressionRank(AudioEncoderSettingsViewModel encoder) =>
        encoder.Extension.ToLowerInvariant() switch
        {
            "ofr" => 0,
            "ape" => 10,
            "tak" => 20,
            "wv" => 30,
            "flac" => 40,
            "m4a" when encoder.Lossless => 45,
            "tta" => 50,
            "wav" => 100,
            _ => 500,
        };

    private static int EfficiencyRank(AudioEncoderSettingsViewModel encoder) =>
        encoder.Extension.ToLowerInvariant() switch
        {
            "opus" => 0,
            "m4a" when encoder.Name == "exhale.exe" => 5,
            "m4a" => 10,
            "ogg" => 20,
            "mpc" => 25,
            "mp3" => 40,
            "wma" => 50,
            _ => 500,
        };

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

    /// <summary>Every actually runnable encoder for one format face. This is the source for the
    /// encoder picker: missing command-line programs never appear as selectable promises.</summary>
    public List<AudioEncoderSettingsViewModel> UsableEncoders(
        CUEConfig config,
        string extension,
        bool lossless)
    {
        var result = new List<AudioEncoderSettingsViewModel>();
        foreach (AudioEncoderSettingsViewModel encoder in config.Encoders)
            if (string.Equals(
                    encoder.Extension,
                    extension,
                    StringComparison.OrdinalIgnoreCase) &&
                encoder.Lossless == lossless &&
                IsUsable(encoder))
                result.Add(encoder);
        return result
            .OrderByDescending(encoder => encoder.Settings.Priority)
            .ThenBy(encoder => encoder.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Select one concrete implementation for a format face. Selection mutates the
    /// persisted format model; it never rewrites the executable path or silently changes the
    /// chosen encoder when another implementation later becomes available.</summary>
    public void SetFormatEncoder(
        CUEConfig config,
        string extension,
        bool lossless,
        AudioEncoderSettingsViewModel encoder)
    {
        if (!config.formats.TryGetValue(extension, out CUEToolsFormat? format) ||
            encoder == null ||
            !string.Equals(
                encoder.Extension,
                extension,
                StringComparison.OrdinalIgnoreCase) ||
            encoder.Lossless != lossless ||
            !config.Encoders.Contains(encoder) ||
            !IsUsable(encoder))
            throw new InvalidOperationException(
                "The selected encoder is not usable for this format.");

        if (lossless)
            format.encoderLossless = encoder;
        else
            format.encoderLossy = encoder;
        _log.Info(
            "encoders",
            $"format {extension}: encoder set to {encoder.Name}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Find a command-line encoder's exe. Files copied into the app-managed encoders directory
    /// require an exact import receipt match; files in the packaged encoders directory require an
    /// exact release hash. An explicitly configured absolute path outside those directories, an
    /// app-adjacent tool, and PATH remain user-managed compatibility paths; the app makes no
    /// publisher or origin claim for them.
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
                if (IsWithinBundledDirectory(absolute) &&
                    !ValidatePackagedEncoder(absolute, enc))
                    return null;
                enc.Path = absolute;
                return absolute;
            }

            string name = exe.Length > 0 ? Path.GetFileName(exe) : enc.Name.Split(' ')[0];
            if (!IsSimpleExecutableName(name))
                return null;
            string[] candidateNames = new[] { name }
                .Concat(AcceptedNames(enc.Name, name))
                .Where(IsSimpleExecutableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string candidateName in candidateNames)
            {
                string local = Path.Combine(_encodersDir, candidateName);
                if (!File.Exists(local))
                    continue;
                // A present but unapproved/changed managed file is a hard refusal. Falling through
                // to PATH here could silently run a different executable than the user imported.
                if (!ValidateManagedEncoder(local, enc))
                    return null;
                enc.Path = local;
                return local;
            }

            // Packaged command-line tools live in an explicit subfolder so they do not mingle with
            // host binaries. A hash-bound user import above always wins, allowing future codec
            // updates without waiting for an application release.
            foreach (string candidateName in candidateNames)
            {
                string bundled = Path.GetFullPath(
                    Path.Combine(_bundledEncodersDir, candidateName));
                if (!File.Exists(bundled))
                    continue;
                if (!ValidatePackagedEncoder(bundled, enc))
                    return null;
                enc.Path = bundled;
                return bundled;
            }

            foreach (string candidateName in candidateNames)
            {
                string beside = Path.GetFullPath(
                    Path.Combine(AppContext.BaseDirectory, candidateName));
                if (!File.Exists(beside))
                    continue;
                enc.Path = beside;
                return beside;
            }
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (string candidateName in candidateNames)
                {
                    try
                    {
                        string p = Path.GetFullPath(
                            Path.Combine(
                                dir.Trim().Trim('"'),
                                candidateName));
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
            if (enc != null)
            {
                enc.Path = dest;
                if (config.formats.TryGetValue(info.Extension, out CUEToolsFormat? format))
                {
                    AudioEncoderSettingsViewModel? selected = info.Lossless
                        ? format.encoderLossless
                        : format.encoderLossy;
                    if (!IsUsable(selected))
                    {
                        if (info.Lossless)
                            format.encoderLossless = enc;
                        else
                            format.encoderLossy = enc;
                    }
                }
            }
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

    private bool ValidatePackagedEncoder(
        string path,
        AudioEncoderSettingsViewModel enc)
    {
        string fileName = Path.GetFileName(path);
        string reason;
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!IsDirectChild(_bundledEncodersDir, fullPath))
                reason = "location";
            else if (!_packagedEncoderHashes.TryGetValue(
                         fileName,
                         out string? expectedSha256))
                reason = "unlisted";
            else
            {
                FileIdentity identity = ReadIdentity(fullPath);
                if (!string.Equals(
                        identity.Sha256,
                        expectedSha256,
                        StringComparison.OrdinalIgnoreCase))
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

        _log.Warn("encoders", $"packaged encoder {fileName} refused ({reason})");
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
        return IsWithinDirectory(path, _encodersDir);
    }

    private bool IsWithinBundledDirectory(string path)
    {
        return IsWithinDirectory(path, _bundledEncodersDir);
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        string root = directory.TrimEnd(
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
        foreach (var (
            enc,
            ext,
            name,
            lossless,
            exe,
            url,
            description,
            history,
            bestUse,
            distribution) in Known)
        {
            var info = new ExternalEncoderInfo
            {
                EncoderName = enc,
                Extension = ext,
                FormatName = name,
                Lossless = lossless,
                ExeName = exe,
                AcceptedExeNames = AcceptedNames(enc, exe),
                DownloadUrl = url,
                Description = description,
                History = history,
                BestUse = bestUse,
                DistributionNote = distribution,
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
            ["wv|libwavpack"] = "high+",        // WavPack: strongest standard mode
            ["ape|MACLib"] = "insane",          // Monkey's Audio: strongest mode
            ["ogg|oggenc.exe"] = "8",           // Vorbis: high-quality archival derivative
            ["opus|opusenc.exe"] = "256",       // Opus: archival derivative with extra signal margin
            ["m4a|qaac.exe (tvbr)"] = "127",    // qaac: strongest TVBR quality
            ["m4a|exhale.exe"] = "9",           // xHE-AAC: strongest conservative public preset
            ["ofr|ofr.exe"] = "max",            // OptimFROG: strongest preset
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
                // Archival default: raise extra-processing effort to the codec's
                // maximum through the typed seam (WavPack -x6 historically); the
                // shared core carries no per-codec assembly references.
                if (e.Settings is IExtraModeSettings extra)
                    extra.ExtraMode = extra.MaxExtraMode;
                _log.Info("encoders", $"archival default applied: {e.Name} -> {mode}");
            }
        }
        catch (Exception ex) { _log.Warn("encoders", "archival defaults failed: " + ex.GetType().Name); }
    }
}
