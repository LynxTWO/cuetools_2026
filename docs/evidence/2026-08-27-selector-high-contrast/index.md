# Selector windows under Windows high contrast, 2026-08-27

The artwork browser and the codec picker captured on screen with Windows high contrast on
("High Contrast Black"), in both app themes: four captures. This closes the last half of the
R72/R73 release evidence gap and raises decision D14.

## Host and method

| Item | Value |
| --- | --- |
| Machine | DESKTOP-D084LOM, AMD Ryzen 9 5950X 16-Core |
| OS | Microsoft Windows 11 Pro Insider Preview, 10.0.26220.0 |
| Display | single monitor, 2560x1440, 100 percent (96 dpi) |
| Build | `CUETools.Wpf` and `CUETools.Wpf.Tests`, Release, net8.0-windows, .NET SDK 8.0.422 |
| Driver | `eng/evidence/Run-SelectorHighContrast.ps1` |

Same capture path as the DPI sweep: the gated `SelectorCaptureTests` shows each window against
synthetic data and reads the presented pixels through a GDI `BitBlt`. The script reads the
original high-contrast flags and scheme first (`flags=0x7E`, off, no scheme), turns high contrast
on through `SPI_SETHIGHCONTRAST`, waits for the session to repaint, runs the capture, and restores
the original flags on every exit path. **Verified after the run, independently of the script:**
`flags=0x7E`, off. One residual: Windows keeps the last scheme name in the preference field even
when high contrast is off, so that field now reads "High Contrast Black" where it was empty. That
is a label, not a state change - high contrast is off.

## File naming

`100pct-<theme>-<window>.png`. The scale in the name is measured in-process, and the sweep ran at
the workstation's normal 96 dpi.

## Results

The app has no high-contrast handling - there is no `SystemParameters.HighContrast` reference
anywhere in `CUETools.Wpf` or `CUETools.App.Core` - so what these captures show is what happens
by default.

| Check | Result |
| --- | --- |
| Does the app palette follow the system scheme? | **No.** Both windows keep their own dark or light palette; only the OS window chrome (title bar, caption buttons) goes high-contrast yellow on black. |
| Artwork browser legibility | Holds. The `DataGridCell` and `TextBlock` styles override every system colour, so headers, cells, thumbnails, and the selected row look exactly as they do without high contrast. |
| Codec picker legibility | **Mixed.** The `ListView` selection half-adopts the system highlight: the selected row's background becomes system cyan while its text keeps the palette colour. In the dark theme that is white on cyan, well below a readable contrast ratio; in the light theme it is black on cyan and readable. |
| Unavailable codec rows | Keep the dimmed palette grey ("Setup required", "Load failed"), which is lower contrast than a high-contrast scheme intends. |
| System-templated controls | The `CheckBox` and `ComboBox` pick up high-contrast styling (black-bordered, black fill when checked) inside an otherwise palette-coloured window, so the mix is visible but functional. |

## What this means

The question is not whether something is broken - the browser is fine and the picker is
readable in the light theme - but whether the app should honour the user's high-contrast setting
at all. That is decision **D14** in `docs/review/decisions-needed.md`: adopt `SystemColors`
under high contrast, or keep the custom palette on purpose and fix only the picker's mixed
selection so its text and background come from the same source. Nothing was changed for this
capture; it records what ships today.
