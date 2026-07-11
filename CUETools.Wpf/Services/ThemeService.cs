using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Services;

public enum AppTheme { Dark, Light }

/// <summary>
/// Live light/dark theming without a DynamicResource refactor: the palette lives as named,
/// non-frozen brush objects in the merged theme dictionary, and switching a theme mutates those
/// brushes' colors in place. Every StaticResource reference keeps pointing at the same brush
/// object, so the whole app re-colors at once. The signature lit switches stay dark in both
/// themes on purpose - a dark tactile control with a glowing lens reads well on a light panel.
/// </summary>
public sealed class ThemeService
{
    private readonly string _prefPath;

    public AppTheme Current { get; private set; } = AppTheme.Dark;
    public event EventHandler? Changed;

    public ThemeService()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CUETools2026");
        _prefPath = System.IO.Path.Combine(dir, "theme.txt");
        try { if (System.IO.File.ReadAllText(_prefPath).Trim() == "Light") Current = AppTheme.Light; }
        catch { /* no saved pref - stay dark */ }
    }

    public void Apply(AppTheme theme)
    {
        if (Application.Current != null) Apply(Application.Current.Resources, theme);
        Current = theme;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_prefPath)!);
            System.IO.File.WriteAllText(_prefPath, theme.ToString());
        }
        catch { /* best-effort persistence */ }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    /// <summary>Replace the named palette resources of <paramref name="r"/> for the given theme.
    /// Replacing values (not mutating frozen brushes) is what lets DynamicResource references
    /// re-resolve live. Static + dictionary-scoped so a headless render harness can preview a
    /// theme too.</summary>
    public static void Apply(ResourceDictionary r, AppTheme theme)
    {
        var p = theme == AppTheme.Light ? Light : Dark;
        foreach (var kv in p)
        {
            if (kv.Key is "ButtonFaceTop" or "ButtonFaceBot") continue;
            if (kv.Key == "ButtonFace")
            {
                r["ButtonFace"] = new LinearGradientBrush(C(p["ButtonFaceTop"]), C(p["ButtonFaceBot"]), 90);
                continue;
            }
            r[kv.Key] = new SolidColorBrush(C(kv.Value));
        }
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    // Dark reproduces the XAML defaults (so toggling back is exact). Only structural surfaces
    // change; accents (Teal/Amber/Good/TealSoft) and the switch internals stay put.
    private static readonly Dictionary<string, string> Dark = new()
    {
        ["Ground"] = "#0C0F0D", ["Bar"] = "#0E1310", ["Face"] = "#161C16", ["Panel"] = "#141A16",
        ["Line"] = "#28312A", ["Ink"] = "#EDF1E9", ["InkDim"] = "#B1BCAE", ["Muted"] = "#7D887C",
        ["Glass"] = "#0E1311", ["GlassLine"] = "#243029", ["ButtonPressed"] = "#0C110E",
        ["ButtonFace"] = "1", ["ButtonFaceTop"] = "#1B221C", ["ButtonFaceBot"] = "#121813",
    };

    private static readonly Dictionary<string, string> Light = new()
    {
        ["Ground"] = "#E7ECE2", ["Bar"] = "#DEE4D8", ["Face"] = "#F4F7EF", ["Panel"] = "#F1F5EB",
        ["Line"] = "#CAD2C2", ["Ink"] = "#1A211B", ["InkDim"] = "#414A40", ["Muted"] = "#6C766A",
        ["Glass"] = "#E4E9DD", ["GlassLine"] = "#CAD2C2", ["ButtonPressed"] = "#D6DDCC",
        ["ButtonFace"] = "1", ["ButtonFaceTop"] = "#FBFDF7", ["ButtonFaceBot"] = "#E9EEE1",
    };
}
