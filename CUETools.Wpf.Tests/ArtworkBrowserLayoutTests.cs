using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class ArtworkBrowserLayoutTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void RipCoverIsAKeyboardAccessibleArtworkBrowserEntryPoint()
    {
        XDocument rip = Load("RipView.xaml");
        XElement button = rip.Descendants(Presentation + "Button").Single(
            element => element.Attribute("Click")?.Value == "Artwork_Click");

        Assert.AreEqual(
            "Choose album artwork",
            button.Attributes().Single(
                attribute => attribute.Name.LocalName == "AutomationProperties.Name").Value);
        Assert.AreEqual("True", button.Attribute("AllowDrop")?.Value);
        Assert.AreEqual("Artwork_Drop", button.Attribute("Drop")?.Value);
        Assert.AreEqual(
            "{Binding ArtEnabled, Converter={StaticResource BoolVis}}",
            button.Ancestors(Presentation + "StackPanel").First()
                .Attribute("Visibility")?.Value);
    }

    [TestMethod]
    public void BrowserIsResizableAndExposesRequiredSortableFacts()
    {
        XDocument browser = Load("ArtworkBrowserWindow.xaml");
        XElement window = browser.Root;
        Assert.AreEqual("720", window.Attribute("MinWidth")?.Value);
        Assert.AreEqual("480", window.Attribute("MinHeight")?.Value);
        Assert.AreNotEqual("NoResize", window.Attribute("ResizeMode")?.Value);

        string[] headers = browser.Descendants()
            .Where(element => element.Name.LocalName.StartsWith(
                "DataGrid", StringComparison.Ordinal))
            .Select(element => element.Attribute("Header")?.Value)
            .Where(value => value != null)
            .Cast<string>()
            .ToArray();
        CollectionAssert.IsSubsetOf(
            new[] { "Source", "Match", "Dimensions", "File size", "Type" },
            headers);

        string[] actions = browser.Descendants(Presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Where(value => value != null)
            .Cast<string>()
            .ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "Add local image...",
                "Open source page",
                "No cover",
                "Use automatic",
                "Cancel",
                "Use selected"
            },
            actions);
        Assert.AreEqual("True", window.Attribute("AllowDrop")?.Value);
        Assert.AreEqual("Window_Drop", window.Attribute("Drop")?.Value);
    }

    [TestMethod]
    public void BrowserGridUsesThemePaletteForBodyCellsAndSelection()
    {
        XDocument browser = Load("ArtworkBrowserWindow.xaml");
        XElement grid = browser.Descendants(Presentation + "DataGrid").Single();

        Assert.AreEqual(
            "{DynamicResource Panel}",
            grid.Attribute("Background")?.Value);
        Assert.AreEqual(
            "{DynamicResource Ink}",
            grid.Attribute("Foreground")?.Value);
        Assert.AreEqual(
            "{DynamicResource Line}",
            grid.Attribute("HorizontalGridLinesBrush")?.Value);

        XElement cellStyle = grid
            .Descendants(Presentation + "Style")
            .Single(style =>
                style.Attribute("TargetType")?.Value == "DataGridCell");
        Assert.IsTrue(
            cellStyle.Descendants(Presentation + "Setter").Any(
                setter =>
                    setter.Attribute("Property")?.Value == "Background" &&
                    setter.Attribute("Value")?.Value == "Transparent"));
        XElement selected = cellStyle
            .Descendants(Presentation + "Trigger")
            .Single(trigger =>
                trigger.Attribute("Property")?.Value == "IsSelected" &&
                trigger.Attribute("Value")?.Value == "True");
        Assert.IsTrue(
            selected.Descendants(Presentation + "Setter").Any(
                setter =>
                    setter.Attribute("Property")?.Value == "Background" &&
                    setter.Attribute("Value")?.Value ==
                    "{DynamicResource Face}"));

        XElement textStyle = grid
            .Descendants(Presentation + "Style")
            .Single(style =>
                style.Attribute("TargetType")?.Value == "TextBlock");
        Assert.IsTrue(
            textStyle.Descendants(Presentation + "Setter").Any(
                setter =>
                    setter.Attribute("Property")?.Value == "Foreground" &&
                    setter.Attribute("Value")?.Value ==
                    "{DynamicResource Ink}"));
    }

    private static XDocument Load(string file)
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(root))
            Assert.Inconclusive("Could not locate repository root.");
        return XDocument.Load(Path.Combine(root, "CUETools.Wpf", "Views", file));
    }
}
