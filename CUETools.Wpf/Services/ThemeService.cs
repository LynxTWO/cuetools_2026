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
/// The themeable structural palette is intentionally NOT in Theme.xaml - if it were, Theme.xaml
/// would be an always-present competing source for the same keys and the swapped-in dictionary
/// could not win. Accents (Teal/Amber/Good) and the lit switches stay put in both themes on
/// purpose and live in Theme.xaml.
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
            d[kv.Key] = new SolidColorBrush(C(kv.Value));
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
        ["Line"] = "#28312A", ["Ink"] = "#EDF1E9", ["InkDim"] = "#B1BCAE", ["Muted"] = "#7D887C",
        ["Glass"] = "#0E1311", ["GlassLine"] = "#243029", ["ButtonPressed"] = "#0C110E",
        ["ButtonFaceTop"] = "#1B221C", ["ButtonFaceBot"] = "#121813",
    };

    private static readonly Dictionary<string, string> Light = new()
    {
        ["Ground"] = "#E7ECE2", ["Bar"] = "#DEE4D8", ["Face"] = "#F4F7EF", ["Panel"] = "#F1F5EB",
        ["Line"] = "#CAD2C2", ["Ink"] = "#1A211B", ["InkDim"] = "#414A40", ["Muted"] = "#6C766A",
        ["Glass"] = "#E4E9DD", ["GlassLine"] = "#CAD2C2", ["ButtonPressed"] = "#D6DDCC",
        ["ButtonFaceTop"] = "#FBFDF7", ["ButtonFaceBot"] = "#E9EEE1",
    };
}
