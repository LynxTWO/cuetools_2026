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
    }
}
