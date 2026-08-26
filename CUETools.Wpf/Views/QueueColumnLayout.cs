using System.Windows;

namespace CUETools.Wpf.Views;

/// <summary>
/// Width arithmetic for the queue's GridView. GridViewColumn has no star sizing, so the last
/// column is computed on resize instead. Kept as a pure function so the reflow can be tested
/// without a window. The fixed three are unchanged from the original layout.
///
/// Chrome is not one opaque guess. It is the sum of the base layout chrome the ListView reserves
/// with no vertical scrollbar showing (BaseChrome), plus the scrollbar itself once the rows
/// overflow (ScrollBar), read live so it cannot silently drift on a themed, high-DPI, or
/// accessibility-configured system. Both parts were measured, not assumed: a real ListView with
/// the same four columns (BorderThickness="0", no ListView style override) was laid out at
/// listWidth=900, and the largest Result column width that produced ScrollableWidth == 0 was
/// found by binary search. With too few rows to show a vertical scrollbar that converged to
/// 5.99px (BaseChrome=6); with enough rows to force one it converged to 22.99px, an implied
/// scrollbar contribution of exactly 17.00px, matching SystemParameters.VerticalScrollBarWidth
/// on this machine (measured 2026-08-24). The prior single constant, Chrome=26, was 3px more
/// than the measured worst case (23) - not dangerously wrong, but unverified against reality.
/// </summary>
internal static class QueueColumnLayout
{
    public const double SourceWidth = 300;
    public const double ActionWidth = 90;
    public const double StatusWidth = 110;

    /// <summary>Below this the horizontal scrollbar takes over rather than the column vanishing.</summary>
    public const double MinimumResultWidth = 120;

    /// <summary>
    /// Layout chrome the ListView reserves with no vertical scrollbar visible: its own border
    /// and padding plus the small horizontal inset GridViewRowPresenter keeps. Measured 5.99px
    /// (see the class remarks); 6 is used so the reservation is never short.
    /// </summary>
    public const double BaseChrome = 6;

    /// <summary>
    /// The vertical scrollbar the list shows once its rows overflow the viewport. Read live
    /// instead of hardcoded so it tracks the real system/theme/DPI/accessibility scrollbar width
    /// rather than a guess that can drift from it.
    /// </summary>
    public static double ScrollBar => SystemParameters.VerticalScrollBarWidth;

    /// <summary>Total width unavailable to the four columns: base chrome plus the scrollbar.</summary>
    public static double Chrome => BaseChrome + ScrollBar;

    public static double ResultWidth(double listWidth, double chrome)
    {
        double leftOver =
            listWidth - chrome - (SourceWidth + ActionWidth + StatusWidth);
        return leftOver < MinimumResultWidth ? MinimumResultWidth : leftOver;
    }
}
