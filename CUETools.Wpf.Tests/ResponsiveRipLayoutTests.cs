using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class ResponsiveRipLayoutTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void NarrowRipLayoutWrapsActionsAndKeepsEveryRegionReachable()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive(
                "Could not locate repository root from " + AppContext.BaseDirectory);

        XDocument document = XDocument.Load(
            Path.Combine(repoRoot, "CUETools.Wpf", "Views", "RipView.xaml"));

        XElement workScroller = Named(document, "DiscWorkScroller");
        Assert.AreEqual(
            "Auto",
            workScroller.Attribute("HorizontalScrollBarVisibility")?.Value);
        XElement workGrid = workScroller.Elements(Presentation + "Grid").Single();
        Assert.AreEqual("860", workGrid.Attribute("MinWidth")?.Value);
        Assert.AreEqual(
            "{Binding ActualWidth, ElementName=DiscWorkScroller}",
            workGrid.Attribute("Width")?.Value,
            "The horizontal fallback must not make star columns measure at infinite width.");

        XElement commandGrid = Named(document, "DiscCommandGrid");
        Assert.AreEqual("860", commandGrid.Attribute("MinWidth")?.Value);
        Assert.AreEqual(
            "{Binding ActualWidth, ElementName=DiscCommandScroller}",
            commandGrid.Attribute("Width")?.Value);

        XElement actions = Named(document, "RipActionWrap");
        Assert.AreEqual(
            Presentation + "WrapPanel",
            actions.Name,
            "Rip settings and primary commands must wrap instead of clipping.");
        CollectionAssert.IsSubsetOf(
            new[] { "Test & Copy", "Rip", "Verify", "Stop" },
            actions.Descendants()
                .Select(element => element.Attribute("Content")?.Value)
                .Where(value => value != null)
                .Cast<string>()
                .ToArray());
        Assert.IsFalse(
            actions.Descendants().Any(
                element => element.Attribute("Content")?.Value == "Deep recovery"),
            "Deep recovery is a durable expert setting, not a per-rip action.");

        AssertRailCanScroll(document, "RipLeftRailScroll");
        AssertRailCanScroll(document, "RipRightRailScroll");

        XElement title = Named(document, "AlbumTitleText");
        Assert.AreEqual(
            "CharacterEllipsis",
            title.Attribute("TextTrimming")?.Value);
        Assert.AreEqual(
            "{Binding AlbumTitle}",
            title.Attribute("ToolTip")?.Value);

        XElement columns = workGrid.Element(
            Presentation + "Grid.ColumnDefinitions")!;
        string[] widths = columns.Elements(Presentation + "ColumnDefinition")
            .Select(column => (string)column.Attribute("Width")!)
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { "0.35*", "*", "0.32*" },
            widths,
            "The side rails must give width back to the center as the window narrows.");
    }

    [TestMethod]
    public void DeepRecoveryIsAnExpertSettingAndNotARipPageToggle()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive(
                "Could not locate repository root from " + AppContext.BaseDirectory);

        XDocument rip = XDocument.Load(
            Path.Combine(repoRoot, "CUETools.Wpf", "Views", "RipView.xaml"));
        Assert.IsFalse(
            rip.Descendants().Any(
                element => element.Attribute("IsChecked")?.Value.Contains(
                    "DeepRecovery",
                    StringComparison.Ordinal) == true));

        XDocument settings = XDocument.Load(
            Path.Combine(repoRoot, "CUETools.Wpf", "Views", "SettingsView.xaml"));
        XElement toggle = settings.Descendants(Presentation + "ToggleButton").Single(
            element => element.Attribute("IsChecked")?.Value.Contains(
                "DeepRecovery",
                StringComparison.Ordinal) == true);
        Assert.AreEqual("{Binding DeepRecovery}", toggle.Attribute("IsChecked")?.Value);
    }

    private static void AssertRailCanScroll(
        XDocument document,
        string name)
    {
        XElement rail = Named(document, name);
        Assert.AreEqual(
            "Auto",
            rail.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.AreEqual(
            "Disabled",
            rail.Attribute("HorizontalScrollBarVisibility")?.Value);
    }

    private static XElement Named(XDocument document, string name)
    {
        return document.Descendants().Single(
            element => element.Attribute(Xaml + "Name")?.Value == name);
    }
}
