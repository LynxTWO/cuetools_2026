using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CUETools.Codecs;
using CUETools.Processor;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>One encoder setting, discovered by reflection (TypeDescriptor, so SRDescription
/// resource descriptions resolve) and written straight through to the live settings object.</summary>
public sealed class EncoderSettingRow : ViewModelBase
{
    private readonly object _target;
    private readonly PropertyDescriptor _prop;

    public EncoderSettingRow(object target, PropertyDescriptor prop, string tooltip)
    {
        _target = target;
        _prop = prop;
        Tooltip = tooltip;
        if (_prop.PropertyType.IsEnum) EnumValues = Enum.GetNames(_prop.PropertyType);
    }

    public string Name => _prop.DisplayName;
    public string Tooltip { get; }
    public bool IsBool => _prop.PropertyType == typeof(bool);
    public bool IsEnum => _prop.PropertyType.IsEnum;
    public bool IsText => !IsBool && !IsEnum;
    public string[] EnumValues { get; } = Array.Empty<string>();
    public string ValidationError { get; private set; } = "";
    public bool HasValidationError => ValidationError.Length != 0;

    internal static bool Supports(PropertyDescriptor property)
    {
        Type type = property.PropertyType;
        return type == typeof(bool) || type == typeof(string) || type.IsEnum ||
               type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    public bool BoolValue
    {
        get => _target != null && _prop.PropertyType == typeof(bool) && (bool)(_prop.GetValue(_target) ?? false);
        set
        {
            try
            {
                _prop.SetValue(_target, value);
                SetValidationError("");
            }
            catch
            {
                SetValidationError("This encoder rejected the value.");
            }
            OnPropertyChanged();
        }
    }

    public string TextValue
    {
        get { try { return Convert.ToString(_prop.GetValue(_target), System.Globalization.CultureInfo.InvariantCulture) ?? ""; } catch { return ""; } }
        set
        {
            object? converted;
            if (!TryConvert(value, _prop.PropertyType, out converted))
            {
                SetValidationError("Enter a valid " + FriendlyTypeName(_prop.PropertyType) + " value.");
                OnPropertyChanged();
                return;
            }

            try
            {
                _prop.SetValue(_target, converted);
                SetValidationError("");
            }
            catch
            {
                SetValidationError("This encoder rejected the value.");
            }
            OnPropertyChanged();
        }
    }

    private void SetValidationError(string message)
    {
        if (ValidationError == message)
            return;
        ValidationError = message;
        OnPropertyChanged(nameof(ValidationError));
        OnPropertyChanged(nameof(HasValidationError));
    }

    private static bool TryConvert(string value, Type type, out object? converted)
    {
        converted = null;
        if (type == typeof(string)) { converted = value; return true; }
        if (type.IsEnum)
        {
            object? parsed;
            if (!Enum.TryParse(type, value, false, out parsed) || parsed == null || !Enum.IsDefined(type, parsed))
                return false;
            converted = parsed;
            return true;
        }

        NumberStyles integer = NumberStyles.Integer;
        NumberStyles floating = NumberStyles.Float;
        CultureInfo invariant = CultureInfo.InvariantCulture;
        byte byteValue; if (type == typeof(byte) && byte.TryParse(value, integer, invariant, out byteValue)) { converted = byteValue; return true; }
        sbyte sbyteValue; if (type == typeof(sbyte) && sbyte.TryParse(value, integer, invariant, out sbyteValue)) { converted = sbyteValue; return true; }
        short shortValue; if (type == typeof(short) && short.TryParse(value, integer, invariant, out shortValue)) { converted = shortValue; return true; }
        ushort ushortValue; if (type == typeof(ushort) && ushort.TryParse(value, integer, invariant, out ushortValue)) { converted = ushortValue; return true; }
        int intValue; if (type == typeof(int) && int.TryParse(value, integer, invariant, out intValue)) { converted = intValue; return true; }
        uint uintValue; if (type == typeof(uint) && uint.TryParse(value, integer, invariant, out uintValue)) { converted = uintValue; return true; }
        long longValue; if (type == typeof(long) && long.TryParse(value, integer, invariant, out longValue)) { converted = longValue; return true; }
        ulong ulongValue; if (type == typeof(ulong) && ulong.TryParse(value, integer, invariant, out ulongValue)) { converted = ulongValue; return true; }
        float floatValue;
        if (type == typeof(float) && float.TryParse(value, floating, invariant, out floatValue) &&
            !float.IsNaN(floatValue) && !float.IsInfinity(floatValue)) { converted = floatValue; return true; }
        double doubleValue;
        if (type == typeof(double) && double.TryParse(value, floating, invariant, out doubleValue) &&
            !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue)) { converted = doubleValue; return true; }
        decimal decimalValue; if (type == typeof(decimal) && decimal.TryParse(value, floating, invariant, out decimalValue)) { converted = decimalValue; return true; }
        return false;
    }

    private static string FriendlyTypeName(Type type)
    {
        if (type.IsEnum)
            return "listed";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "number";
        if (type == typeof(string))
            return "text";
        return "whole-number";
    }
}

/// <summary>
/// Settings for ONE encoder, built by reflection so every codec's real knobs appear without
/// per-codec UI code. The COMMON setting is the compression/quality mode (with a per-codec plain
/// English explanation and the archival-defaults note); ADVANCED is every property the encoder
/// marks browsable, each with a hover explanation (the codec's own Description resources when it
/// has them, a curated explanation otherwise). Everything applies immediately to the live encoder
/// object and persists with the app settings on exit.
/// </summary>
public sealed class EncoderSettingsViewModel : ViewModelBase
{
    private readonly AudioEncoderSettingsViewModel _enc;

    public string Title { get; }
    public string Subtitle { get; }
    public ObservableCollection<AudioEncoderSettingsViewModel> Encoders { get; } = new();
    public ObservableCollection<string> Modes { get; } = new();
    public ObservableCollection<EncoderSettingRow> Advanced { get; } = new();
    public string ModeHint { get; }
    public bool HasEncoderChoice => Encoders.Count > 1;
    public bool HasModes => Modes.Count > 0;
    public bool HasAdvanced => Advanced.Count > 0;
    public event Action<AudioEncoderSettingsViewModel>? EncoderChanged;

    // The lossless/lossy TYPE picker for two-faced formats (wma: WMA Lossless vs Standard; m4a:
    // ALAC vs an imported AAC encoder). Populated by the window's Open path; choosing the other
    // type raises TypeChanged so the dialog rebuilds around the other encoder.
    public bool HasTypeChoice { get; set; }
    public bool IsLossyType { get; set; }
    public event Action<bool>? TypeChanged;
    public bool TypeLossless { get => !IsLossyType; set { if (value && IsLossyType) TypeChanged?.Invoke(false); } }
    public bool TypeLossy { get => IsLossyType; set { if (value && !IsLossyType) TypeChanged?.Invoke(true); } }

    public string SelectedMode
    {
        get => _enc.Settings.EncoderMode ?? "";
        set { try { _enc.Settings.EncoderMode = value; } catch { } OnPropertyChanged(); }
    }

    public AudioEncoderSettingsViewModel SelectedEncoder
    {
        get => _enc;
        set
        {
            if (value != null && !ReferenceEquals(value, _enc))
                EncoderChanged?.Invoke(value);
        }
    }

    public EncoderSettingsViewModel(
        CUEConfig config,
        Services.EncoderCatalog catalog,
        string format,
        bool lossy)
    {
        var f = config.formats[format];
        _enc = (lossy ? f.encoderLossy : f.encoderLossless)
            ?? throw new InvalidOperationException("no encoder for " + format);
        foreach (AudioEncoderSettingsViewModel encoder in
            catalog.UsableEncoders(config, format, !lossy))
            Encoders.Add(encoder);
        var s = _enc.Settings;

        var cliSettings =
            s as CUETools.Codecs.CommandLine.EncoderSettings;
        bool cli = cliSettings != null;
        Title = $"{format.ToUpperInvariant()} encoder - {_enc.Name}";
        Subtitle = cliSettings?.UsesLegacyUnverifiedCompatibility == true
            ? "Legacy external lossless encoder retained for compatibility. Its output is not "
                + "independently verified. Configure exactly one verification decoder and a "
                + "%I decode command below to enable verified publication."
            : cli
            ? "External command-line encoder. The advanced settings below include the program path and its argument template."
            : "Built-in encoder (runs in-process). Changes apply immediately and are saved when the app closes.";

        // modes are PCM-dependent for some codecs (WMA); this app encodes CD audio
        try { s.PCM = AudioPCMConfig.RedBook; } catch { }
        foreach (var m in (s.SupportedModes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)) Modes.Add(m);
        ModeHint = ModeHintFor(format, _enc.Name);
        // a stored mode can be stale (WMA names shorten once PCM is known) - snap to a valid one
        if (Modes.Count > 0 && !Modes.Contains(SelectedMode))
        {
            string cur = SelectedMode;
            string match = Modes.FirstOrDefault(m => cur.StartsWith(m + ",")) ?? Modes[Modes.Count - 1];
            SelectedMode = match;
        }

        // every browsable property is exposed; the plumbing ones the encoder hides stay hidden
        var skip = new HashSet<string>
        {
            "EncoderMode", "PCM", "BlockSize", "Padding", "Lossless"
        };
        if (cliSettings is { Lossless: false })
        {
            skip.Add("VerificationUsesEncoder");
            skip.Add("VerificationPath");
            skip.Add("VerificationParameters");
        }
        foreach (PropertyDescriptor p in TypeDescriptor.GetProperties(s))
        {
            if (!p.IsBrowsable || p.IsReadOnly || skip.Contains(p.Name) || !EncoderSettingRow.Supports(p)) continue;
            string tip = !string.IsNullOrWhiteSpace(p.Description) ? p.Description : CuratedTip(p.Name);
            Advanced.Add(new EncoderSettingRow(s, p, tip));
        }
    }

    // Plain-English mode explanations, including WHY the default is what it is (the owner's
    // archival policy: maximum compression for lossless, efficiency-leaning-archival for lossy).
    private static string ModeHintFor(string format, string encoderName) =>
        (format, encoderName) switch
    {
        ("flac", _) => "Compression level 0 (fastest, largest) to 8 (maximum subset compression). FLAC is " +
                  "lossless at every level and all levels decode equally fast - higher levels only cost " +
                  "encode time. Default 8: maximum archival compression.",
        ("m4a", "qaac.exe (tvbr)") => "qaac true-VBR quality 10 to 127. Default 127: the strongest " +
                 "archival-leaning AAC setting. It is still lossy and requires Apple's separately " +
                 "installed CoreAudio components.",
        ("m4a", "exhale.exe") => "exhale xHE-AAC modes 0 to 9 are the conservative public presets; " +
                 "a to g expose higher expert modes. Default 9 favors preservation-quality lossy " +
                 "output without silently opting into expert behavior.",
        ("m4a", _) => "ALAC compression effort 0 to 10. Lossless at every level; higher levels shrink the " +
                 "file at the cost of encode time. Default 10: maximum archival compression.",
        ("mp3", _) => "LAME VBR quality from V9 (smallest) to V0 (best). Default V0 (~245 kbps): the top " +
                 "VBR quality, the archival-leaning lossy choice. V2 (~190 kbps) is the classic " +
                 "transparency sweet spot if you want smaller files.",
        ("wma", _) => "WMA VBR quality from 10 to 98. Default 90: high quality at an efficient size; 98 is " +
                 "the maximum-quality VBR mode if size does not matter.",
        ("mpc", _) => "Musepack quality 0 to 10 (5 = the classic 'standard' ~170 kbps). Default 7: leans " +
                 "archival (~250 kbps). Musepack's VBR is tuned for transparency at mid-high bitrates.",
        ("ofr", _) => "OptimFROG presets 0 to 10 and max are lossless; stronger presets compress " +
                 "harder and run much slower. Default max: maximum archival compression. CUETools " +
                 "decodes the finalized file and compares its PCM before publishing it.",
        ("tak", _) => "TAK preset 0 to 4; 'e' and 'm' variants use extra/maximum evaluation for a little " +
                 "more compression at slower speed. Default 4m: the strongest setting.",
        ("ogg", _) => "Ogg Vorbis quality -1 to 8; q6 is roughly 192 kbps. Default q8: a " +
                 "high-quality, archival-leaning derivative.",
        ("opus", _) => "Opus bitrate in kbps. Default 256: an archival-leaning derivative with " +
                  "extra signal margin; 128 is the usual efficiency sweet spot.",
        ("wv", _) => "WavPack fast through high+ changes only compression effort. Default high+ " +
                 "with ExtraMode 6: maximum archival compression while remaining bit-exact.",
        ("ape", _) => "Monkey's Audio fast through insane changes only compression effort. " +
                 "Default insane: maximum archival compression; all modes are bit-exact.",
        _ => "The encoder's compression or quality mode."
    };

    // Fallback hover text for settings whose codec ships no Description resource.
    private static string CuratedTip(string propName) => propName switch
    {
        "Quality" => "The encoder's internal quality/speed trade-off (LAME -q). High is the sane " +
                     "default; Highest costs extra time for a negligible gain.",
        "AllowNonSubset" => "Allow non-subset FLAC (levels 9-11): compresses slightly harder, but " +
                            "some hardware players only accept subset files. Leave off for maximum " +
                            "compatibility.",
        "Path" => "Full path to the encoder program (.exe). Use the Settings page's Encoders section " +
                  "to download and import it.",
        "Parameters" => "The argument template used to run the program. %M = the selected mode, " +
                        "%O = the output file, %P = padding. Change only if you know the encoder's " +
                        "command line.",
        "Lossless" => "Whether this external encoder produces lossless output. New lossless " +
                      "configurations require independent decode verification. Pre-contract " +
                      "profiles remain explicitly unverified until a decoder is configured.",
        _ => "Encoder-specific setting. The codec's documentation describes its effect."
    };
}
