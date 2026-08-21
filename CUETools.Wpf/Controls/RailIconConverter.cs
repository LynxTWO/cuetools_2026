using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CUETools.Wpf.Theme;

namespace CUETools.Wpf.Controls;

/// <summary>Maps a page title to its strip glyph (shared path data in
/// App.Core, parsed once per title). A title without a glyph converts to
/// null and the template falls back to the page's initial.</summary>
public sealed class RailIconConverter : IValueConverter
{
    private static readonly Dictionary<string, Geometry?> Cache = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? title = value as string;
        if (title == null) return null;
        if (!Cache.TryGetValue(title, out Geometry? geometry))
        {
            string? path = RailIconPaths.ForTitle(title);
            geometry = path == null ? null : Geometry.Parse(path);
            if (geometry is { CanFreeze: true }) geometry.Freeze();
            Cache[title] = geometry;
        }
        return geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
