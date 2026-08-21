using System;
using System.Collections.Generic;

namespace CUETools.Wpf.Theme;

/// <summary>
/// The rail strip's glyph geometry, shared by both heads as raw path data
/// (each head parses into its own Geometry type). Keyed by the page TITLES,
/// which live in the shared view models and are therefore the one vocabulary
/// both heads already agree on. The glyph metaphors and the owner-approved
/// sheets are documented on the Linux head (RailIcons.cs and
/// docs/evidence/2026-08-20-slice013-icon-sheet-*.png in cuetools-linux).
/// 24x24 viewbox, 2px strokes, round caps.
/// </summary>
public static class RailIconPaths
{
    public static readonly (string Title, string Path)[] All =
    {
        ("Rip",
            "M 9.5,5 A 7,7 0 1 0 9.5,19 A 7,7 0 1 0 9.5,5 Z " +
            "M 9.5,10.5 A 1.5,1.5 0 1 0 9.5,13.5 A 1.5,1.5 0 1 0 9.5,10.5 Z " +
            "M 17,12 L 21.5,12 M 21.5,12 L 19.3,9.8 M 21.5,12 L 19.3,14.2"),
        ("Verify & Repair",
            "M 5,13 L 10,18 L 19,7"),
        ("Convert",
            "M 6,9 L 15,9 M 15,9 L 12,6 M 15,9 L 12,12 " +
            "M 18,15 L 9,15 M 9,15 L 12,12.5 M 9,15 L 12,18"),
        ("Queue",
            "M 5,7 L 19,7 M 5,12 L 19,12 M 5,17 L 12,17 " +
            "M 15,17 L 19,17 M 19,17 L 16.5,14.8 M 19,17 L 16.5,19.2"),
        ("Report",
            "M 7,4 L 15,4 L 18,7 L 18,20 L 7,20 Z M 15,4 L 15,7 L 18,7 " +
            "M 14.5,14.5 A 2,2 0 1 0 14.5,18.5 A 2,2 0 1 0 14.5,14.5 Z"),
        ("Naming",
            "M 8.5,6 A 2.5,2.5 0 1 0 8.5,11 A 2.5,2.5 0 1 0 8.5,6 Z " +
            "M 15.5,13 A 2.5,2.5 0 1 0 15.5,18 A 2.5,2.5 0 1 0 15.5,13 Z " +
            "M 17,6 L 7,18"),
        ("Drive & Read",
            "M 6,8 L 18,8 A 2,2 0 0 1 20,10 L 20,15 A 2,2 0 0 1 18,17 " +
            "L 6,17 A 2,2 0 0 1 4,15 L 4,10 A 2,2 0 0 1 6,8 Z " +
            "M 7,11.5 L 17,11.5 M 16.6,14.6 L 16.8,14.6"),
        ("Settings",
            "M 7,5 L 7,19 M 12,5 L 12,19 M 17,5 L 17,19 " +
            "M 5.2,14.5 L 8.8,14.5 M 10.2,8.5 L 13.8,8.5 M 15.2,12 L 18.8,12"),
        ("Advanced",
            "M 12,5 A 7,7 0 1 0 12,19 A 7,7 0 1 0 12,5 Z " +
            "M 12,12 L 16,8 M 12,12 L 12,12.01"),
        ("How a CD Works",
            "M 10.5,5 A 5.5,5.5 0 1 0 10.5,16 A 5.5,5.5 0 1 0 10.5,5 Z " +
            "M 10.5,9.2 A 1.3,1.3 0 1 0 10.5,11.8 A 1.3,1.3 0 1 0 10.5,9.2 Z " +
            "M 14.5,14.5 L 19,19"),
    };

    private static readonly Dictionary<string, string> ByTitleMap = Build();

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (title, path) in All) map[title] = path;
        return map;
    }

    /// <summary>The glyph for a page title, or null for a title without one
    /// (a new page renders its initial as an honest fallback until it earns
    /// a glyph).</summary>
    public static string? ForTitle(string? title)
        => title != null && ByTitleMap.TryGetValue(title, out string? path) ? path : null;
}

/// <summary>D-076's layout thresholds, shared by both heads: the full card
/// rail at 1140 logical pixels of window width and up, the icon strip below,
/// and the floor (a held layout inside horizontal scrolling) below 860.</summary>
public static class RailBreakpointValues
{
    public const double FullAt = 1140;
    public const double FloorBelow = 860;
    public const double HeldLayoutWidth = 860;
    public const double MinWindowWidth = 640;
    public const double MinWindowHeight = 480;
}
