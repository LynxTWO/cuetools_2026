using System;
using System.IO;
using System.Linq;
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

        Assert.AreEqual(
            "Right",
            when.Attribute(Presentation + "DockPanel.Dock")?.Value
                ?? when.Attribute("DockPanel.Dock")?.Value,
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
}
