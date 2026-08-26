using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

// The clipping policy: page content may scroll, but it may never be unreachable. This is the
// guard for the eleventh page - a new view fails here until someone decides how it scrolls.
[TestClass]
public sealed class PageScrollPolicyTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    // Every page view, and how it lets the user reach content that does not fit.
    private static readonly string[] KnownPageViews =
    {
        "AdvancedView.xaml",
        "ConvertView.xaml",
        "DriveView.xaml",
        "ExploreView.xaml",
        "NamingView.xaml",
        "QueueView.xaml",
        "ReportView.xaml",
        "RipView.xaml",
        "SettingsView.xaml",
        "VerifyView.xaml",
    };

    private static string ViewsDirectory()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive("Could not locate repository root from " + AppContext.BaseDirectory);
        return Path.Combine(repoRoot, "CUETools.Wpf", "Views");
    }

    [TestMethod]
    public void TheListOfPageViewsIsComplete()
    {
        string[] actual = Directory
            .GetFiles(ViewsDirectory(), "*View.xaml")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;

        CollectionAssert.AreEqual(
            KnownPageViews.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            actual,
            "A page view was added or removed. Add it here and give it a scroll affordance.");
    }

    [TestMethod]
    public void EveryPageViewCanReachContentThatDoesNotFit()
    {
        foreach (string view in KnownPageViews)
        {
            XDocument document = XDocument.Load(Path.Combine(ViewsDirectory(), view));

            bool verticalScroller = document.Descendants(Presentation + "ScrollViewer")
                .Any(e =>
                    // WPF's real default for a missing attribute is Visible, not Auto; either
                    // way it is not Disabled, which is all this check needs.
                    (e.Attribute("VerticalScrollBarVisibility")?.Value ?? "Visible") != "Disabled");
            bool scrollingList =
                document.Descendants(Presentation + "ListView").Any() ||
                document.Descendants(Presentation + "ListBox").Any() ||
                document.Descendants(Presentation + "DataGrid").Any();

            Assert.IsTrue(
                verticalScroller || scrollingList,
                view + " has no way to reach content taller than the window. Give it a "
                    + "ScrollViewer with vertical scrolling, or an items control that scrolls.");
        }
    }
}
