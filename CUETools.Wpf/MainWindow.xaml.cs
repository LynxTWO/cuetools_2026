using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CUETools.Wpf.Services;

namespace CUETools.Wpf;

public partial class MainWindow : Window
{
    private readonly ThemeService _theme;

    public MainWindow(ThemeService theme)
    {
        InitializeComponent();
        _theme = theme;
        // The palette brushes are recolored in place by ThemeService (they are single mutable
        // objects in the app resources), so the window just plays the light-switch animation.
        _theme.Changed += (_, _) => AnimateLightSwitch(_theme.Current);
    }

    // Model a real light being switched: going to LIGHT the room brightens (a dark cover fades out
    // fast, like a bulb warming); going to DARK it dims and the warm glow lingers (a warm cover
    // fades out with a long tail, like a filament cooling). The palette itself has already swapped;
    // this overlay just carries the physical brightness curve on top of it.
    private void AnimateLightSwitch(AppTheme theme)
    {
        bool toLight = theme == AppTheme.Light;

        // cover color = the look we are leaving, so fading it out reveals the new theme
        ThemeFlash.Background = new SolidColorBrush(toLight
            ? Color.FromRgb(0x0A, 0x0E, 0x0C)   // dark room (fades away as the light comes on)
            : Color.FromRgb(0xFB, 0xF4, 0xE6));  // warm incandescent glow (lingers as it dies)

        var anim = new DoubleAnimation
        {
            From = toLight ? 0.92 : 0.85,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(toLight ? 300 : 420),  // cool-down lasts longer
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, // fast then long tail
            FillBehavior = FillBehavior.Stop
        };
        ThemeFlash.BeginAnimation(OpacityProperty, anim);
    }
}
