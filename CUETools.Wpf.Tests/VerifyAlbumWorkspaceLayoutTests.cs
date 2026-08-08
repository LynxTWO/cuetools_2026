using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class VerifyAlbumWorkspaceLayoutTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void VerifyPageAcceptsFileDropsAndProvidesFolderSelection()
    {
        XDocument view = Load("Views", "VerifyView.xaml");
        Assert.AreEqual("True", view.Root!.Attribute("AllowDrop")?.Value);
        Assert.AreEqual("Verify_DragOver", view.Root.Attribute("PreviewDragOver")?.Value);
        Assert.AreEqual("Verify_Drop", view.Root.Attribute("Drop")?.Value);
        Assert.IsNotNull(Named(view, "VerifyDropTarget"));

        string[] actions = view.Descendants(Presentation + "Button")
            .Select(button => button.Attribute("Content")?.Value)
            .Where(value => value != null)
            .Cast<string>()
            .ToArray();
        CollectionAssert.IsSubsetOf(
            new[] { "File...", "Folder...", "Verify album", "Stop after disc", "Repair this disc" },
            actions);
    }

    [TestMethod]
    public void VerifyWorkspaceHasCompactWidthFallbackAndRichEvidenceColumns()
    {
        XDocument view = Load("Views", "VerifyView.xaml");
        XElement work = Named(view, "VerifyWorkScroller");
        Assert.AreEqual("Auto", work.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.AreEqual("Stretch", work.Attribute("HorizontalContentAlignment")?.Value);
        XElement stack = work.Elements(Presentation + "StackPanel").Single();
        Assert.AreEqual("720", stack.Attribute("MinWidth")?.Value);
        Assert.IsNull(stack.Attribute("Width"),
            "A viewport-width child plus margins guarantees scrollbar bleed.");

        XElement source = Named(view, "VerifySourceScroller");
        Assert.AreEqual("Stretch", source.Attribute("HorizontalContentAlignment")?.Value);
        Assert.IsNull(Named(view, "VerifySourceGrid").Attribute("Width"));

        string[] headers = view.Descendants(Presentation + "GridViewColumn")
            .Select(column => column.Attribute("Header")?.Value)
            .Where(value => value != null)
            .Cast<string>()
            .ToArray();
        CollectionAssert.IsSubsetOf(
            new[] { "#", "Title", "Artist", "CRC32", "AR conf", "CTDB conf" },
            headers);

        string xaml = view.ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(xaml, "OverviewHeadline");
        StringAssert.Contains(xaml, "LabelText");
        StringAssert.Contains(xaml, "BarcodeText");
        StringAssert.Contains(xaml, "TocText");
        StringAssert.Contains(xaml, "RepairHeadroom");
    }

    [TestMethod]
    public void VerifyOutcomeUsesThemeTokensForEveryState()
    {
        XDocument view = Load("Views", "VerifyView.xaml");
        string xaml = view.ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(xaml, "DynamicResource StatusGood");
        StringAssert.Contains(xaml, "DynamicResource StatusWarning");
        StringAssert.Contains(xaml, "DynamicResource StatusDanger");
        StringAssert.Contains(xaml, "DynamicResource StatusAccent");
        StringAssert.Contains(xaml, "DynamicResource Ink");
        StringAssert.Contains(xaml, "DynamicResource Muted");
    }

    [TestMethod]
    public void AppUsesThemeAwareFaderScrollbarsInsteadOfNativeChrome()
    {
        XDocument theme = Load("Theme", "Theme.xaml");
        XElement implicitStyle = theme.Descendants(Presentation + "Style").Single(style =>
            style.Attribute("TargetType")?.Value == "ScrollBar" &&
            style.Attribute(Xaml + "Key") == null);
        Assert.IsNotNull(implicitStyle);
        CollectionAssert.DoesNotContain(
            implicitStyle.Elements(Presentation + "Setter")
                .Select(setter => setter.Attribute("Property")?.Value)
                .Where(property => property is not null)
                .ToList(),
            "Height",
            "A fixed height would collapse vertical scrollbars instead of only sizing their rail.");
        CollectionAssert.DoesNotContain(
            implicitStyle.Elements(Presentation + "Setter")
                .Select(setter => setter.Attribute("Property")?.Value)
                .Where(property => property is not null)
                .ToList(),
            "Width",
            "A fixed width would collapse horizontal scrollbars instead of only sizing their rail.");

        string xaml = theme.ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(xaml, "VerticalScrollBarTemplate");
        StringAssert.Contains(xaml, "HorizontalScrollBarTemplate");
        StringAssert.Contains(xaml, "ScrollThumbHover");
        StringAssert.Contains(xaml, "ScrollBar.PageUpCommand");
        StringAssert.Contains(xaml, "ScrollBar.PageRightCommand");
    }

    private static XDocument Load(string directory, string file)
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(root))
            Assert.Inconclusive("Could not locate repository root.");
        return XDocument.Load(Path.Combine(root, "CUETools.Wpf", directory, file));
    }

    private static XElement Named(XDocument document, string name)
        => document.Descendants().Single(
            element => element.Attribute(Xaml + "Name")?.Value == name);
}
