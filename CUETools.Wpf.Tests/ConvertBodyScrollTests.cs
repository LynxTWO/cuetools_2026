using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

// Convert's middle row is centred content with no scroller, so tall content would be cut at both
// ends with no way to reach it. The page cannot simply be wrapped in a ScrollViewer: it is an
// Auto/*/Auto grid whose bottom row is a fixed status bar that must not scroll away.
[TestClass]
public sealed class ConvertBodyScrollTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void OnlyTheBodyScrollsAndTheStatusBarStaysPut()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive("Could not locate repository root from " + AppContext.BaseDirectory);

        XDocument document =
            XDocument.Load(Path.Combine(repoRoot, "CUETools.Wpf", "Views", "ConvertView.xaml"));

        XElement rootGrid = document.Root!.Elements(Presentation + "Grid").Single();
        Assert.AreEqual(
            Presentation + "Grid",
            rootGrid.Name,
            "the page root stays a grid; wrapping it would make the status bar scroll away");

        XElement scroller = document.Descendants(Presentation + "ScrollViewer")
            .Single(e => (string?)e.Attribute(Xaml + "Name") == "ConvertBodyScroller");
        Assert.AreEqual("1", scroller.Attribute("Grid.Row")?.Value,
            "only the middle row scrolls");
        Assert.AreEqual("Auto", scroller.Attribute("VerticalScrollBarVisibility")?.Value);

        XElement body = scroller.Elements(Presentation + "Grid").Single();
        Assert.AreEqual(
            "{Binding ActualHeight, ElementName=ConvertBodyScroller}",
            body.Attribute("MinHeight")?.Value,
            "MinHeight keeps the content vertically centred while there is room to centre it");

        // Search the whole document (not just rootGrid's direct children) so that if a future
        // edit slides the closing </Grid></ScrollViewer> pair too far down and swallows the
        // status bar into the scroller's content, this assertion catches it by location rather
        // than silently passing because Single() still finds exactly one match somewhere.
        XElement statusBar = document.Descendants(Presentation + "Border")
            .Single(e => (string?)e.Attribute("Grid.Row") == "2");
        Assert.AreSame(
            rootGrid,
            statusBar.Parent,
            "the Grid.Row=\"2\" status bar Border must be a direct child of the root Grid, " +
            "not nested inside ConvertBodyScroller, or it would scroll away with the body");
        Assert.IsFalse(
            scroller.Descendants().Contains(statusBar),
            "the Grid.Row=\"2\" status bar Border must stay outside ConvertBodyScroller, " +
            "or it would scroll away with the body");
    }
}
