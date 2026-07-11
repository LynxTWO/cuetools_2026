---
name: lit-panel-controls
description: Use when building or restyling WPF "hi-fi bench" controls that model a real bulb behind translucent plastic - toggle switches, indicator lamps, VU/meters, accent keys - or when recoloring that emissive glow to a different light color. WPF/XAML specific.
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

Ramps used in CUETools 2026 (housing/cap/occlusion stay neutral - only these carry the hue):

| Role | Teal (default) | Amber | Green |
|---|---|---|---|
| LampCore | `#EAFFFB` | `#FFF6E9` | `#EAFFF1` |
| LampLight | `#6FE3D6` | `#F0C784` | `#9FE9B8` |
| LampBase | `#27A99C` | `#C8871F` | `#3FB877` |
| LampDark | `#0E4F48` | `#7A5416` | `#1E5E38` |
| LampEdge | `#06211E` | `#241804` | `#06210F` |
| LampHalo | `#34CFC0` | `#E9A63F` | `#5CCB8B` |

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
   `root.Resources.MergedDictionaries` so implicit styles apply. See `scratchpad/SwitchRender`.
3. For ANIMATION or the lit ON state (which needs the storyboard clock, and for `DynamicResource`
   recolor which needs a real resource scope), run a real `Application` + `Window` off-screen,
   wait ~600ms on a `DispatcherTimer`, THEN `RenderTargetBitmap`. A single headless frame captures
   the animation at t=0 (looks off). See `scratchpad/AnimTest` - it is how the warm-up and the
   teal/amber/green recolor were verified.

## Verify by rendering, never by guessing

You usually cannot see the running WPF app. Render the control to a PNG and look at it:

1. Keep the styles in a standalone `ResourceDictionary` (e.g. `Theme/Theme.xaml`) merged by the
   app, so a harness can load the SAME file.
2. A tiny net8 WPF console loads it with `XamlReader.Parse(File.ReadAllText(path))`, builds the
   control with the real `Style`, `Measure`/`Arrange`, and renders via `RenderTargetBitmap`
   (192 dpi = 2x) to a PNG. Put the theme dict in `root.Resources.MergedDictionaries` so implicit
   styles apply. See `scratchpad/SwitchRender` in this repo for the harness.
3. Read the PNG back and adjust hotspot origin, falloff offsets, and bleed opacity until the
   light reads right. This is how the switch physics were tuned.

## Common mistakes

- Putting the glow hotspot UNDER the slider instead of on the exposed lens - backwards; the lit
  part is what is NOT covered.
- Making the cap bleed too strong - thick plastic only leaks a little; if it looks like a lit
  button, cut the opacity.
- Coloring the occlusion or housing with the accent - those stay neutral; only the emissive
  layers carry color, which is what makes one-knob recolor work.
- Using `StaticResource` for glow colors if you also want a runtime theme swap - use the ramp
  table per theme instead.
