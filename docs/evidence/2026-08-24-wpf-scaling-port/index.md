# WPF fractional-scaling port walkthrough, 2026-08-24

Evidence for the SLICE-013 Windows port (fork PR 72). 40 captures: five Windows display scales,
both themes, four window widths each.

**The port itself behaves as specified at every scale and width tested.** This walkthrough also
found one pre-existing defect on the Rip page that the port did not introduce; it is described at
the bottom. That defect has since been fixed (D13) and all 40 captures in this folder were
regenerated after the fix, so they show the corrected layout, not the original defect.

## Host and method

| Item | Value |
| --- | --- |
| Machine | DESKTOP-D084LOM, AMD Ryzen 9 5950X 16-Core |
| OS | Microsoft Windows 11 Pro Insider Preview, 10.0.26220.0 |
| Display | single monitor, 2560x1440, originally 100% scale (96 dpi) |
| Build | `CUETools.Wpf`, Release, net8.0-windows, .NET SDK 8.0.422 |
| Prerequisites | `Prepare-VendorSources.ps1` current (1556 files); `Test-VendorSubmodulesClean.ps1` PASS on 5 worktrees |
| Tests | `CUETools.Wpf.Tests` 730 passing, `RailStripPortTests` 3 of 3 |

Display scale was driven live through `SPI_SETLOGICALDPIOVERRIDE` and restored to the original
index on every exit path. **Verified after the run:** effective DPI back to 96, and the
`HKCU\Control Panel\Desktop\PerMonitorSettings` key the override created was removed, because it
did not exist beforehand. The workstation is byte-for-byte as it was found.

Captures read real pixels from the screen with the window forced foreground. `PrintWindow` was
tried first and returns a blank bitmap for this window, because WPF composites through
DirectComposition.

## What the breakpoints actually exercise

The breakpoints in `RailIconPaths` are **logical** pixels (`FullAt = 1140`, `FloorBelow = 860`,
`HeldLayoutWidth = 860`), so display scale does not move them. It changes how much logical width a
physical display offers:

| Scale | Logical width of this 2560px display |
| --- | --- |
| 100% | 2560 |
| 125% | 2048 |
| 150% | 1707 |
| 175% | 1463 |
| 200% | 1280 |

Every one of those is above 1140, so on this monitor a maximized window always shows the full card
rail. **The strip and the floor are reached by window width, not by scaling.** Each scale was
therefore walked at four widths: 1200 (the documented default, full rail), 1100 (icon strip), 800
(below the floor), and 640x480 (the window minimum).

## File naming

`<scale>pct-<theme>-<logical-width>-<state>.png`, for example `200pct-light-1100-icon-strip.png`.

## Results

| Check | Result |
| --- | --- |
| Full card rail at and above 1140 | correct at all 5 scales, both themes |
| Rail collapses to the 44x38 icon strip below 1140 | correct at all 5 scales, both themes |
| Glyphs legible at 44x38 | yes, and they sharpen with scale rather than blurring |
| Below 860: layout held at its 860 shape, horizontal scroll | correct, horizontal scrollbars appear and content is not squeezed; at 640x480 that is a uniform 314px of overflow (78 + 860 - 624), reachable by scrollbar |
| Window drags to 640x480 without breaking | yes; the rail strip gains a vertical scrollbar for overflow |
| Light/dark flip restyles the strip | correct at all 5 scales, no stranded brushes from the previous palette |
| Nothing clips | fails on the Rip page history rows, see below |
| Rip page history rows (D13, fixed after this walkthrough) | `When` now docks before `Result`; the timestamp renders and `Result` trims with a tooltip (`RipHistoryRowTests`) |

At 200%, the 1200 and 1100 captures are height-clamped to 1470 physical pixels by the desktop work
area. Width is what the breakpoints key off, so this does not affect the rail result.

## Finding: Rip page history rows starved the timestamp and hard-clipped the result - fixed (D13)

Found during this walkthrough. Not a scaling-port regression: it reproduced at every scale and
every width, including the 1200 default, and the port did not touch this template. Logged as D13
in `docs/review/decisions-needed.md` and left unfixed at the time the walkthrough was written up,
because the row layout is a Rip page design decision and this walkthrough was scoped to reviewing
the scaling port.

`CUETools.Wpf/Views/RipView.xaml:526-533` laid each history row out as:

```xml
<DockPanel LastChildFill="False">
  <StackPanel DockPanel.Dock="Left"> Title / Artist </StackPanel>
  <TextBlock DockPanel.Dock="Right" Text="{Binding Result}" .../>
  <TextBlock DockPanel.Dock="Right" Text="{Binding When}"   .../>
</DockPanel>
```

`DockPanel` reserves space in declaration order, so `Result` docked against the right edge first
and took everything left over. `Result` carried the full evidence sentence, which is far wider
than the row, so it consumed the entire remaining width and `When` was left zero width.

Two consequences, both **measured** across the original 40 captures:

1. **The relative timestamp never rendered.** `When` was always populated -
   `HistoryStore.cs:75` sets `When = Relative(r.When)` - but it did not appear in a single
   capture at any scale or width.
2. **`Result` was hard-clipped mid-word.** That `TextBlock` set no `TextTrimming` and no
   `ToolTip`, so the text was cut at the panel edge with no ellipsis and no way to read the rest.
   CLAUDE.md requires that long identity text be trimmed only with its full value in a tooltip.

The album title also abutted the result text with no gap once the row was narrow, which was the
same root cause.

**Fixed 2026-08-24.** `RipView.xaml` now docks `When` before `Result`: `When` docks Right and
always gets its width, and `Result` is the `LastChildFill` child, so it fills the remaining
middle and trims with `TextTrimming="CharacterEllipsis"` plus a `ToolTip` bound to the same
value. `RipHistoryRowTests` (`CUETools.Wpf.Tests`) pins both halves of the fix - the dock order
and the trim/tooltip pair. D13 is resolved with this fix; see
`docs/review/decisions-needed.md`. All 40 captures in this folder were regenerated after the fix
landed, so every capture here, including the 1200-width ones, shows the timestamp rendering and
the result trimming with an ellipsis rather than the original defect.
