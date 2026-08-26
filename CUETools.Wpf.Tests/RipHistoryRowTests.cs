using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

// The history row used to dock Result before When in a DockPanel. DockPanel reserves space in
// declaration order, so Result took everything left and When was starved to zero width: the
// relative timestamp rendered in none of the 40 scaling captures on 2026-08-24, and Result was
// cut mid-word with no ellipsis and no tooltip.
[TestClass]
public sealed class RipHistoryRowTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement HistoryRow()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive("Could not locate repository root from " + AppContext.BaseDirectory);

        XDocument document =
            XDocument.Load(Path.Combine(repoRoot, "CUETools.Wpf", "Views", "RipView.xaml"));
        return document.Descendants(Presentation + "DockPanel")
            .Single(panel => panel.Descendants(Presentation + "TextBlock")
                .Any(t => (string?)t.Attribute("Text") == "{Binding Result}"));
    }

    [TestMethod]
    public void TheTimestampIsReservedBeforeTheEvidenceText()
    {
        XElement row = HistoryRow();
        XElement[] children = row.Elements().ToArray();

        XElement when = children.Single(
            e => (string?)e.Attribute("Text") == "{Binding When}");
        XElement result = children.Single(
            e => (string?)e.Attribute("Text") == "{Binding Result}");

        // Attached-property attributes are written unprefixed throughout this file's XAML.
        Assert.AreEqual(
            "Right",
            when.Attribute("DockPanel.Dock")?.Value,
            "When must dock right so the timestamp always has room");
        Assert.IsTrue(
            Array.IndexOf(children, when) < Array.IndexOf(children, result),
            "DockPanel reserves in declaration order, so When must come before Result");
        Assert.IsNull(
            result.Attribute("DockPanel.Dock"),
            "Result is the fill child, so it takes the leftover middle and trims there");
        Assert.AreEqual(
            "True",
            row.Attribute("LastChildFill")?.Value,
            "the evidence text is the fill child");
    }

    [TestMethod]
    public void TheEvidenceTextTrimsAndKeepsItsFullValueInATooltip()
    {
        XElement result = HistoryRow().Elements()
            .Single(e => (string?)e.Attribute("Text") == "{Binding Result}");

        Assert.AreEqual("CharacterEllipsis", result.Attribute("TextTrimming")?.Value);
        Assert.AreEqual("NoWrap", result.Attribute("TextWrapping")?.Value);
        Assert.AreEqual(
            "{Binding Result}",
            result.Attribute("ToolTip")?.Value,
            "CLAUDE.md allows trimming only when the full value stays available in a tooltip");
    }

    [TestMethod]
    public void TheTitleColumnIsBoundedAndTrimsWithATooltip()
    {
        // Same defect class as D13, one element over. The title block is the first-reserved
        // DockPanel.Dock="Left" child, and DockPanel grants it min(desired, remaining) before
        // anything else. An unbounded title - a classical box set runs to roughly 600px at
        // Serif 14 - starves the fill child to zero width, and a zero-width TextBlock has no
        // hover surface, so the tooltip that makes its trimming legal becomes unreachable.
        XElement row = HistoryRow();
        Assert.AreEqual(
            "HistoryRowDock",
            row.Attribute(Xaml + "Name")?.Value,
            "the row panel must be named so the title bound can follow its width");
        XElement titles = row.Elements(Presentation + "StackPanel").Single();

        string? declared = titles.Attribute("MaxWidth")?.Value;
        Assert.IsNotNull(
            declared,
            "the title block must be bounded, or a long title starves the evidence text");
        StringAssert.Contains(
            declared!, "Binding ActualWidth",
            "the bound is proportional - a fixed cap trims long titles even when the row has room");
        StringAssert.Contains(declared!, "ElementName=HistoryRowDock");
        StringAssert.Contains(declared!, "FractionOfWidth");

        Match fraction = Regex.Match(declared!, "ConverterParameter=(?<f>[0-9.]+)");
        Assert.IsTrue(fraction.Success, "the share must be declared, was " + declared);
        double share = double.Parse(
            fraction.Groups["f"].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.IsTrue(
            share > 0 && share <= 0.5,
            "the title column keeps at most half the row so the timestamp and the evidence text "
                + "always keep a hover surface, was " + share);

        foreach (string binding in new[] { "{Binding Title}", "{Binding Artist}" })
        {
            XElement line = titles.Elements(Presentation + "TextBlock")
                .Single(e => (string?)e.Attribute("Text") == binding);
            Assert.AreEqual(
                "CharacterEllipsis",
                line.Attribute("TextTrimming")?.Value,
                binding + " is bounded now, so it must trim rather than clip");
            Assert.AreEqual("NoWrap", line.Attribute("TextWrapping")?.Value, binding);
            Assert.AreEqual(
                binding,
                line.Attribute("ToolTip")?.Value,
                "CLAUDE.md allows trimming only when the full value stays available in a tooltip");
        }
    }
}
