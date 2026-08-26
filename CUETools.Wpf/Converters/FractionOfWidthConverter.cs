using System;
using System.Globalization;
using System.Windows.Data;

namespace CUETools.Wpf.Converters;

/// <summary>A width to a fraction of it, for proportional MaxWidth bounds: the bound scales with
/// the row instead of trimming at a fixed pixel count when there is room. Before the source has a
/// real measure (ActualWidth 0 on the first pass) the bound is unlimited rather than collapsing
/// the target to zero width.</summary>
public sealed class FractionOfWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || width <= 0 ||
            !double.TryParse(
                parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double fraction) ||
            fraction <= 0)
            return double.PositiveInfinity;
        return width * fraction;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
