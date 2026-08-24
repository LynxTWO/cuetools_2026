using System;

namespace CUETools.Wpf.Views;

/// <summary>
/// Width arithmetic for the queue's GridView. GridViewColumn has no star sizing, so the last
/// column is computed on resize instead. Kept as a pure function so the reflow can be tested
/// without a window. The fixed three are unchanged from the original layout.
/// </summary>
internal static class QueueColumnLayout
{
    public const double SourceWidth = 300;
    public const double ActionWidth = 90;
    public const double StatusWidth = 110;

    /// <summary>Below this the horizontal scrollbar takes over rather than the column vanishing.</summary>
    public const double MinimumResultWidth = 120;

    /// <summary>GridView row padding plus the vertical scrollbar the list shows when it fills.</summary>
    public const double Chrome = 26;

    public static double ResultWidth(double listWidth, double chrome)
    {
        double leftOver =
            listWidth - chrome - (SourceWidth + ActionWidth + StatusWidth);
        return leftOver < MinimumResultWidth ? MinimumResultWidth : leftOver;
    }
}
