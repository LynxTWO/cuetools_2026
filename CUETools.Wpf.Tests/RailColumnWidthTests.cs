using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using CUETools.Wpf.Theme;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

// The strip column is not decorative. At 56 the vertical scrollbar that appears when the rail
// overflows left 22px of content for a 44px icon button, so the icons rendered at half width
// (measured 2026-08-24, window 640x480). These pin the arithmetic and the fix.
//
// The sum leaves exactly one whole icon and no slack, so every part of it has to be true rather
// than merely written down: the scrollbar is read live from SystemParameters, and the other three
// parts are checked against the markup in MainWindow.xaml they claim to mirror. Without that
// second check, changing Padding="8,8" to "12,12" would clip the icons again with a green suite.
[TestClass]
public sealed class RailColumnWidthTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void TheStripColumnHoldsTheIconPaddingScrollbarAndBorder()
    {
        Assert.AreEqual(44, RailColumnWidths.IconButton, "the 44x38 strip icon contract");
        Assert.AreEqual(
            SystemParameters.VerticalScrollBarWidth,
            RailColumnWidths.ScrollBar,
            0.001,
            "the scrollbar part must be read live: the app's ScrollBar style overrides only "
                + "Background and Template, so the theme style's system-metric Width still "
                + "governs the real bar");
        Assert.AreEqual(
            RailColumnWidths.IconButton
                + RailColumnWidths.ListPadding
                + RailColumnWidths.ScrollBar
                + RailColumnWidths.Border,
            RailColumnWidths.Strip,
            0.001,
            "the strip column must be the sum of what it has to hold");
        Assert.AreEqual(
            44 + 16 + SystemParameters.VerticalScrollBarWidth + 1,
            RailColumnWidths.Strip,
            0.001,
            "78 at the default 17px scrollbar metric, and it must move with that metric");
        Assert.AreEqual(214, RailColumnWidths.Full);
    }

    [TestMethod]
    public void TheStripColumnLeavesAWholeIconAfterTheScrollbar()
    {
        double content =
            RailColumnWidths.Strip - RailColumnWidths.Border - RailColumnWidths.ListPadding;
        Assert.IsTrue(
            content - RailColumnWidths.ScrollBar >= RailColumnWidths.IconButton,
            "a scrolling rail must still draw a whole 44px icon, not a clipped one");
    }

    [TestMethod]
    public void EachMirroredConstantMatchesTheMarkupItMirrors()
    {
        XDocument document = MainWindow();

        XElement navList = document.Descendants(Presentation + "ListBox")
            .Single(e => (string?)e.Attribute(Xaml + "Name") == "NavList");
        Assert.AreEqual(
            RailColumnWidths.ListPadding,
            HorizontalOf(navList.Attribute("Padding")?.Value, "NavList Padding"),
            0.001,
            "ListPadding mirrors NavList's Padding; widening it eats into the icon");

        XElement rail = document.Descendants(Presentation + "Border")
            .Single(e => e.Descendants(Presentation + "ListBox")
                .Any(l => (string?)l.Attribute(Xaml + "Name") == "NavList"));
        Assert.AreEqual(
            RailColumnWidths.Border,
            HorizontalOf(rail.Attribute("BorderThickness")?.Value, "rail BorderThickness"),
            0.001,
            "Border mirrors the rail Border's horizontal thickness");

        XElement strip = document.Descendants(Presentation + "DataTemplate")
            .Single(e => (string?)e.Attribute(Xaml + "Key") == "StripNavTemplate");
        XElement icon = strip.Elements(Presentation + "Border").Single();
        Assert.AreEqual(
            RailColumnWidths.IconButton,
            Number(icon.Attribute("Width")?.Value, "StripNavTemplate Border Width"),
            0.001,
            "IconButton mirrors the strip icon button's width");
    }

    [TestMethod]
    public void TheStripItemPinsTheIconLeftSoItDoesNotShift()
    {
        XElement style = MainWindow().Descendants(Presentation + "Style")
            .Single(e => (string?)e.Attribute(Xaml + "Key") == "StripNavItem");
        XElement? alignment = style.Elements(Presentation + "Setter")
            .FirstOrDefault(e => (string?)e.Attribute("Property") == "HorizontalAlignment");

        Assert.IsNotNull(alignment, "StripNavItem must set HorizontalAlignment");
        Assert.AreEqual(
            "Left",
            alignment!.Attribute("Value")?.Value,
            "Centring the icon moves it about 8px when the scrollbar appears; pin it left.");
    }

    private static XDocument MainWindow()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive("Could not locate repository root from " + AppContext.BaseDirectory);

        return XDocument.Load(Path.Combine(repoRoot, "CUETools.Wpf", "MainWindow.xaml"));
    }

    // WPF Thickness shorthand: one value is all four sides, two are horizontal then vertical,
    // four are left, top, right, bottom. Only the horizontal total matters to the strip column.
    private static double HorizontalOf(string? thickness, string what)
    {
        Assert.IsNotNull(thickness, what + " must be declared in MainWindow.xaml");
        double[] parts = thickness!.Split(',')
            .Select(part => Number(part, what))
            .ToArray();
        return parts.Length switch
        {
            1 => parts[0] * 2,
            2 => parts[0] * 2,
            4 => parts[0] + parts[2],
            _ => throw new AssertFailedException(what + " is not a WPF thickness: " + thickness),
        };
    }

    private static double Number(string? value, string what)
    {
        Assert.IsNotNull(value, what + " must be declared in MainWindow.xaml");
        Assert.IsTrue(
            double.TryParse(value!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed),
            what + " must be a number, was " + value);
        return parsed;
    }
}
