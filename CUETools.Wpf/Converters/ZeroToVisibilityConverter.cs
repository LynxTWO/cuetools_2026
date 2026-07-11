using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CUETools.Wpf.Converters;

/// <summary>Int count to Visibility: 0 -> Visible (show the empty-state), non-zero -> Collapsed.</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is int n && n == 0) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
