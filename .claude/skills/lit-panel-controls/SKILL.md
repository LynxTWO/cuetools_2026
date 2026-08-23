---
name: lit-panel-controls
description: Use when building or restyling WPF "hi-fi bench" controls that model a real bulb behind translucent plastic - toggle switches, indicator lamps, VU/meters, accent keys - or when recoloring that emissive glow to a different light color. WPF and Avalonia; see soft-body-controls for controls that DEFORM rather than light up.
---

# Lit Panel Controls

Physical instrument controls where a colored bulb sits behind translucent plastic. The look is
modeled from real light behavior, and the whole family shares ONE knob: the light color.

## The light model (four layers, back to front)

A lit control is built by stacking these, not by coloring one shape:

1. **Housing** - the recessed plastic body. Dark when off; a faint outer glow (DropShadowEffect
   in the light color) when on.
2. **Lens glow** - the bulb shining through the EXPOSED plastic. A `RadialGradientBrush` whose
   hotspot sits on the exposed side (the side the slider is NOT covering) and falls off to near
   black under the slider. This is the core trick: the lit lens is the part you can see, so when
   a toggle sits right, the glow hotspot is on the LEFT.
3. **Occlusion** - a soft dark radial riding under the slider, so the light visibly falls off
   where the cap covers it.
4. **Cap bleed** - a faint glow of the light color bleeding THROUGH the thick plastic cap,
   strongest on the cap edge facing the lens, fading across the cap. Keep it subtle (thick
   plastic): low opacity over an already-reduced-alpha gradient.

Off state = housing + opaque-looking cap only (all glow layers `Opacity=0`). The checked/on
trigger raises the glow, occlusion, and cap-bleed opacities and slides the cap + its bleed.

Reference implementation: [assets/lit-switch.xaml](assets/lit-switch.xaml) (the CUETools 2026
`Switch` style). The same four layers recolor a lamp (housing + lens only), a VU backlight, or
an accent key.

## The one knob: light color (via scoped resources - the good way)

The bulb colors are a ramp of named `Color` resources the template references with
`DynamicResource`, so ONE template recolors per scope - no duplicated styles. Keys:
`LampCore` (near-white), `LampLight`, `LampBase`, `LampDark`, `LampEdge`, `LampHalo`
(DropShadow color), and the cap-bleed `LampCapCore` / `LampCapMid` / `LampCapEdge` (alpha-
prefixed). Default is teal, defined once in `Theme.xaml`.

To color-code a section, override those keys in that element's `Resources` - every switch inside
recolors:

```xml
<StackPanel>
  <StackPanel.Resources>
    <Color x:Key="LampCore">#FFF6E9</Color><Color x:Key="LampLight">#F0C784</Color>
    <Color x:Key="LampBase">#C8871F</Color><Color x:Key="LampDark">#7A5416</Color>
    <Color x:Key="LampEdge">#241804</Color><Color x:Key="LampHalo">#E9A63F</Color>
    <Color x:Key="LampCapCore">#B8FFF8EE</Color><Color x:Key="LampCapMid">#5AE0A94A</Color><Color x:Key="LampCapEdge">#00E9A63F</Color>
  </StackPanel.Resources>
  ... switches here are amber ...
</StackPanel>
```

Ramps used in CUETools 2026 (housing/cap/occlusion stay neutral - only these carry the hue).
Category semantics as shipped: teal = core rip engine, blue = database results
(AccurateRip & CTDB), amber = metadata/presentation (tagging, album art), rose =
privacy and consent, green = special audio decode (HDCD):

| Role | Teal (default) | Blue | Amber | Rose | Green |
|---|---|---|---|---|---|
| LampCore | `#EAFFFB` | `#EAF6FF` | `#FFF6E9` | `#FFEEF6` | `#EAFFF1` |
| LampLight | `#6FE3D6` | `#79C4EC` | `#F0C784` | `#EFA3C8` | `#9FE9B8` |
| LampBase | `#27A99C` | `#2E86B8` | `#C8871F` | `#C25789` | `#3FB877` |
| LampDark | `#0E4F48` | `#123D57` | `#7A5416` | `#5E2743` | `#1E5E38` |
| LampEdge | `#06211E` | `#071925` | `#241804` | `#29101E` | `#06210F` |
| LampHalo | `#34CFC0` | `#46B8E0` | `#E9A63F` | `#E37DAD` | `#5CCB8B` |

Cap-bleed triads follow the same construction per hue (`B8` near-white core, `5A` mid,
`00` halo edge), e.g. blue `#B8EFF9FF`/`#5A5CB2DE`/`#0046B8E0`, rose
`#B8FFF2F8`/`#5AD685B2`/`#00E37DAD`.

Note: `DynamicResource` on a `GradientStop.Color` / `DropShadowEffect.Color` (both Freezables)
DOES resolve through the scope here - verified - but it is a known-finicky corner; keep the
render check below.

## Animation (real bulb physics)

The transition is animated, not a snap. The moving parts (cap, cap-bleed, occlusion, sheen) ride
in a `mover` Grid with a `TranslateTransform`; the lens `Opacity` carries the light curve. Turning
ON (`Trigger.EnterActions` storyboard): the cap slides across with a slight mechanical overshoot
(`BackEase`), the bulb WARMS UP fast (`CubicEase` EaseOut ~180ms), and the cap-bleed fades in a
beat later (light takes a moment through thick plastic). Turning OFF (`Trigger.ExitActions`): the
bulb COOLS DOWN with a longer dim tail (~340ms) like an incandescent filament.

Use `To=`-only animations (no `From`) with default `HoldEnd` so the resting states are correct and
toggling mid-animation is smooth. Caveat: a switch that is CHECKED AT LOAD relies on EnterActions
firing on load - they do here (verified), settling it lit - but if you ever see an on-by-default
switch stuck dark, that is the cause; fix with VSM or explicit state, not property setters that
fight the animation.

## Verify by rendering, never by guessing

You usually cannot see the running WPF app. Render the control to a PNG and look at it:

1. Keep the styles in a standalone `ResourceDictionary` (e.g. `Theme/Theme.xaml`) merged by the
   app, so a harness can load the SAME file.
2. For STATIC looks (off state, geometry, colors), a tiny net8 WPF console loads it with
   `XamlReader.Parse(File.ReadAllText(path))`, builds the control with the real `Style`,
   `Measure`/`Arrange`, renders via `RenderTargetBitmap` (192 dpi = 2x). Put the theme dict in
   `root.Resources.MergedDictionaries` so implicit styles apply. Build this harness fresh; no checked-in copy exists.
3. For ANIMATION or the lit ON state (which needs the storyboard clock, and for `DynamicResource`
   recolor which needs a real resource scope), run a real `Application` + `Window` off-screen,
   wait ~600ms on a `DispatcherTimer`, THEN `RenderTargetBitmap`. A single headless frame captures
   the animation at t=0 (looks off). Build this harness fresh; no checked-in copy exists - it is how the warm-up and the
   teal/amber/green recolor were verified.

## Avalonia port notes (learned dialing in the Linux head, 2026-08-20)

The four-layer model ports as an Avalonia ControlTheme, with three deltas:

- **The light must die out INSIDE the housing.** The WPF gradient's opaque outer
  stops read fine under WPF's rendering, but on the Avalonia head they painted
  tinted corners with a hard stop at the border (owner-reported). Give the glow
  border a rounded-vignette `OpacityMask` (radial, white plateau to ~0.45,
  transparent by ~0.97) so the glow falls to bare housing before any edge or
  corner - the physical story is plastic thickening toward the rim.
- **ClipToBounds eats the halo.** The halo BoxShadow extends past the control's
  bounds; anything clipping at those bounds turns the soft aura into a hard-edged
  rectangle of light (this, not the gradient, was the original "light leaking with
  a hard stop" report). Set `ClipToBounds="False"` on the control AND the template
  root. Verify by rendering the CHECKED state zoomed 5x.
- **BoxShadow resolves no resources.** Avalonia box shadows are structs, so the
  halo cannot ride `DynamicResource LampHalo` the way WPF's DropShadowEffect does.
  Recolor a scoped group with a sibling style next to the scoped ramp:
  `<Style Selector="ToggleButton.lit /template/ Border#halo">` setting a literal
  per-hue `BoxShadow`. Everything else recolors through the ramp keys as on WPF.

The theme flip itself got the same physics on the Linux head: ThemeCrossfade holds
the old theme's frame in an overlay and dims it out (560 ms going dark, 300 ms to
light, CubicEaseOut) - the cooling-filament read, applied to the whole panel.

## Sibling skill: deformation

`soft-body-controls` owns controls that physically deform under a pointer - rubber keys,
dome switches, membrane pads. This skill owns emissive layers, the `Lamp*` ramp, and
state-driven light curves; that one owns geometry, pointer response, and spring motion.

Where they meet, one rule belongs to both: **a specular highlight that stays put while the
surface bends is the tell that breaks the illusion.** If a lit control is also a deforming
one, the glow layers must ride the deformation, and a press must suppress the crown's
specular locally rather than adding a second highlight of its own.

## Common mistakes

- Putting the glow hotspot UNDER the slider instead of on the exposed lens - backwards; the lit
  part is what is NOT covered.
- Making the cap bleed too strong - thick plastic only leaks a little; if it looks like a lit
  button, cut the opacity.
- Coloring the occlusion or housing with the accent - those stay neutral; only the emissive
  layers carry color, which is what makes one-knob recolor work.
- Using `StaticResource` for glow colors if you also want a runtime theme swap - use the ramp
  table per theme instead.
