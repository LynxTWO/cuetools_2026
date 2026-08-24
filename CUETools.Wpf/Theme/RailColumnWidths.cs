namespace CUETools.Wpf.Theme;

/// <summary>
/// Widths for the two rail states. The strip number is arithmetic, not taste: the column has to
/// hold the 44px icon button, the ListBox padding, the vertical scrollbar that appears whenever
/// the rail overflows, and the panel border. At 56 the scrollbar left 22px of content for a 44px
/// button, so every icon rendered at roughly half width once the window was shorter than about
/// 600px (measured 2026-08-24 at 640x480). Do not shrink this back without redoing that sum.
/// </summary>
internal static class RailColumnWidths
{
    /// <summary>The strip icon contract in RailIconPaths, shared with the Linux head.</summary>
    public const double IconButton = 44;

    /// <summary>NavList Padding="8,8", so 8 left plus 8 right.</summary>
    public const double ListPadding = 16;

    /// <summary>SystemParameters.VerticalScrollBarWidth at 96 dpi.</summary>
    public const double ScrollBar = 17;

    /// <summary>The rail Border's BorderThickness="0,0,1,0".</summary>
    public const double Border = 1;

    public const double Strip = IconButton + ListPadding + ScrollBar + Border;
    public const double Full = 214;
}
