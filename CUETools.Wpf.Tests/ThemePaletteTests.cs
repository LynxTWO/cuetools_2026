using System;
using System.Windows;
using System.Windows.Media;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class ThemePaletteTests
{
    [TestMethod]
    public void LightAndDarkPalettesExposeTheSameTypedControlTokens()
    {
        var dark = new ResourceDictionary();
        var light = new ResourceDictionary();

        ThemeService.Swap(dark, AppTheme.Dark);
        ThemeService.Swap(light, AppTheme.Light);

        string[] brushKeys =
        {
            "Ground", "Panel", "Ink", "InkDim", "Muted",
            "StatusAccent", "StatusGood", "StatusWarning", "StatusDanger",
            "ScrollTrack", "ScrollTrackBorder", "ScrollThumb",
            "ScrollThumbHover", "ScrollThumbPressed",
            "ControlBorder", "ButtonEdge", "SwitchHousingBorder",
            "DiscData", "DiscHub", "DiscEdge", "DiscBack", "DiscTrack",
        };
        foreach (string key in brushKeys)
        {
            Assert.IsInstanceOfType<SolidColorBrush>(dark[key]);
            Assert.IsInstanceOfType<SolidColorBrush>(light[key]);
        }

        string[] colorKeys =
        {
            "ControlShadowColor", "SwitchHousingTopColor",
            "SwitchHousingBottomColor", "SwitchChannelTopColor",
            "SwitchThumbTopColor",
        };
        foreach (string key in colorKeys)
        {
            Assert.IsInstanceOfType<Color>(dark[key]);
            Assert.IsInstanceOfType<Color>(light[key]);
        }
    }

    [TestMethod]
    public void LightPaletteActuallyChangesStructuralContrast()
    {
        var dark = new ResourceDictionary();
        var light = new ResourceDictionary();
        ThemeService.Swap(dark, AppTheme.Dark);
        ThemeService.Swap(light, AppTheme.Light);

        Assert.AreNotEqual(Brush(dark, "Ground"), Brush(light, "Ground"));
        Assert.AreNotEqual(Brush(dark, "Ink"), Brush(light, "Ink"));
        Assert.AreNotEqual(
            Brush(dark, "SwitchHousingBorder"),
            Brush(light, "SwitchHousingBorder"));
        Assert.AreNotEqual(Brush(dark, "DiscData"), Brush(light, "DiscData"));
        Assert.AreNotEqual(Brush(dark, "DiscEdge"), Brush(light, "DiscEdge"));
        Assert.AreNotEqual(Brush(dark, "StatusAccent"), Brush(light, "StatusAccent"));
    }

    [TestMethod]
    public void LightStatusTextTokensMeetNormalTextContrastOnAppSurfaces()
    {
        var light = new ResourceDictionary();
        ThemeService.Swap(light, AppTheme.Light);

        string[] statuses =
        {
            "StatusAccent", "StatusGood", "StatusWarning", "StatusDanger"
        };
        string[] surfaces = { "Ground", "Panel", "Face", "Glass" };
        foreach (string status in statuses)
        foreach (string surface in surfaces)
        {
            double ratio = Contrast(Brush(light, status), Brush(light, surface));
            Assert.IsTrue(
                ratio >= 4.5,
                $"{status} contrast on {surface} was only {ratio:F2}:1.");
        }
    }

    private static Color Brush(ResourceDictionary dictionary, string key) =>
        ((SolidColorBrush)dictionary[key]).Color;

    private static double Contrast(Color left, Color right)
    {
        double high = Math.Max(Luminance(left), Luminance(right));
        double low = Math.Min(Luminance(left), Luminance(right));
        return (high + 0.05) / (low + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) +
        0.7152 * Linear(color.G) +
        0.0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
