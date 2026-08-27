using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

// D14 (2026-08-27, option B): the app keeps its own palette under Windows high contrast on
// purpose, so every selection visual must come from the palette rather than from whichever
// theme dictionary WPF happens to load. The codec picker's stock ListViewItem template painted
// selection with SystemColors.HighlightBrush under high contrast while the cell text stayed
// palette Ink - white on system cyan in the dark theme. The row now owns its template.
[TestClass]
public sealed class CodecPickerLayoutTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static XElement RowStyle()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive("Could not locate repository root from " + AppContext.BaseDirectory);

        XDocument picker = XDocument.Load(
            Path.Combine(repoRoot, "CUETools.Wpf", "Views", "CodecPickerWindow.xaml"));
        XElement list = picker.Descendants(Presentation + "ListView")
            .Single(e => (string?)e.Attribute("Name") == "CodecList"
                         || e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "CodecList"));
        return list.Element(Presentation + "ListView.Resources")!
            .Elements(Presentation + "Style")
            .Single(style => style.Attribute("TargetType")?.Value == "ListViewItem");
    }

    [TestMethod]
    public void TheRowOwnsItsTemplateSoSelectionNeverComesFromTheThemeDictionary()
    {
        XElement style = RowStyle();
        XElement template = style.Elements(Presentation + "Setter")
            .Single(s => (string?)s.Attribute("Property") == "Template")
            .Descendants(Presentation + "ControlTemplate")
            .Single();

        Assert.AreEqual("ListViewItem", template.Attribute("TargetType")?.Value);
        XElement border = template.Elements(Presentation + "Border").Single();
        Assert.AreEqual("{TemplateBinding Background}", border.Attribute("Background")?.Value,
            "the template must paint the style's background, not a theme brush");
        Assert.AreEqual("{TemplateBinding BorderBrush}", border.Attribute("BorderBrush")?.Value);
        Assert.IsTrue(
            border.Descendants(Presentation + "GridViewRowPresenter").Any(),
            "the row still has to present its GridView columns");
        Assert.IsFalse(
            template.ToString().Contains("SystemColors", StringComparison.Ordinal),
            "no system colour may reach the row template");
    }

    [TestMethod]
    public void SelectionBackgroundAndTextComeFromThePalette()
    {
        XElement style = RowStyle();
        var defaults = style.Elements(Presentation + "Setter")
            .Where(s => s.Attribute("Value") != null)
            .ToDictionary(s => s.Attribute("Property")!.Value, s => s.Attribute("Value")!.Value);
        Assert.AreEqual("{DynamicResource Ink}", defaults["Foreground"],
            "row text inherits the palette foreground, the same source as the selection background");

        XElement selected = style.Descendants(Presentation + "Trigger")
            .Single(t => (string?)t.Attribute("Property") == "IsSelected"
                         && (string?)t.Attribute("Value") == "True");
        var setters = selected.Elements(Presentation + "Setter")
            .ToDictionary(s => s.Attribute("Property")!.Value, s => s.Attribute("Value")!.Value);
        Assert.AreEqual("{DynamicResource Face}", setters["Background"]);
        Assert.AreEqual("{DynamicResource Teal}", setters["BorderBrush"]);
    }
}
