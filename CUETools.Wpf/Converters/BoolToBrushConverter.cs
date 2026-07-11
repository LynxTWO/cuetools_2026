using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CUETools.Wpf.Converters;

/// <summary>
/// Maps a bool to a themed brush by resource key. ConverterParameter is "trueKey;falseKey"
/// (default "Good;Muted"): true -> the "on" accent, false -> a dim brush. Resolves through the
/// live application resources so it follows the light/dark theme swap.
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool on = value is bool b && b;
        string keys = parameter as string ?? "Good;Muted";
        string[] parts = keys.Split(';');
        string key = on ? parts[0] : (parts.Length > 1 ? parts[1] : parts[0]);
        object res = System.Windows.Application.Current?.TryFindResource(key);
        return res as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
