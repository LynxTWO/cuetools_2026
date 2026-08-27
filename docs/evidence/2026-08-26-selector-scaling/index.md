# Selector windows at 100 to 200 percent DPI, 2026-08-26

The artwork browser and the codec picker, captured on screen at five Windows display scales in
both themes: 20 captures. This closes the DPI half of the R72/R73 release evidence gap; the
high-contrast half is still open.

## Host and method

| Item | Value |
| --- | --- |
| Machine | DESKTOP-D084LOM, AMD Ryzen 9 5950X 16-Core |
| OS | Microsoft Windows 11 Pro Insider Preview, 10.0.26220.0 |
| Display | single monitor, 2560x1440, originally 100 percent (96 dpi) |
| Build | `CUETools.Wpf` and `CUETools.Wpf.Tests`, Release, net8.0-windows, .NET SDK 8.0.422 |
| Driver | `eng/evidence/Run-SelectorSweep.ps1` |

The windows are shown by `SelectorCaptureTests` in `CUETools.Wpf.Tests`, a gated on-screen
capture that runs only when `CUETOOLS_SELECTOR_CAPTURE_DIR` is set (Inconclusive otherwise, so the
ordinary suite never opens a window). It builds one WPF `Application` with `Theme.xaml` merged,
swaps the palette per theme through `ThemeService`, shows each window against synthetic data,
waits for the thumbnails to decode, and reads the presented pixels off the screen through a GDI
`BitBlt` of the window's physical rectangle. No disc, no network, no service graph: the browser is
constructed through the new `IArtworkBrowserHost` seam with six synthetic candidates spanning
every provider, tier, and approval state, and a stub `IAlbumArtService` that renders labelled
gradient JPEGs; the picker gets eight `CodecChoice` rows across Ready, Setup required, and Load
failed.

The sweep drives the display scale live through `SPI_SETLOGICALDPIOVERRIDE`, restores the
original index on every exit path, and removes the `PerMonitorSettings` registry key the override
creates when it did not exist beforehand. **Verified after the run:** 96 dpi, key absent.

The scale in each filename is measured in-process from `VisualTreeHelper.GetDpi`, not copied
from the sweep's request.

## File naming

`<scale>pct-<theme>-<window>.png`, for example `200pct-light-codec-picker.png`. The browser is
1040x700 logical, the size the earlier 96 dpi captures used; the picker takes its own size.

## Results

| Check | Result |
| --- | --- |
| Text renders sharp at every scale | yes - glyph edges are crisp at 200 percent, so the process is DPI-aware rather than bitmap-scaled by DWM |
| Palette resolves in both themes | yes - grid body, cells, selection, headers, and detail pane all follow the swapped palette; no default-white surfaces |
| Thumbnails present, not placeholders | yes - all five front-art rows show decoded previews at every scale |
| Row filter | correct - the synthetic Back cover stays hidden until All artwork is ticked |
| Codec picker readiness states | correct - Ready, Setup required, and Load failed rows are visibly distinct, unavailable rows stay listed and unselectable |
| Nothing clips | **one defect found and fixed, see below**; nothing else clips at any scale |

At 200 percent the browser's 1040x700 logical window is 2080x1400 physical, which fits the
2560x1440 desktop with the window at (40, 40); every action button stays on screen.

## Finding, fixed in the same change

The browser's "Why this matched" column is the star column, and the match reason is a full
sentence. At the 1040 default it was cut mid-word at the grid edge with no ellipsis and no
tooltip - visible in the first 100 percent capture before the fix. The clipping policy in
CLAUDE.md allows trimming only with the full value in a tooltip, so the column now has an
`ElementStyle` with `TextTrimming="CharacterEllipsis"` and a `ToolTip` bound to the same value,
and `ArtworkBrowserLayoutTests` pins it. Every capture in this folder shows the fixed column.

## Still open

Windows high-contrast captures of both windows. The app has no high-contrast handling at all
(no `SystemParameters.HighContrast` references), so those captures will show the custom palette
ignoring the system scheme; whether that is acceptable is a decision for the owner, and toggling
high contrast repaints every window on the workstation while it runs.
