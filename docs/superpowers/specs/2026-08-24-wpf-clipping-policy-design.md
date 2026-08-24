# WPF clipping policy and fixes, 2026-08-24

## Problem

The SLICE-013 scaling port walkthrough (`docs/evidence/2026-08-24-wpf-scaling-port/`) checked that
nothing clips and found that things do. The owner also reported sidebar clipping. This design
settles a policy for the whole app - what must never clip, what must reflow, and what may
legitimately ask the user to scroll - and fixes the three confirmed defects.

## Evidence

Measured 2026-08-24 on DESKTOP-D084LOM, Windows 11 Pro 10.0.26220.0, `CUETools.Wpf` Release
net8.0-windows, by driving the real app with UI Automation and reading pixels off screen. Four
probes were used: an element-overflow sweep, a rail geometry probe, a wheel-scroll reachability
test, and a XAML parse of every view's scroll structure.

Two early readings were **rejected** on further measurement and are recorded here so they are not
repeated:

- "Advanced and How a CD Works are unreachable at 480 height." **Wrong.** The full rail does have a
  vertical scrollbar; the first crop was 150px wide and cut it off. Items are reachable.
- "ConvertView and QueueView clip vertically because they have no ScrollViewer." **Wrong as a
  current defect.** Both measure `overBottom = 0` at 1200x480 and 640x480; their content fits. It
  is a latent fragility, not a live bug.

### Confirmed defects

| Id | Defect | Measurement |
| --- | --- | --- |
| C1 | Strip rail icons clipped by the rail's own scrollbar | Strip column 56px, minus 1px border, minus 16px `Padding="8,8"`, minus ~17px scrollbar leaves ~22px of content for a `Width="44"` button. Item right edge measures 77 against a content edge of ~78. Occurs only when the rail must scroll, i.e. window height below ~600 in strip mode. |
| C2 | Rip history rows hide the timestamp and cut the evidence text | `RipView.xaml:526-533` is a `DockPanel LastChildFill="False"` with `Result` docked Right **before** `When`. DockPanel reserves in declaration order, so `Result` takes all remaining width and `When` is starved to zero. Overflow past the client edge: 244px at 1200x800, 244px at 1200x480, 426px at 860x800, and 354px past even the held 860 layout at 640x480. `Result` sets no `TextTrimming` and no `ToolTip`. `HistoryStore.cs:75` always populates `When`, yet it renders in none of the 40 walkthrough captures. |
| C3 | Queue's Result column is unreachable in a narrow band | `QueueView.xaml:42-45` fixes `GridViewColumn` widths at 300 + 90 + 110 + 320 = 820, and the ListView sets `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`. Overflow measures 48px at 860x800. Below 860 the shell's held layout rescues it, so the broken band is roughly 860 to 910. |

### Working as specified

Below 860 the page area holds its 860 layout and scrolls horizontally. At 640x480 that measures a
uniform 292px of overflow, which is exactly `56 + 860 - 624`, and it is reachable. Every page that
overflows vertically scrolls: confirmed by sending real wheel events and diffing the pixels, with
Rip, Convert, Settings, Naming, Advanced, and How a CD Works all moving. The remaining four pages
do not scroll because they measure zero vertical overflow, not because they are stuck.

## Policy

Three tiers, in priority order.

1. **Never clips.** Navigation and primary controls. The rail must always show whole icons or whole
   labels; it may scroll, but it may never render a partial control. Rationale: a half-drawn
   navigation target is both unreadable and an unclear click target.
2. **Reflows.** Page content between 860 and the full-rail breakpoint. Content compresses to fit,
   with flexible columns and wrapping, down to 860 logical pixels. A control gets its own scrollbar
   only when reflow genuinely cannot fit it.
3. **Scrolls.** Below the 860 floor the layout is held at its 860 shape and the page area scrolls
   horizontally, which is the existing behaviour and stays unchanged. Vertical overflow at any size
   scrolls.

Trimming is allowed in tier 2 only with the full value available in a tooltip, per CLAUDE.md.

## Changes

### 1. Rail strip column, 56 -> 78

`MainWindow.xaml.cs` `ApplyRailLayout` sets the compact `RailColumn.Width` to 78 rather than 56.
The arithmetic is `44` icon + `16` padding + `17` scrollbar + `1` border = `78`, where 17 is
`SystemParameters.VerticalScrollBarWidth` at 96 dpi. The `RailIconPaths` 44x38 contract is
unchanged, so CLAUDE.md's strip specification still holds.

The icon is pinned to a fixed horizontal offset inside the strip item rather than centred, so it
does not shift by ~8px when the scrollbar appears and disappears.

Consequence to record: the held-layout total at the floor moves from `56 + 860 = 916` to `938`, so
the documented 292px overflow at 640x480 becomes 314px. This is bookkeeping in the evidence
documents, not a behaviour change.

### 2. Rip history row

In `RipView.xaml`, dock `When` to the right **before** `Result`, then give `Result`
`TextTrimming="CharacterEllipsis"` and a `ToolTip` bound to the same text. The row stays one line
and keeps its height. This closes D13 and also removes the title/result collision at narrow widths,
which has the same root cause.

### 3. Queue columns reflow

Make the Queue's `Result` column consume the width left over after Source, Action, and Status,
instead of a fixed 320. `GridViewColumn` has no star sizing, so the width binds to the ListView's
`ActualWidth` minus the other three columns and the chrome, via a converter. Re-enable
`ScrollViewer.HorizontalScrollBarVisibility="Auto"` on that ListView as the tier-2 fallback.

### 4. Convert and Queue page scrollers

Wrap the root `Grid` of `ConvertView.xaml` and `QueueView.xaml` in
`<ScrollViewer VerticalScrollBarVisibility="Auto">` so all ten pages behave alike. No visible
change at today's content sizes; this removes the latent fragility rather than fixing a live bug.

## Testing

A new `PageReachabilityTests` suite drives every page registered as a `PageViewModel` at 640x480
and 1200x480 and fails when any element extends past the viewport without a scrollbar that can
reach it. This is the regression net for the policy above, and it catches the eleventh page
automatically.

Existing suites must stay green: `CUETools.Wpf.Tests` at 730 and `RailStripPortTests` at 3.
`RailStripPortTests` pins the rail data contract and needs review against the 78px column.

The four probe scripts used to gather the evidence are rerun as before-and-after proof, and the
affected states are recaptured into `docs/evidence/`.

## Out of scope

Wiring the mutation harness into CI (still open). The two mutation follow-ups recorded in
`docs/review/2026-08-24-mutation-harness-rebaseline.md`. Any restyling that is not required to stop
clipping.

## Risks

The Queue column converter is the least contained change, because `GridView` sizing interacts with
the ListView's own scrollbar appearing. It needs a test at the exact 860 boundary where the defect
was measured.

Widening the strip to 78 moves the page area 22px to the right and costs it 22px of width at every
size below the 1140 breakpoint. That is the tier-2 band, so page content must reflow into 22px less
room than it does today; Queue is the page already closest to its limit there. The 1100 icon-strip
captures in `docs/evidence/` need retaking so the archive matches shipped behaviour.
