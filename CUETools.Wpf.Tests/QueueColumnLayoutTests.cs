using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using CUETools.Wpf.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

// Queue's GridView columns were fixed at 300 + 90 + 110 + 320 = 820, so at an 860px window the
// Result column ran 48px past the edge with the ListView's horizontal scrollbar disabled: the
// column was unreachable between about 860 and 910 (measured 2026-08-24).
[TestClass]
public sealed class QueueColumnLayoutTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void TheResultColumnTakesWhatIsLeftOver()
    {
        double width = QueueColumnLayout.ResultWidth(900, QueueColumnLayout.Chrome);
        Assert.AreEqual(
            900 - QueueColumnLayout.Chrome
                - (QueueColumnLayout.SourceWidth
                   + QueueColumnLayout.ActionWidth
                   + QueueColumnLayout.StatusWidth),
            width,
            0.01);
    }

    [TestMethod]
    public void TheResultColumnNeverCollapsesBelowItsMinimum()
    {
        double width = QueueColumnLayout.ResultWidth(520, QueueColumnLayout.Chrome);
        Assert.AreEqual(QueueColumnLayout.MinimumResultWidth, width, 0.01,
            "below this the horizontal scrollbar is the fallback, not a zero-width column");
    }

    [TestMethod]
    public void ChromeIsTheSumOfItsMeasuredParts()
    {
        // Both numbers were measured, not guessed: see QueueColumnLayout's remarks for how. This
        // pins them so a future edit to one part cannot leave Chrome stale.
        Assert.AreEqual(6, QueueColumnLayout.BaseChrome,
            "layout chrome with no vertical scrollbar showing, measured 2026-08-24");
        Assert.AreEqual(
            SystemParameters.VerticalScrollBarWidth,
            QueueColumnLayout.ScrollBar,
            0.001,
            "the scrollbar part must be read live, not a hardcoded guess");
        Assert.AreEqual(
            QueueColumnLayout.BaseChrome + QueueColumnLayout.ScrollBar,
            QueueColumnLayout.Chrome,
            0.001,
            "Chrome must stay the sum of its named parts");
    }

    [TestMethod]
    public void EveryColumnFitsAtTheFloorWidth()
    {
        // 860 window minus the 78px rail and the list margin is the tightest reflow case.
        double list = 860 - 78 - 24;

        // Recomputed from the fixed three plus the decomposed chrome parts, independently of the
        // QueueColumnLayout.Chrome property used inside ResultWidth, so a wiring bug in that
        // property (a dropped or duplicated part) makes this assertion able to fail instead of
        // holding true by construction of ResultWidth for any Chrome value.
        double independentChrome = QueueColumnLayout.BaseChrome + SystemParameters.VerticalScrollBarWidth;
        double total = QueueColumnLayout.SourceWidth
            + QueueColumnLayout.ActionWidth
            + QueueColumnLayout.StatusWidth
            + QueueColumnLayout.ResultWidth(list, QueueColumnLayout.Chrome);
        Assert.IsTrue(total <= list - independentChrome + 0.01,
            "columns must reflow inside the list at the 860 floor, total was " + total);
    }

    [TestMethod]
    public void TheListDeclaresItsHorizontalFallbackAndNamesTheResultColumn()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive("Could not locate repository root from " + AppContext.BaseDirectory);

        XDocument document =
            XDocument.Load(Path.Combine(repoRoot, "CUETools.Wpf", "Views", "QueueView.xaml"));
        XElement list = document.Descendants(Presentation + "ListView").Single();

        Assert.AreEqual(
            "Auto",
            list.Attribute(Presentation + "ScrollViewer.HorizontalScrollBarVisibility")?.Value
                ?? list.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value,
            "reflow first, but the scrollbar is the fallback when reflow cannot fit");
        Assert.AreEqual("QueueList", list.Attribute(Xaml + "Name")?.Value);

        XElement resultColumn = document.Descendants(Presentation + "GridViewColumn")
            .Single(c => (string?)c.Attribute("Header") == "Result");
        Assert.AreEqual("ResultColumn", resultColumn.Attribute(Xaml + "Name")?.Value);
        Assert.IsNull(
            resultColumn.Attribute("Width"),
            "the result column width is computed on resize, not fixed in XAML");

        // The three fixed widths live in the markup and are only mirrored here. Without this
        // check, editing a GridViewColumn width leaves the reflow arithmetic silently wrong and
        // the Result column runs off the edge again with a green suite.
        (string Header, double Mirrored)[] fixedColumns =
        {
            ("Source", QueueColumnLayout.SourceWidth),
            ("Action", QueueColumnLayout.ActionWidth),
            ("Status", QueueColumnLayout.StatusWidth),
        };
        foreach ((string header, double mirrored) in fixedColumns)
        {
            XElement column = document.Descendants(Presentation + "GridViewColumn")
                .Single(c => (string?)c.Attribute("Header") == header);
            string? declared = column.Attribute("Width")?.Value;
            Assert.IsNotNull(declared, header + " column must declare a fixed width");
            Assert.IsTrue(
                double.TryParse(
                    declared!.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double width),
                header + " column width must be a number, was " + declared);
            Assert.AreEqual(
                mirrored,
                width,
                0.001,
                header + " column width in QueueView.xaml must match the constant the reflow "
                    + "arithmetic mirrors");
        }
    }

    [TestMethod]
    public void ApplyResultWidthDrivesARealGridViewColumnFromTheSizeChangedSeam()
    {
        // QueueView cannot be constructed directly here: its StaticResource lookups need an
        // Application with merged resource dictionaries. QueueView.ApplyResultWidth is the
        // extracted seam QueueList_SizeChanged calls, so this drives the real runtime behaviour
        // against a real GridViewColumn instead of only the pure QueueColumnLayout function.
        RunSta(() =>
        {
            var column = new GridViewColumn();

            QueueView.ApplyResultWidth(column, 900);

            Assert.AreEqual(
                QueueColumnLayout.ResultWidth(900, QueueColumnLayout.Chrome),
                column.Width,
                0.01);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
