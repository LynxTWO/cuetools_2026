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

## The one knob: light color

Every glow uses the same 5-role ramp derived from one accent hue - near-white core, light tint,
base accent, dark accent, near-black edge - with alpha preserved. To recolor, swap the ramp:

| Role        | Teal (default) | Amber      | Green      |
|-------------|----------------|------------|------------|
| Core        | `#EAFFFB`      | `#FFF6E9`  | `#EAFFF1`  |
| Light tint  | `#6FE3D6`      | `#F0C784`  | `#9FE9B8`  |
| Base accent | `#34CFC0`      | `#E9A63F`  | `#5CCB8B`  |
| Dark accent | `#0E4F48`      | `#7A5416`  | `#1E5E38`  |
| Edge (dark) | `#06211E`      | `#241804`  | `#06210F`  |

Keep the OFFSETS and ALPHAS as in the reference; change only these five colors and the
`DropShadowEffect Color`. The housing, cap metal, and occlusion (pure black) do NOT change - only
the emissive layers carry the light color.

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
