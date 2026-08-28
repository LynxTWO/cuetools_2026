# The machined key and the lamp checkbox, 2026-08-28

The WPF button is now the Linux head's machined console key, and the checkbox is its lamp. These
are the first two slices of the control-theme port the owner approved on 2026-08-28; the key is
the slice that proves the mechanisms every later control needs.

## What changed

The old button modelled a rubber cap floating 6 logical pixels above the panel, squashing and
tilting toward the click point with an elastic spring-back (`BendyButton`). The new one models a
key sitting flush in its housing: pressing sinks the cap 1.2 pixels while the housing lamp behind
its skirt comes up. Those are different physical objects, not two qualities of one, so the old
behaviour was removed rather than tuned. `BendyButton.cs` went with it - nothing else referenced it.

Five layers, back to front: the recess the key sits in, the housing lamp seen through the seam
around the cap, the cap itself, the cap's dome (shoulder, crown, lip), and the label with its
legend strip.

## Host and method

| Item | Value |
| --- | --- |
| Machine | DESKTOP-D084LOM, AMD Ryzen 9 5950X, Windows 11 Pro 10.0.26220 |
| Display | single monitor, 2560x1440, 100 percent (96 dpi) |
| Build | `CUETools.Wpf`, Release, net8.0-windows, .NET SDK 8.0.422 |
| Capture | offscreen `RenderTargetBitmap` at 192 dpi against the real merged `Theme.xaml` and a real `ThemeService` palette |

The state matrix is rendered offscreen because `IsMouseOver` cannot be faked and a trigger
storyboard needs a live clock. The hover and pressed rows light the same seam layer the triggers
animate, at the same opacity they land on: **that shows the look the animation reaches, not the
animation**. The triggers themselves were exercised live (below).

## Results

`key-states-dark.png` and `key-states-light.png`, thirteen states each: nine for the key, four for
the lamp.

| State | What it shows |
| --- | --- |
| rest | The domed cap: crown highlight high and off-centre, shoulder falloff darkening toward every edge, a bright lip hairline on top and a shadow hairline beneath. |
| hover | The housing lamp at 0.34, spilling amber onto the console around the key. No outline: a key in a lit housing shows more of that light as it moves. |
| pressed | The lamp at 0.85, the face on `ButtonPressed`, the drop shadow tightened, the cap sunk 1.2 pixels. |
| disabled | Unpowered, not blanked. The label sits at standby current with a faint glow of its own, and the seam is dark. |
| accent | The same cap in the accent metal. Setters only, no second template. |
| accent off | An unpowered accent key returns to the ordinary face; its label drops to standby with every other dead key. |
| transport | The backlit legend strip, unlit. |
| transport lit | The primary key's legend, lit and blooming. |
| transport off | The legend goes grey and stops blooming. |
| lamp off | The recessed housing, its specular cap highlight, and a tick etched into it - present but unlit. |
| lamp on | The lens lights, the tick darkens against it, and the housing takes a teal halo. Warms in 0.18s, cools in 0.34s, the way a filament does. |
| lamp off, dead | The label drops to standby. |
| lamp on, dead | The lens stays lit and the halo goes out. The lens carries state, so a disabled option still says whether it is on; the halo carries power, so it goes, exactly as a dead key's seam lamp does. |

### The lamp checkbox

Until this slice the app had **no** CheckBox style at all: its two checkboxes rendered OS default,
and they are the pair recorded in `docs/evidence/2026-08-27-selector-high-contrast` as adopting
Windows high-contrast styling inside an otherwise palette-coloured window. D14 chose to keep the
custom palette on purpose, so one explicit template settles both of those and the new ones together.

Two departures from the Linux head, both because it only ever renders this control on a dark
console: the housing reuses the switch's own palette tokens rather than hardcoded darks, so the
light theme gets a light housing instead of a black hole in a pale panel; and the tick's two
colours became `LampTickOff` and `LampTickOn` tokens for the same reason.

The Rip page's cue, log and cover-art options moved from switch rows to this lamp, wrapping
horizontally with readable labels (the owner's option C). A switch says "this state is on now",
which belongs on a settings page where the other 53 live; a lamp says "include this in what I am
about to do", which is what these three are. It also costs the rail about 44 vertical pixels
instead of about 91, on the page that already fought clipping. The labels stay readable rather than
dropping to the Linux head's `cue` and `log`, which save twelve pixels and cost the reader a guess.

## Live check

The real app was driven to the Rip page at 1200 logical pixels with a disc in K:. The RUN group
rendered as a bank of transport keys with their legend strips, all three correctly in the
greyed-legend disabled state (artwork discovery had not returned, and `CanStartEncodedJob` gates on
it). The gated `SelectorCaptureTests`, which constructs the artwork browser and codec picker for
real and would throw `XamlParseException` on a bad template part, passes.

Those live frames are not archived here: they carry the disc's album and artist text and the
owner's output path, and this repository scrubs both from its logs.

**One measured trade-off.** The disabled label carries a `DropShadowEffect`, and WPF drops
subpixel text antialiasing inside an effect. Compared side by side with an enabled key at 3x, the
standby labels are visibly softer. They stay readable, and the softness reads as the glow it is
meant to be, so it is retained rather than worked around. Recorded here as measured, not inferred.

## What this slice proves for the rest of the port

- The five-layer stack, radial gradients with relative origins, and `DropShadowEffect` standing in
  for Avalonia `BoxShadow` all reproduce faithfully. Every Linux shadow uses spread 0, which is
  what makes that substitution exact.
- Variants work through an attached property (`KeyStyle.Role`) plus triggers inside the one
  template. Avalonia reaches into another style's template with a `/template/` selector; WPF
  cannot, and mixing style setters with template `TargetName` setters is what let the Classic
  theme's own trigger beat the palette under high contrast (D14). Keeping every part-level
  override inside the single template is the rule the rest of the port follows.
- Keyboard-only focus comes from `FocusVisualStyle`, which WPF draws only when the most recent
  input device was the keyboard. That is what Avalonia's `:focus-visible` means; a trigger on
  `IsKeyboardFocused` would light after every mouse click.
- State transitions come from `EnterActions`/`ExitActions` storyboards, the pattern the `Switch`
  toggle already used here.

`MachinedKeyTemplateTests` pins the layers, the light-told states, the standby treatment, the
attached-property mechanism, the accent key inheriting the one template, the RUN group's roles, and
both palettes carrying every new token. Before it, no test in this suite walked a control template
or named a single part.

## Still to come in this port

The `ComboBox.window` with its thumbwheel, the `ListBox.bank`, and the `OpacityMask` vignette the
`Switch` glow is missing. The DPI sweep and the 1200-pixel Rip page captures need re-taking once
control metrics settle; the rail and queue column tests assert measured constants rather than
measuring, so a metric change there goes wrong without going red.
