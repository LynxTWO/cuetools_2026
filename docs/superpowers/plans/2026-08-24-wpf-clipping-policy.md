# WPF Clipping Policy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the three confirmed clipping defects in the WPF head and pin the clipping policy with tests so it cannot silently regress.

**Architecture:** Four small, independent layout fixes plus two test suites. Each fix is local to one control or one code-behind file; none change the shared `RailBreakpointValues` in App.Core, so the Linux head is untouched. Tests follow the existing house pattern of loading the `.xaml` with `XDocument` and asserting structure (see `ResponsiveRipLayoutTests.cs`), plus pure unit tests for arithmetic.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, MSTest v2, `System.Xml.Linq` for XAML assertions.

**Spec:** `docs/superpowers/specs/2026-08-24-wpf-clipping-policy-design.md`

## Global Constraints

- Do not change `CUETools.App.Core/Theme/RailIconPaths.cs` or `RailBreakpointValues`. Those are shared with the Linux head; changing them moves both heads at once (CLAUDE.md).
- The strip icon contract stays 44x38.
- Human-facing text: ASCII only. No em dashes, en dashes, arrows, checkmarks or curly quotes. Use `" - "`, `->`, `x`, `<=`, `...`.
- Commit messages: brief and human. No `Co-Authored-By` trailer, no generated-with footer.
- `CUETools.Wpf.Tests` must stay green. Baseline before this plan: **730 passing**.
- Run tests with: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet`
- The new `internal` types are visible to the tests already: `CUETools.Wpf/Properties/AssemblyInfo.cs`
  carries `[assembly: InternalsVisibleTo("CUETools.Wpf.Tests")]`. No project change is needed.
- `CUETools.App.Core/Theme/RailIconPaths.cs` uses namespace `CUETools.Wpf.Theme`, so
  `RailColumnWidths` joins that namespace from the `CUETools.Wpf` assembly. Different assemblies,
  different type names, no conflict.

## Deviations from the approved spec

Both were found while reading the code to write this plan. They are corrections, and they need the owner's nod before Task 4 runs.

1. **Spec change 4 said "wrap the root Grid of ConvertView and QueueView in a ScrollViewer".** That would be a bug. Both views are `Auto / * / Auto` grids with a fixed bottom bar (Queue's progress bar, Convert's status bar). Wrapping the root collapses the `*` row and makes the bottom bar scroll away. Corrected: wrap **only ConvertView's `Grid.Row="1"` body**, and leave QueueView alone vertically because its middle row is a `ListView`, which already scrolls.
2. **Spec testing said "drive every registered page at 640x480 and 1200x480".** That needs App's full DI graph (about 40 services) inside a unit test, which is fragile and slow. Corrected: assert the policy structurally from the XAML, in the established house style, plus pure unit tests for the two arithmetic fixes. This is what actually regresses and it is deterministic.

## File Structure

| File | Responsibility |
| --- | --- |
| `CUETools.Wpf/Theme/RailColumnWidths.cs` (create) | The rail column arithmetic, documented so nobody shrinks it back |
| `CUETools.Wpf/MainWindow.xaml.cs` (modify) | `ApplyRailLayout` consumes the constants |
| `CUETools.Wpf/MainWindow.xaml` (modify) | `StripNavItem` pins the icon left so it cannot shift |
| `CUETools.Wpf/Views/RipView.xaml` (modify) | History row: timestamp survives, evidence trims with a tooltip |
| `CUETools.Wpf/Views/QueueColumnLayout.cs` (create) | Pure width arithmetic for the Queue's last column |
| `CUETools.Wpf/Views/QueueView.xaml` (modify) | Named list and result column, horizontal fallback enabled |
| `CUETools.Wpf/Views/QueueView.xaml.cs` (modify) | Applies the computed width on resize |
| `CUETools.Wpf/Views/ConvertView.xaml` (modify) | Body scroller that keeps centring when there is room |
| `CUETools.Wpf.Tests/RailColumnWidthTests.cs` (create) | Pins the strip arithmetic and the icon pinning |
| `CUETools.Wpf.Tests/QueueColumnLayoutTests.cs` (create) | Pins the column arithmetic and the XAML wiring |
| `CUETools.Wpf.Tests/PageScrollPolicyTests.cs` (create) | Every page view must declare a vertical scroll affordance |

---

### Task 1: Rail strip column stops clipping its icons

**Files:**
- Create: `CUETools.Wpf/Theme/RailColumnWidths.cs`
- Create: `CUETools.Wpf.Tests/RailColumnWidthTests.cs`
- Modify: `CUETools.Wpf/MainWindow.xaml.cs:74`
- Modify: `CUETools.Wpf/MainWindow.xaml:52-56` (`StripNavItem` style)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `CUETools.Wpf.Theme.RailColumnWidths` with `public const double IconButton, ListPadding, ScrollBar, Border, Strip, Full`. `Strip` is 78, `Full` is 214.

- [ ] **Step 1: Write the failing test**

Create `CUETools.Wpf.Tests/RailColumnWidthTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CUETools.Wpf.Theme;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

// The strip column is not decorative. At 56 the vertical scrollbar that appears when the rail
// overflows left 22px of content for a 44px icon button, so the icons rendered at half width
// (measured 2026-08-24, window 640x480). These pin the arithmetic and the fix.
[TestClass]
public sealed class RailColumnWidthTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void TheStripColumnHoldsTheIconPaddingScrollbarAndBorder()
    {
        Assert.AreEqual(44, RailColumnWidths.IconButton, "the 44x38 strip icon contract");
        Assert.AreEqual(
            RailColumnWidths.IconButton
                + RailColumnWidths.ListPadding
                + RailColumnWidths.ScrollBar
                + RailColumnWidths.Border,
            RailColumnWidths.Strip,
            "the strip column must be the sum of what it has to hold");
        Assert.AreEqual(78, RailColumnWidths.Strip);
        Assert.AreEqual(214, RailColumnWidths.Full);
    }

    [TestMethod]
    public void TheStripColumnLeavesAWholeIconAfterTheScrollbar()
    {
        double content =
            RailColumnWidths.Strip - RailColumnWidths.Border - RailColumnWidths.ListPadding;
        Assert.IsTrue(
            content - RailColumnWidths.ScrollBar >= RailColumnWidths.IconButton,
            "a scrolling rail must still draw a whole 44px icon, not a clipped one");
    }

    [TestMethod]
    public void TheStripItemPinsTheIconLeftSoItDoesNotShift()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive("Could not locate repository root from " + AppContext.BaseDirectory);

        XDocument document =
            XDocument.Load(Path.Combine(repoRoot, "CUETools.Wpf", "MainWindow.xaml"));
        XElement style = document.Descendants(Presentation + "Style")
            .Single(e => (string?)e.Attribute(Xaml + "Key") == "StripNavItem");
        XElement? alignment = style.Elements(Presentation + "Setter")
            .FirstOrDefault(e => (string?)e.Attribute("Property") == "HorizontalAlignment");

        Assert.IsNotNull(alignment, "StripNavItem must set HorizontalAlignment");
        Assert.AreEqual(
            "Left",
            alignment!.Attribute("Value")?.Value,
            "Centring the icon moves it about 8px when the scrollbar appears; pin it left.");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet --filter "FullyQualifiedName~RailColumnWidthTests"`

Expected: FAIL to compile with `CS0246: The type or namespace name 'RailColumnWidths' could not be found`.

- [ ] **Step 3: Create the constants**

Create `CUETools.Wpf/Theme/RailColumnWidths.cs`:

```csharp
namespace CUETools.Wpf.Theme;

/// <summary>
/// Widths for the two rail states. The strip number is arithmetic, not taste: the column has to
/// hold the 44px icon button, the ListBox padding, the vertical scrollbar that appears whenever
/// the rail overflows, and the panel border. At 56 the scrollbar left 22px of content for a 44px
/// button, so every icon rendered at roughly half width once the window was shorter than about
/// 600px (measured 2026-08-24 at 640x480). Do not shrink this back without redoing that sum.
/// </summary>
internal static class RailColumnWidths
{
    /// <summary>The strip icon contract in RailIconPaths, shared with the Linux head.</summary>
    public const double IconButton = 44;

    /// <summary>NavList Padding="8,8", so 8 left plus 8 right.</summary>
    public const double ListPadding = 16;

    /// <summary>SystemParameters.VerticalScrollBarWidth at 96 dpi.</summary>
    public const double ScrollBar = 17;

    /// <summary>The rail Border's BorderThickness="0,0,1,0".</summary>
    public const double Border = 1;

    public const double Strip = IconButton + ListPadding + ScrollBar + Border;
    public const double Full = 214;
}
```

- [ ] **Step 4: Use the constants in ApplyRailLayout**

In `CUETools.Wpf/MainWindow.xaml.cs`, add `using CUETools.Wpf.Theme;` if absent, then replace line 74:

```csharp
        RailColumn.Width = new GridLength(compact ? 56 : 214);
```

with:

```csharp
        RailColumn.Width =
            new GridLength(compact ? RailColumnWidths.Strip : RailColumnWidths.Full);
```

- [ ] **Step 5: Pin the strip icon left**

In `CUETools.Wpf/MainWindow.xaml`, in the `StripNavItem` style, change:

```xml
      <Setter Property="HorizontalAlignment" Value="Center"/>
```

to:

```xml
      <!-- Left, not Center: the rail's scrollbar appears and disappears with window height, and a
           centred icon jumps about 8px when it does. -->
      <Setter Property="HorizontalAlignment" Value="Left"/>
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet --filter "FullyQualifiedName~RailColumnWidthTests"`

Expected: PASS, 3 tests.

- [ ] **Step 7: Commit**

```bash
git add CUETools.Wpf/Theme/RailColumnWidths.cs CUETools.Wpf.Tests/RailColumnWidthTests.cs CUETools.Wpf/MainWindow.xaml.cs CUETools.Wpf/MainWindow.xaml
git commit -m "Widen the rail strip so its scrollbar stops clipping the icons"
```

---

### Task 2: Rip history rows keep the timestamp and trim the evidence

**Files:**
- Modify: `CUETools.Wpf/Views/RipView.xaml:526-533`
- Create: `CUETools.Wpf.Tests/RipHistoryRowTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the failing test**

Create `CUETools.Wpf.Tests/RipHistoryRowTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet --filter "FullyQualifiedName~RipHistoryRowTests"`

Expected: FAIL. `TheTimestampIsReservedBeforeTheEvidenceText` fails on `LastChildFill` being `False`; `TheEvidenceTextTrimsAndKeepsItsFullValueInATooltip` fails with `TextTrimming` null.

- [ ] **Step 3: Rewrite the row**

In `CUETools.Wpf/Views/RipView.xaml`, replace the `DockPanel` block (currently lines 526-533):

```xml
                  <DockPanel LastChildFill="False">
                    <StackPanel DockPanel.Dock="Left">
                      <TextBlock Text="{Binding Title}" FontFamily="{StaticResource Serif}" FontSize="14" Foreground="{DynamicResource Ink}"/>
                      <TextBlock Text="{Binding Artist}" FontFamily="{StaticResource Serif}" FontStyle="Italic" FontSize="11.5" Foreground="{DynamicResource Muted}"/>
                    </StackPanel>
                    <TextBlock DockPanel.Dock="Right" Text="{Binding Result}" Foreground="{StaticResource Teal}" FontFamily="{StaticResource Mono}" FontSize="11.5" VerticalAlignment="Center"/>
                    <TextBlock DockPanel.Dock="Right" Text="{Binding When}" Foreground="{DynamicResource Muted}" FontFamily="{StaticResource Mono}" FontSize="11" VerticalAlignment="Center" Margin="0,0,14,0"/>
                  </DockPanel>
```

with:

```xml
                  <!-- Order matters: DockPanel reserves space in declaration order. When docks
                       first so the timestamp always shows, then Result fills the leftover middle
                       and trims there, keeping its full value in a tooltip. -->
                  <DockPanel LastChildFill="True">
                    <StackPanel DockPanel.Dock="Left">
                      <TextBlock Text="{Binding Title}" FontFamily="{StaticResource Serif}" FontSize="14" Foreground="{DynamicResource Ink}"/>
                      <TextBlock Text="{Binding Artist}" FontFamily="{StaticResource Serif}" FontStyle="Italic" FontSize="11.5" Foreground="{DynamicResource Muted}"/>
                    </StackPanel>
                    <TextBlock DockPanel.Dock="Right" Text="{Binding When}" Foreground="{DynamicResource Muted}" FontFamily="{StaticResource Mono}" FontSize="11" VerticalAlignment="Center" Margin="14,0,0,0"/>
                    <TextBlock Text="{Binding Result}" Foreground="{StaticResource Teal}" FontFamily="{StaticResource Mono}" FontSize="11.5" VerticalAlignment="Center" Margin="14,0,0,0"
                               TextTrimming="CharacterEllipsis" TextWrapping="NoWrap" ToolTip="{Binding Result}"/>
                  </DockPanel>
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet --filter "FullyQualifiedName~RipHistoryRowTests"`

Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add CUETools.Wpf/Views/RipView.xaml CUETools.Wpf.Tests/RipHistoryRowTests.cs
git commit -m "Rip history rows keep the timestamp and trim the evidence text"
```

---

### Task 3: Queue's result column reflows instead of running off

**Files:**
- Create: `CUETools.Wpf/Views/QueueColumnLayout.cs`
- Create: `CUETools.Wpf.Tests/QueueColumnLayoutTests.cs`
- Modify: `CUETools.Wpf/Views/QueueView.xaml:38-48`
- Modify: `CUETools.Wpf/Views/QueueView.xaml.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `CUETools.Wpf.Views.QueueColumnLayout.ResultWidth(double listWidth, double chrome)` returning `double`, plus `public const double SourceWidth, ActionWidth, StatusWidth, MinimumResultWidth, Chrome`.

- [ ] **Step 1: Write the failing test**

Create `CUETools.Wpf.Tests/QueueColumnLayoutTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
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
    public void EveryColumnFitsAtTheFloorWidth()
    {
        // 860 window minus the 78px rail and the list margin is the tightest reflow case.
        double list = 860 - 78 - 24;
        double total = QueueColumnLayout.SourceWidth
            + QueueColumnLayout.ActionWidth
            + QueueColumnLayout.StatusWidth
            + QueueColumnLayout.ResultWidth(list, QueueColumnLayout.Chrome);
        Assert.IsTrue(total <= list - QueueColumnLayout.Chrome + 0.01,
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
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet --filter "FullyQualifiedName~QueueColumnLayoutTests"`

Expected: FAIL to compile with `CS0103: The name 'QueueColumnLayout' does not exist`.

- [ ] **Step 3: Create the arithmetic**

Create `CUETools.Wpf/Views/QueueColumnLayout.cs`:

```csharp
using System;

namespace CUETools.Wpf.Views;

/// <summary>
/// Width arithmetic for the queue's GridView. GridViewColumn has no star sizing, so the last
/// column is computed on resize instead. Kept as a pure function so the reflow can be tested
/// without a window. The fixed three are unchanged from the original layout.
/// </summary>
internal static class QueueColumnLayout
{
    public const double SourceWidth = 300;
    public const double ActionWidth = 90;
    public const double StatusWidth = 110;

    /// <summary>Below this the horizontal scrollbar takes over rather than the column vanishing.</summary>
    public const double MinimumResultWidth = 120;

    /// <summary>GridView row padding plus the vertical scrollbar the list shows when it fills.</summary>
    public const double Chrome = 26;

    public static double ResultWidth(double listWidth, double chrome)
    {
        double leftOver =
            listWidth - chrome - (SourceWidth + ActionWidth + StatusWidth);
        return leftOver < MinimumResultWidth ? MinimumResultWidth : leftOver;
    }
}
```

- [ ] **Step 4: Wire the XAML**

In `CUETools.Wpf/Views/QueueView.xaml`, replace the `ListView` block:

```xml
      <ListView Margin="12" BorderThickness="0" Background="Transparent" ItemsSource="{Binding Items}"
                ScrollViewer.HorizontalScrollBarVisibility="Disabled">
        <ListView.View>
          <GridView>
            <GridViewColumn Header="Source" Width="300" DisplayMemberBinding="{Binding Display}"/>
            <GridViewColumn Header="Action" Width="90" DisplayMemberBinding="{Binding Action}"/>
            <GridViewColumn Header="Status" Width="110" DisplayMemberBinding="{Binding Status}"/>
            <GridViewColumn Header="Result" Width="320" DisplayMemberBinding="{Binding Result}"/>
          </GridView>
        </ListView.View>
      </ListView>
```

with:

```xml
      <!-- Result takes the leftover width so the columns reflow down to the 860 floor. The
           horizontal scrollbar is the fallback for when even that is not enough. -->
      <ListView x:Name="QueueList" Margin="12" BorderThickness="0" Background="Transparent"
                ItemsSource="{Binding Items}" SizeChanged="QueueList_SizeChanged"
                ScrollViewer.HorizontalScrollBarVisibility="Auto">
        <ListView.View>
          <GridView>
            <GridViewColumn Header="Source" Width="300" DisplayMemberBinding="{Binding Display}"/>
            <GridViewColumn Header="Action" Width="90" DisplayMemberBinding="{Binding Action}"/>
            <GridViewColumn Header="Status" Width="110" DisplayMemberBinding="{Binding Status}"/>
            <GridViewColumn x:Name="ResultColumn" Header="Result" DisplayMemberBinding="{Binding Result}"/>
          </GridView>
        </ListView.View>
      </ListView>
```

- [ ] **Step 5: Apply the width on resize**

In `CUETools.Wpf/Views/QueueView.xaml.cs`, add `using System.Windows;` if absent and add this handler inside the `QueueView` class:

```csharp
    // GridViewColumn has no star sizing, so the last column is measured here instead. See
    // QueueColumnLayout for the arithmetic and why it is a pure function.
    private void QueueList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;
        ResultColumn.Width =
            QueueColumnLayout.ResultWidth(e.NewSize.Width, QueueColumnLayout.Chrome);
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet --filter "FullyQualifiedName~QueueColumnLayoutTests"`

Expected: PASS, 4 tests.

- [ ] **Step 7: Commit**

```bash
git add CUETools.Wpf/Views/QueueColumnLayout.cs CUETools.Wpf/Views/QueueView.xaml CUETools.Wpf/Views/QueueView.xaml.cs CUETools.Wpf.Tests/QueueColumnLayoutTests.cs
git commit -m "Queue's result column reflows instead of running off the edge"
```

---

### Task 4: Convert's body scrolls without losing its status bar

Do not start this task until the owner has confirmed deviation 1 in the header.

**Files:**
- Modify: `CUETools.Wpf/Views/ConvertView.xaml` (the `Grid.Row="1"` element)
- Create: `CUETools.Wpf.Tests/ConvertBodyScrollTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: a `ScrollViewer` named `ConvertBodyScroller` in `ConvertView.xaml`, relied on by Task 5's policy table.

- [ ] **Step 1: Write the failing test**

Create `CUETools.Wpf.Tests/ConvertBodyScrollTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet --filter "FullyQualifiedName~ConvertBodyScrollTests"`

Expected: FAIL with `Sequence contains no matching element` on the `ConvertBodyScroller` lookup.

- [ ] **Step 3: Wrap only the body**

In `CUETools.Wpf/Views/ConvertView.xaml`, change the opening of the middle row from:

```xml
    <Grid Grid.Row="1">
```

to:

```xml
    <!-- Only the body scrolls. The row above and the status bar below are fixed, so the page
         root must stay a grid. MinHeight keeps the centred states centred while there is room. -->
    <ScrollViewer Grid.Row="1" x:Name="ConvertBodyScroller" VerticalScrollBarVisibility="Auto">
    <Grid MinHeight="{Binding ActualHeight, ElementName=ConvertBodyScroller}">
```

and close it by changing that grid's matching `</Grid>` (the one immediately before `<!-- progress bar -->` or the `Grid.Row="2"` Border) to:

```xml
    </Grid>
    </ScrollViewer>
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet --filter "FullyQualifiedName~ConvertBodyScrollTests"`

Expected: PASS, 1 test.

- [ ] **Step 5: Commit**

```bash
git add CUETools.Wpf/Views/ConvertView.xaml CUETools.Wpf.Tests/ConvertBodyScrollTests.cs
git commit -m "Convert's body scrolls while its status bar stays put"
```

---

### Task 5: Every page must declare a vertical scroll affordance

**Files:**
- Create: `CUETools.Wpf.Tests/PageScrollPolicyTests.cs`

**Interfaces:**
- Consumes: Task 4's body scroller in `ConvertView.xaml`. It does not reference the name, only that
  the view now contains a vertical ScrollViewer, so this task fails until Task 4 lands.
- Produces: nothing.

- [ ] **Step 1: Write the test**

Create `CUETools.Wpf.Tests/PageScrollPolicyTests.cs`:

```csharp
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
                .Any(e => (e.Attribute("VerticalScrollBarVisibility")?.Value ?? "Auto") != "Disabled");
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
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet --filter "FullyQualifiedName~PageScrollPolicyTests"`

Expected: PASS, 2 tests. If `EveryPageViewCanReachContentThatDoesNotFit` fails on `ConvertView.xaml`, Task 4 has not been done.

- [ ] **Step 3: Commit**

```bash
git add CUETools.Wpf.Tests/PageScrollPolicyTests.cs
git commit -m "Guard the clipping policy: every page must be able to reach its content"
```

---

### Task 6: Refresh the evidence and close D13

**Files:**
- Modify: `docs/review/decisions-needed.md` (the D13 entry)
- Modify: `docs/evidence/2026-08-24-wpf-scaling-port/index.md`
- Replace: the `1100`, `0800` and `0640` captures in `docs/evidence/2026-08-24-wpf-scaling-port/`

**Interfaces:**
- Consumes: all earlier tasks.
- Produces: nothing.

- [ ] **Step 1: Verify the whole suite is green**

Run: `dotnet test .\CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj -c Release --nologo -v quiet`

Expected: PASS. 730 before this plan, plus 12 new tests = **742**.

- [ ] **Step 2: Recapture the affected states**

The strip width changed, so every capture below 1140 is stale. Rerun the sweep used on 2026-08-24:

```powershell
& "J:\TEMP\claude\c--DEV-cuetools-2026\ff9c0552-6301-4164-bbe1-3fe136492b76\scratchpad\Run-ScalingSweep.ps1"
```

Copy the regenerated `1100`, `0800` and `0640` files over the ones in
`docs/evidence/2026-08-24-wpf-scaling-port/`. The `1200` captures are above the breakpoint and do not change.

- [ ] **Step 3: Update the evidence index**

In `docs/evidence/2026-08-24-wpf-scaling-port/index.md`, update the held-layout arithmetic. The rail is now 78, so replace the `292px` figure and its `56 + 860 - 624` derivation with `314px` and `78 + 860 - 624`. Add one line to the results table noting the history rows now trim with a tooltip and the timestamp renders.

- [ ] **Step 4: Close D13**

In `docs/review/decisions-needed.md`, change the D13 heading to
`### D13. Rip page history rows starve the timestamp and hard-clip the result - RESOLVED 2026-08-24`
and add a short resolution paragraph naming the fix (When docks before Result, Result fills and
trims with a tooltip) and the test that pins it (`RipHistoryRowTests`). Move it out of
`## Open decisions` into `## Resolved / actioned`, keeping the original record beneath as the other
entries in that file do.

- [ ] **Step 5: Verify no typographic characters crept in**

```bash
python -c "
import unicodedata
BAD={'\u2014','\u2013','\u2192','\u2713','\u2717','\u2212','\u2018','\u2019','\u201c','\u201d','\u2026'}
for f in ['docs/review/decisions-needed.md','docs/evidence/2026-08-24-wpf-scaling-port/index.md']:
    hits=[(i,unicodedata.name(c,'?')) for i,l in enumerate(open(f,encoding='utf-8'),1) for c in l if c in BAD]
    print(('CLEAN ' if not hits else 'BAD   ')+f, hits[:5])
"
```

Expected: `CLEAN` for both.

- [ ] **Step 6: Commit**

```bash
git add docs/review/decisions-needed.md docs/evidence/2026-08-24-wpf-scaling-port
git commit -m "Refresh the scaling evidence for the wider rail and close D13"
```
