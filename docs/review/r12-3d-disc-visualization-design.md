# R12: 3D disc + laser optical model - design

Status: implemented and visually refreshed through R80 on 2026-07-29. Normal
light/dark live captures and the offscreen state matrix pass. A formal frame-time
benchmark remains open.

## Goal

Show, in real 3D, how a CD is physically read: the laser tracking the spiral of pits and lands. Two
things at once - it is tied to what the drive is actually doing during a rip, and it is explorable as
a standalone lesson.

Decisions already made with the owner (2026-07-12):

- Renderer: WPF 3D (`Viewport3D`, built-in, GPU-rasterized) plus bounded
  procedural `WriteableBitmap` surface textures.
  Chosen over a full D3D11 interop for maintainability and because it degrades into its own
  weak-hardware fallback with no separate renderer to keep.
- Scope: both a live disc on the Rip page and an explorable "How a CD works" mode. Built in two
  stages, live first.

Implementation checkpoint, 2026-07-29:

- The live model separates the 15 mm hole, clamp ring, mirrored hub, 25 to
  58 mm program area, clear outer rim, back, and edge thickness.
- The pickup lens and representative near-infrared beam sit below the substrate.
  The focus spot remains at the real equal-area read radius.
- The data texture is a representative continuous spiral. Narrow curved
  diffraction highlights replace the earlier broad rainbow wedge.
- Inner-radius visual spin is faster than outer-radius spin, preserving the CLV
  relationship at a slowed presentation rate.
- `RereadActive`, `RereadFrac`, and `Unreadable` still own camera zoom, recovery
  ease-out, and unreadable hold. The visual layer adds no optical-drive commands.
- The tier-zero fallback uses the same physical radii and theme tokens, and it
  does not spin while no rip is active.

## What it teaches

Plain English first: a CD is one long spiral of tiny bumps read from the inside out by a laser that
reflects off a mirror layer; a scratch makes the laser re-read the same spot until it agrees.

- The spiral: one continuous track from the hub outward, not concentric rings (the read-map already
  says "reading inside-out"; this shows why).
- Pits and lands: the bumps encode the audio via EFM; transitions are the 1s. Representative, not a
  bit-exact EFM reconstruction (see honesty rules).
- The laser: a focused spot tracking the spiral; the disc spins (CLV: faster at the hub, slower at
  the rim, so the data rate stays constant).
- Damage: on a real re-read the laser visibly back-tracks over the scratched region - the same event
  the RereadScope already reports.
- Layers (stage 2): polycarbonate substrate, reflective aluminum, lacquer, label - why a scratch on
  the clear side is often recoverable but a scratch on the label side is fatal.

## Architecture

- `DiscModel3D` control (`FrameworkElement` hosting a `Viewport3D`), GPU-rasterized like the other
  controls are GPU-composited. Disc mesh (a thin cylinder + textured top), a laser beam (a thin
  emissive cylinder + a spot light), an orbit camera.
- Procedural pit detail: a WPF `ShaderEffect` (HLSL pixel shader) on the disc-surface material paints
  the pit/land spiral at the real track pitch (1.6 um) and pit length scale when the camera is close.
  Billions of pits cannot be meshed; the shader draws them at any zoom. Far away, the shader fades to
  a plain reflective surface (no cost, no moire).
- Data is the real rip data already wired: `RipProgress` (0..1 read position), the re-read state
  (`RereadActive`, `RereadFrac`, `RereadCount`), and the disc TOC total length (maps read fraction to
  a radius on the spiral). No new data path.

### Read-position mapping (real)

The spiral radius for a read fraction `f` interpolates the CD geometry: inner radius ~= 25 mm, outer
~= 58 mm, track pitch 1.6 um. `radius(f) = sqrt(rInner^2 + f * (rOuter^2 - rInner^2))` (equal-area,
so a linear read rate moves the laser at constant data density - the CLV truth). The laser angle
advances with the spin. On a re-read, the laser holds/returns to `RereadFrac` and jitters within the
stuck window until `RereadActive` clears - driven by the same events the RereadScope uses.

## Stage 1: live 3D disc on the Rip page

Replaces or augments the current 2D `DiscReadMap` in the left column of the Rip page.

- Idle (no rip): the disc sits still, slight ambient tilt, a hint of rainbow diffraction on the
  surface (a cheap shader term), the laser parked at the hub.
- Reading: the disc spins, the laser tracks `radius(RipProgress)`, the read spiral behind the laser
  lights up (already-read region tinted), ahead stays dark.
- Re-reading: the laser back-tracks over the stuck window and pulses; a small "re-reading" tag can
  reuse `RereadCount`. This is the physical twin of the RereadScope, not a second data source.
- Verify: same, tinted teal instead of amber to match the page.

Camera: a fixed 3/4 orbit by default; optional slow auto-orbit. No user interaction needed here (that
is stage 2).

### Zoom to damage (live, real-outcome-driven)

When the drive hits real damage, the camera dollies IN to the damaged radius, shows it being worked,
then eases back OUT when the read continues - so the viewer's eye is taken to exactly where the disc
is failing, on this disc, right now. Driven entirely by data already wired (`RereadActive`,
`RereadFrac`, `RereadCount`, plus the failed-sector outcome), no new source.

Honest note on the two damage types: the drive reports difficulty and failure, never the physical
cause, so the states below are keyed to the real OUTCOME (which correlates with cause), and the
physical cause is shown as EDUCATION, not a claimed measurement.

- Being re-read (amber): `RereadActive` -> camera zooms to `radius(RereadFrac)`, laser re-scans the
  stuck window, the re-read count ticks. Smooth lerp in.
- Recovered (green flash), then zoom out: the window converged (`ErrorsCount -> 0`). Caption: "read
  through it" - typically a clear-side scratch, the data layer is intact. Read resumes, camera eases
  back to the overview.
- Reconstructed from parity (teal): the drive failed the window but CTDB parity can fill it (surfaces
  on the Verify/encode pass, links to the RepairScope). Marked, not zoomed-held.
- Unreadable / uncorrectable (red): sectors the drive failed AND parity cannot recover. A red damage
  marker sits at that spot. Caption frames the physical cause as education: "the data layer here is
  gone - this is what label-side damage (a pin-hole through the reflective layer) does; a clear-side
  scratch usually reads through or is fixed from parity."

If the rip is set to stop on unrecoverable damage (a new opt-in setting, off by default so today's
behavior of finishing and marking failed sectors is unchanged), the camera STAYS zoomed on the failed
location with the red marker slowly flashing until the user ejects or stops - so an unrecoverable disc
does not silently produce a bad rip; it stops and shows you exactly where and why. The stop itself
reuses the existing `Stop()` path (`StopException`), just triggered by the failed-sector outcome
instead of the button.

## Stage 2: explorable "How a CD works" mode

A new nav page (or a full-window overlay opened by an "Explore" button on the live disc).

- Free orbit + zoom (mouse / trackpad), from the whole disc down to a single pit.
- Zoom tiers, each with a one-line callout: disc -> a track band -> the pit/land spiral -> one pit,
  with the real dimensions labeled (1.6 um pitch, ~0.5 um pit width, ~0.83 um minimum pit length).
- A cross-section cutaway of the layers (polycarbonate 1.2 mm, aluminum, lacquer, label), with the
  laser entering from the clear side and focusing past a surface scratch - the why behind CTDB
  repair and the clear-side-vs-label-side asymmetry.
- No live rip data required; it is a self-guided lesson. If a disc is loaded it can optionally start
  focused on that disc's read position.

## Weak-hardware fallback (required by the standing perf directive)

Tiered, detected once at startup from the WPF `RenderCapability.Tier`:

- Tier 2 (full GPU, e.g. the RTX 3060): shader pits, reflections, diffraction, auto-orbit.
- Tier 1 (basic GPU): flat-shaded 3D disc + laser, no pit shader, no diffraction. Still real 3D.
- Tier 0 / software rendering: fall back to the existing 2D `DiscReadMap`. No regression - the 2D
  map is what ships today.

The live read data drives all three tiers identically; only the surface detail scales.

## Honesty rules (same standard as the other visualizations)

- The read position, spin direction, inside-out progression, and the re-read back-track are REAL
  (driven by `RipProgress` and the re-read events).
- The pit/land pattern is REPRESENTATIVE of EFM at the correct physical scale, not a bit-exact
  reconstruction of this disc's data. State that in a caption and a code comment; do not imply the
  visible pits are this track's actual bits.
- The layer cross-section dimensions are the real CD spec figures. Cite them.
- No fabricated "reading" motion when nothing is being read (idle disc is still).
- Damage states are labeled by the real OUTCOME (re-reading / recovered / reconstructed from parity /
  unreadable), never by a physical cause the drive cannot report. The scratch-vs-pin-hole framing is
  phrased as education ("this is what label-side damage does"), tied to the outcome, not asserted as a
  measurement of this disc.

## Verification plan

- `RenderTargetBitmap` renders idle, reading, re-reading, unreadable, and
  tier-zero fallback frames in both palettes.
- Radius tests cover clamping and the exact f = 0, 0.5, and 1 equal-area values.
- State tests prove normal pickup movement, re-read back-track and zoom,
  recovery ease-out, and unreadable hold.
- A 1,000-frame post-warmup test bounds render-state allocations below 128 KiB.
- Actual 1180x740 WPF windows were inspected in dark and light mode at 96 DPI.

## Risks / open questions

- The shader question is resolved. The control builds bounded procedural
  textures once and uses WPF materials at render time.
- A formal live frame-time benchmark is still open. Tier zero retains the
  software-rendered fallback.
- Placement is resolved. The 3D control owns the live slot on capable hardware;
  the 2D map occupies the same slot at tier zero.

## Build order

1. Procedural texture selection: done.
2. Live geometry, read mapping, re-read back-track, and hardware tiers: done.
3. Damage zoom and outcome markers: done.
4. Explore mode and layer cross-section: done.
5. Formal frame-time benchmark: open.
