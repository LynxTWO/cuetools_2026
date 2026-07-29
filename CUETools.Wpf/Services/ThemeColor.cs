using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Services;

/// <summary>
/// Resolves live palette colors for code-drawn controls. XAML uses
/// DynamicResource automatically; DrawingContext controls must request the
/// current brush on each render or they remain stuck in the dark palette.
/// </summary>
internal static class ThemeColor
{
    internal static Color Get(string key, Color fallback)
    {
        object? resource = Application.Current?.TryFindResource(key);
        return resource is SolidColorBrush brush
            ? brush.Color
            : fallback;
    }
}
