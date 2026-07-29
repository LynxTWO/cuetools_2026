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
    }

    private static Color Brush(ResourceDictionary dictionary, string key) =>
        ((SolidColorBrush)dictionary[key]).Color;
}
