using System.Windows;

namespace CUETools.Wpf.Theme;

/// <summary>
/// Widths for the two rail states. The strip number is arithmetic, not taste: the column has to
/// hold the 44px icon button, the ListBox padding, the vertical scrollbar that appears whenever
/// the rail overflows, and the panel border. At 56 the scrollbar left 22px of content for a 44px
/// button, so every icon rendered at roughly half width once the window was shorter than about
/// 600px (measured 2026-08-24 at 640x480). Do not shrink this back without redoing that sum.
///
/// The scrollbar part is read live from SystemParameters rather than frozen at 17, because the
/// app's ScrollBar style overrides only Background and Template - the WPF theme style's
/// Width="{DynamicResource SystemParameters.VerticalScrollBarWidthKey}" still governs the real
/// layout width. A themed, high-DPI, or accessibility-configured system moves the bar, and a
/// frozen constant would not follow it. The sum leaves exactly one whole icon and no slack, so
/// tracking the live metric is what keeps the tier-1 never-clip guarantee true.
/// </summary>
internal static class RailColumnWidths
{
    /// <summary>The strip icon contract in RailIconPaths, shared with the Linux head.</summary>
    public const double IconButton = 44;

    /// <summary>NavList Padding="8,8", so 8 left plus 8 right.</summary>
    public const double ListPadding = 16;

    /// <summary>
    /// The rail's own vertical scrollbar, read live. 17 at 96 dpi with the default system metric,
    /// which is the value the 78px strip column was measured against.
    /// </summary>
    public static double ScrollBar => SystemParameters.VerticalScrollBarWidth;

    /// <summary>The rail Border's BorderThickness="0,0,1,0".</summary>
    public const double Border = 1;

    /// <summary>
    /// 44 + 16 + scrollbar + 1, which is 78 at the default 17px scrollbar metric. Computed rather
    /// than frozen so it tracks the scrollbar the rail actually draws.
    /// </summary>
    public static double Strip => IconButton + ListPadding + ScrollBar + Border;

    public const double Full = 214;
}
