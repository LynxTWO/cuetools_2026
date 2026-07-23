using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

    public bool BoolValue
    {
        get => _target != null && _prop.PropertyType == typeof(bool) && (bool)(_prop.GetValue(_target) ?? false);
        set { try { _prop.SetValue(_target, value); OnPropertyChanged(); } catch { } }
    }

    public string TextValue
    {
        get { try { return Convert.ToString(_prop.GetValue(_target), System.Globalization.CultureInfo.InvariantCulture) ?? ""; } catch { return ""; } }
        set
        {
            try
            {
                object v = _prop.PropertyType.IsEnum
                    ? Enum.Parse(_prop.PropertyType, value)
                    : Convert.ChangeType(value, _prop.PropertyType, System.Globalization.CultureInfo.InvariantCulture);
                _prop.SetValue(_target, v);
            }
            catch { /* invalid input: keep the old value; the getter re-shows it */ }
            OnPropertyChanged();
        }
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
    public ObservableCollection<string> Modes { get; } = new();
    public ObservableCollection<EncoderSettingRow> Advanced { get; } = new();
    public string ModeHint { get; }
    public bool HasModes => Modes.Count > 0;
    public bool HasAdvanced => Advanced.Count > 0;

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

    public EncoderSettingsViewModel(CUEConfig config, string format, bool lossy)
    {
        var f = config.formats[format];
        _enc = (lossy ? f.encoderLossy : f.encoderLossless)
            ?? throw new InvalidOperationException("no encoder for " + format);
        var s = _enc.Settings;

        bool cli = s is CUETools.Codecs.CommandLine.EncoderSettings;
        Title = $"{format.ToUpperInvariant()} encoder - {_enc.Name}";
        Subtitle = cli
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
        var skip = new HashSet<string> { "EncoderMode", "PCM", "BlockSize", "Padding" };
        foreach (PropertyDescriptor p in TypeDescriptor.GetProperties(s))
        {
            if (!p.IsBrowsable || p.IsReadOnly || skip.Contains(p.Name)) continue;
            string tip = !string.IsNullOrWhiteSpace(p.Description) ? p.Description : CuratedTip(p.Name);
            Advanced.Add(new EncoderSettingRow(s, p, tip));
        }
    }

    // Plain-English mode explanations, including WHY the default is what it is (the owner's
    // archival policy: maximum compression for lossless, efficiency-leaning-archival for lossy).
    private static string ModeHintFor(string format, string encoderName) => format switch
    {
        "flac" => "Compression level 0 (fastest, largest) to 8 (maximum subset compression). FLAC is " +
                  "lossless at every level and all levels decode equally fast - higher levels only cost " +
                  "encode time. Default 8: maximum archival compression.",
        "m4a" => "ALAC compression effort 0 to 10. Lossless at every level; higher levels shrink the " +
                 "file at the cost of encode time. Default 10: maximum archival compression.",
        "mp3" => "LAME VBR quality from V9 (smallest) to V0 (best). Default V0 (~245 kbps): the top " +
                 "VBR quality, the archival-leaning lossy choice. V2 (~190 kbps) is the classic " +
                 "transparency sweet spot if you want smaller files.",
        "wma" => "WMA VBR quality from 10 to 98. Default 90: high quality at an efficient size; 98 is " +
                 "the maximum-quality VBR mode if size does not matter.",
        "mpc" => "Musepack quality 0 to 10 (5 = the classic 'standard' ~170 kbps). Default 7: leans " +
                 "archival (~250 kbps). Musepack's VBR is tuned for transparency at mid-high bitrates.",
        "ofr" => "OptimFROG preset 0 to 10. Lossless at every preset; higher presets compress harder " +
                 "and encode slower. Default 10: maximum archival compression.",
        "tak" => "TAK preset 0 to 4; 'e' and 'm' variants use extra/maximum evaluation for a little " +
                 "more compression at slower speed. Default 4m: the strongest setting.",
        "ogg" => "Ogg Vorbis quality -1 to 8; q6 is roughly 192 kbps.",
        "opus" => "Opus bitrate in kbps; 128 is transparent for most material, 192 adds margin.",
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
        "Lossless" => "Whether this external encoder produces lossless output. Affects verification " +
                      "and which list the format appears in.",
        _ => "Encoder-specific setting. The codec's documentation describes its effect."
    };
}
