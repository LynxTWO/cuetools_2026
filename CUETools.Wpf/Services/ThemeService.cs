using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Services;

public enum AppTheme { Dark, Light }

/// <summary>
/// Live light/dark theming by swapping a whole palette ResourceDictionary in and out of the
/// owner's MergedDictionaries. This is the one mechanism that reliably re-renders a LIVE window:
/// adding/removing a merged dictionary raises the resource-change notifications that every
/// DynamicResource consumer listens for, so the surfaces and text repaint immediately.
///
/// Two approaches were tried and rejected first:
///  - Replacing Application.Resources["Ground"] etc. with new brushes: live DynamicResource
///    consumers deep in the tree did not re-resolve (worked only under RenderTargetBitmap, which
///    forces a full re-render and masked the bug).
///  - Mutating a single shared SolidColorBrush.Color in place: WPF FREEZES a resource brush once
///    the visual tree renders with it (confirmed: existing.IsFrozen==True on the second Apply), so
///    the mutation silently failed and fell back to a replace.
///
/// The themeable structural and custom-control palette is intentionally NOT in Theme.xaml - if it were, Theme.xaml
/// would be an always-present competing source for the same keys and the swapped-in dictionary
/// could not win. Decorative accents (Teal/Amber/Good) stay stable; semantic status text and the
/// switch housing, channel, thumb, border, and shadow colors come from this palette so controls
/// remain legible in both modes.
/// </summary>
public sealed class ThemeService
{
    // Marker key stamped into every palette dictionary we build, so the swap can find and remove
    // the previous palette without holding a reference (also used by the render harness).
    private const string Marker = "__ThemePalette__";

    private readonly string _prefPath;
    private readonly IDiagnosticLog? _log;

    public AppTheme Current { get; private set; } = AppTheme.Dark;
    public event EventHandler? Changed;

    public ThemeService(IDiagnosticLog? log = null)
    {
        _log = log;
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CUETools2026");
        _prefPath = System.IO.Path.Combine(dir, "theme.txt");
        try { if (System.IO.File.ReadAllText(_prefPath).Trim() == "Light") Current = AppTheme.Light; }
        catch { /* no saved pref - stay dark */ }
    }

    public void Apply(AppTheme theme)
    {
        if (Application.Current != null) Swap(Application.Current.Resources, theme);
        Current = theme;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_prefPath)!);
            System.IO.File.WriteAllText(_prefPath, theme.ToString());
        }
        catch (Exception ex) { _log?.Warn("theme", "theme preference not saved: " + ex.GetType().Name); }
        _log?.Info("theme", $"apply {theme}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    /// <summary>Swap the palette dictionary on <paramref name="owner"/>: remove any palette we
    /// previously merged, then merge a fresh one for <paramref name="theme"/>. Used for
    /// Application.Resources (the app) and for the render harness's root dictionary.</summary>
    public static void Swap(ResourceDictionary owner, AppTheme theme)
    {
        var merged = owner.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
            if (merged[i].Contains(Marker)) merged.RemoveAt(i);
        merged.Add(BuildPalette(theme));
    }

    /// <summary>Back-compat alias for the render harnesses that call Apply(dict, theme).</summary>
    public static void Apply(ResourceDictionary owner, AppTheme theme) => Swap(owner, theme);

    private static ResourceDictionary BuildPalette(AppTheme theme)
    {
        var p = theme == AppTheme.Light ? Light : Dark;
        var d = new ResourceDictionary { [Marker] = theme.ToString() };
        foreach (var kv in p)
        {
            if (kv.Key is "ButtonFaceTop" or "ButtonFaceBot") continue;
            d[kv.Key] = kv.Key.EndsWith("Color", StringComparison.Ordinal)
                ? C(kv.Value)
                : new SolidColorBrush(C(kv.Value));
        }
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(C(p["ButtonFaceTop"]), 0));
        g.GradientStops.Add(new GradientStop(C(p["ButtonFaceBot"]), 1));
        d["ButtonFace"] = g;
        return d;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static readonly Dictionary<string, string> Dark = new()
    {
        ["Ground"] = "#0C0F0D", ["Bar"] = "#0E1310", ["Face"] = "#161C16", ["Panel"] = "#141A16",
        ["StripEtched"] = "#4A554B", ["StripLit"] = "#C9FBF4", ["StripGlowColor"] = "#34CFC0",
        ["Line"] = "#28312A", ["Ink"] = "#EDF1E9", ["InkDim"] = "#B1BCAE", ["Muted"] = "#7D887C",
        ["StatusAccent"] = "#34CFC0", ["StatusGood"] = "#5CCB8B",
        ["StatusWarning"] = "#E9A63F", ["StatusDanger"] = "#E06C75",
        ["ScrollTrack"] = "#111713", ["ScrollTrackBorder"] = "#2B3931",
        ["ScrollThumb"] = "#50675F", ["ScrollThumbHover"] = "#34CFC0",
        ["ScrollThumbPressed"] = "#78E9DE",
        ["Glass"] = "#0E1311", ["GlassLine"] = "#243029", ["ButtonPressed"] = "#0C110E",
        ["ButtonFaceTop"] = "#1B221C", ["ButtonFaceBot"] = "#121813",
        ["ControlBorder"] = "#42FFFFFF", ["ButtonEdge"] = "#0A0E0B",
        // The machined key. KeySeam is the housing lamp seen through the gap around the cap;
        // Shoulder/Crown/Lip are the three layers that make the cap read as a domed solid rather
        // than a flat panel. KeyStandby is a dead key's legend at standby current: deliberately
        // 3.2:1 to 3.8:1, the floor of its own contract, because WCAG 1.4.3 exempts inactive
        // controls and the structural Line it replaced measured 1.2:1 on a key face.
        ["KeyStandby"] = "#42786F", ["KeySeamColor"] = "#F0A24A",
        ["KeyStandbyGlowColor"] = "#34CFC0",
        ["KeyShoulderMidColor"] = "#2E000000", ["KeyShoulderEdgeColor"] = "#5A000000",
        ["KeyCrownCoreColor"] = "#24FFFFFF", ["KeyCrownMidColor"] = "#12FFFFFF",
        ["KeyLipTopColor"] = "#26FFFFFF", ["KeyLipBottomColor"] = "#59000000",
        ["AccentKeyTopColor"] = "#3BD8C9", ["AccentKeyBottomColor"] = "#27A99C",
        ["AccentKeyText"] = "#0C0F0D",
        ["ControlShadowColor"] = "#000000", ["SwitchHousingBorder"] = "#0A0F0B",
        ["SwitchHousingTopColor"] = "#0A0E0B", ["SwitchHousingBottomColor"] = "#131A14",
        ["SwitchChannelTopColor"] = "#B0000000", ["SwitchChannelMidColor"] = "#40000000",
        ["SwitchChannelBottomColor"] = "#2AFFFFFF", ["SwitchThumbTopColor"] = "#5A6356",
        ["SwitchThumbMidColor"] = "#333C34", ["SwitchThumbBottomColor"] = "#242B25",
        ["SwitchSheen"] = "#26FFFFFF",
        ["DiscData"] = "#93A39F", ["DiscHub"] = "#BCC8C4",
        ["DiscEdge"] = "#DDE6E2", ["DiscBack"] = "#303A36",
        ["DiscTrack"] = "#E5F3EE",
    };

    private static readonly Dictionary<string, string> Light = new()
    {
        ["Ground"] = "#E7ECE2", ["Bar"] = "#DEE4D8", ["Face"] = "#F4F7EF", ["Panel"] = "#F1F5EB",
        ["StripEtched"] = "#8A968B", ["StripLit"] = "#0A8A7F", ["StripGlowColor"] = "#087067",
        ["Line"] = "#CAD2C2", ["Ink"] = "#1A211B", ["InkDim"] = "#414A40", ["Muted"] = "#6C766A",
        ["StatusAccent"] = "#087067", ["StatusGood"] = "#246D3C",
        ["StatusWarning"] = "#835600", ["StatusDanger"] = "#A83442",
        ["ScrollTrack"] = "#D7DED1", ["ScrollTrackBorder"] = "#B7C2B2",
        ["ScrollThumb"] = "#71837B", ["ScrollThumbHover"] = "#087067",
        ["ScrollThumbPressed"] = "#07544E",
        ["Glass"] = "#E4E9DD", ["GlassLine"] = "#CAD2C2", ["ButtonPressed"] = "#D6DDCC",
        ["ButtonFaceTop"] = "#FBFDF7", ["ButtonFaceBot"] = "#E9EEE1",
        ["ControlBorder"] = "#6676816F", ["ButtonEdge"] = "#B8C2B2",
        // Machined-key tokens, light. The seam lamp is warmer and darker here because it is read
        // against a bright console rather than a dark one, and the crown/lip highlights are
        // stronger because a light cap catches more of the bench light.
        ["KeyStandby"] = "#577F79", ["KeySeamColor"] = "#C9762A",
        ["KeyStandbyGlowColor"] = "#087067",
        ["KeyShoulderMidColor"] = "#1A000000", ["KeyShoulderEdgeColor"] = "#33000000",
        ["KeyCrownCoreColor"] = "#34FFFFFF", ["KeyCrownMidColor"] = "#14FFFFFF",
        ["KeyLipTopColor"] = "#59FFFFFF", ["KeyLipBottomColor"] = "#33000000",
        ["AccentKeyTopColor"] = "#087067", ["AccentKeyBottomColor"] = "#065850",
        ["AccentKeyText"] = "#F4F7EF",
        ["ControlShadowColor"] = "#536057", ["SwitchHousingBorder"] = "#9DAA98",
        ["SwitchHousingTopColor"] = "#D4DBCE", ["SwitchHousingBottomColor"] = "#EEF2E9",
        ["SwitchChannelTopColor"] = "#66747D70", ["SwitchChannelMidColor"] = "#33747D70",
        ["SwitchChannelBottomColor"] = "#B8FFFFFF", ["SwitchThumbTopColor"] = "#7E897A",
        ["SwitchThumbMidColor"] = "#5F695C", ["SwitchThumbBottomColor"] = "#465044",
        ["SwitchSheen"] = "#66FFFFFF",
        ["DiscData"] = "#74837E", ["DiscHub"] = "#AAB7B2",
        ["DiscEdge"] = "#4B5954", ["DiscBack"] = "#2E3935",
        ["DiscTrack"] = "#E8F2EE",
    };
}
