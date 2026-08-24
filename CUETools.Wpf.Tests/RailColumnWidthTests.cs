using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CUETools.Wpf.Theme;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

// The strip column is not decorative. At 56 the vertical scrollbar that appears when the rail
// overflows left 22px of content for a 44px icon button, so the icons rendered at half width
// (measured 2026-08-24, window 640x480). These pin the arithmetic and the fix.
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
            RailColumnWidths.IconButton
                + RailColumnWidths.ListPadding
                + RailColumnWidths.ScrollBar
                + RailColumnWidths.Border,
            RailColumnWidths.Strip,
            "the strip column must be the sum of what it has to hold");
        Assert.AreEqual(78, RailColumnWidths.Strip);
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
    public void TheStripItemPinsTheIconLeftSoItDoesNotShift()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive("Could not locate repository root from " + AppContext.BaseDirectory);

        XDocument document =
            XDocument.Load(Path.Combine(repoRoot, "CUETools.Wpf", "MainWindow.xaml"));
        XElement style = document.Descendants(Presentation + "Style")
            .Single(e => (string?)e.Attribute(Xaml + "Key") == "StripNavItem");
        XElement? alignment = style.Elements(Presentation + "Setter")
            .FirstOrDefault(e => (string?)e.Attribute("Property") == "HorizontalAlignment");

        Assert.IsNotNull(alignment, "StripNavItem must set HorizontalAlignment");
        Assert.AreEqual(
            "Left",
            alignment!.Attribute("Value")?.Value,
            "Centring the icon moves it about 8px when the scrollbar appears; pin it left.");
    }
}
