---
name: disc-read-visualization
description: Use when building, extending, or verifying a real-time visualization of what a CD drive and error-correction are actually doing during a rip or verify - sector re-reads on a scratched disc, CTDB Reed-Solomon parity repair, read level (VU), or read speed. Sibling to codec-visualization (which is the codec compression pipeline). Portable pattern; reference rendering is WPF.
---

# Disc-Read / Error-Correction Visualization

Show what the **drive and the error-correction** are really doing while reading a disc: how many
times a damaged sector is being re-read, which sectors CTDB parity had to reconstruct, how loud the
audio is, how fast the read is going. Every number comes from the real ripper event or the real
CTDB repair object - the visualization only ever appears when the thing it shows is actually
happening, and never invents a number.

This is the physical-read twin of `codec-visualization` (the codec compression pipeline). Keep the
two separate: a re-read is not a codec stage.

## The controls

All are `FrameworkElement` + `OnRender` + `CompositionTarget.Rendering`, GPU-composited, driven by
dependency properties the view-model marshals real data into:

| Control | Shows | Real source |
|---|---|---|
| `RereadScope` | a stuck sector being re-read (count, disagreeing sectors, band + pits) | drive retry loop |
| `RepairScope` | CTDB Reed-Solomon parity reconstruction (damage map, RS pipeline) | `CDRepairFix` |
| `VuMeter` | read level, per-channel | RMS of the read audio |
| `SpeedGraph` | read speed vs realtime | read-progress delta |
| `DiscModel3D` | 3D CD + laser reading the spiral; zooms to real damage | rip progress + re-read |
| `LayerCrossSection` | the CD layer stack + laser path (explore mode) | static CD spec |
| `DiscReadMap` | inside-out read position (the 2D / weak-hardware fallback) | rip progress fraction |

## Re-read (RereadScope): the drive's retry loop

The ripper reads a window of sectors and does up to `16 << cq` passes (Burst/Secure/Paranoid =
16/32/64), breaking early once `pass >= cq && ErrorsCount == 0`. So the guaranteed-minimum passes =
`cq + 1`; **any pass beyond that is a real re-read of a window that would not agree with itself.**

- Source: the ripper's `ReadProgress` event -> `ReadProgressArgs { Position, Pass, PassStart,
  PassEnd, ErrorsCount }` (`Pass == -1` is the TOC/pregap read, skip it).
- `reReads = Pass - cq` (only `> 0` while genuinely struggling); `maxReReads = (16<<cq) - 1 - cq`;
  `ErrorsCount` = sectors in the window that still disagree.
- Fire the VM callback only when `reReads > 0` (plus one final `0` so the box can hide). Count each
  stuck window once (`reReads > 0 && lastReReads == 0`) for a diagnostics line and a peak.
- The band is the window, one dark pit per disagreeing sector, a head sweeping = one pass. Make the
  sweep **slow down as the count climbs** - the real drive drops its read speed on a hard pass
  (32500 -> 300 -> 150 -> 75 sectors/s). Escalate amber -> red by severity; turn green ("recovered")
  the instant `ErrorsCount` hits 0; linger ~1.4s then fade so the recovered state is seen.
- During a re-read the read `Position` jumps back inside the window, so the progress bar and the
  read-speed graph naturally pause/dip at exactly that spot. The re-read box is the explanation for
  that pause - same real event, three views.

## Repair (RepairScope): CTDB Reed-Solomon parity

When the drive gives up on sectors, the CUETools database has parity computed across the whole disc
and can reconstruct the exact damaged samples. All the numbers are on the real `CDRepairFix`, and
they are populated during the **Verify** pass (`DoVerify`), before any destructive write:

- `DBEntry.repair` (a `CDRepairFix`): `CorrectableErrors` (bad 16-bit samples), `CanRecover`,
  `AffectedSectorArray` (a `BitArray`, **one bit per CD sector**, true = a sample there was
  corrected), `ActualOffset`, `CRC`. `DBEntry.Npar` = parity depth (4/8/16), `conf`, `CTDB.Total`.
- Downsample `AffectedSectorArray` to a `double[]` density map for the strip - a scratch shows as a
  cluster exactly where it is. This is the most concrete "what happened".
- Parity structure to show: 16-bit symbols, GF(2^16) (poly 0x1100B), `npar` symbols per **10-sector
  stride** (stride = `10 * 588 * 2` = 11760 bytes), up to `npar/2` corrections per stride-column.
- The RS pipeline is real: **syndrome -> locate (Berlekamp-Massey `calcSigmaMBM`) -> Chien search ->
  Forney -> apply**. The first four are computed at Verify, so light them as soon as errors are
  found; **`apply` is the destructive write (`repair.Write` XORs Forney corrections into the audio)
  and lights only on Repair.** States: recoverable (amber) -> repairing (green sweep tied to the real
  repair progress) -> repaired (green).

## The 3D disc (DiscModel3D)

A real 3D CD (a `Viewport3D` subclass, GPU-rasterized, built-in - no native dependency, and it IS
the weak-hardware fallback: `RenderCapability.Tier >> 16` picks the 2D `DiscReadMap` on tier 0).

- **Read position is real, via true CD geometry.** `radius(f) = sqrt(rInner^2 + f*(rOuter^2 -
  rInner^2))` (inner data ~25 mm, outer ~58 mm) - an EQUAL-AREA mapping, so a linear read fraction
  moves the laser at constant data density (the CLV truth). A consequence worth showing: only ~10% of
  the data sits before 50% radius, because most of a CD's area is near the rim. The read glow (an
  emissive radial-gradient brush, planar UVs so it maps to world radius) grows inside-out to the
  laser.
- **Zoom-to-damage** reuses the re-read data: on `RereadActive` a `_zoom` lerps 0..1 and the camera
  interpolates from the overview pose to a close pose above `radius(RereadFrac)`; a pulsing marker +
  amber edge mark the spot, red when `Unreadable` (a window that exhausted retries with errors:
  `reReads == maxReReads && errors > 0`). Stop-on-unrecoverable holds the red zoom until the job ends.
- **Surface is procedural textures, not shaders.** WPF `ShaderEffect` is SM3 / 2D-post-only, so the
  diffraction rainbow and the (representative, not literal 1.6 um) data-track spiral are generated
  into `WriteableBitmap`s once and layered as emissive materials; the track opacity rises with zoom.
- **Explore mode** (`Interactive`): a free-orbit camera (spherical az/el/dist) driven by `Orbit()` /
  `Zoom()` from mouse drag + wheel, for the "How a CD Works" lesson page with the `LayerCrossSection`.
- **Honesty:** the read position, re-read back-track, and CLV are real; the pits and the diffraction
  are representative at the correct scale, not this disc's actual bits (a caption says so). Damage is
  labeled by outcome, never by a physical cause the drive cannot report.

## VU and speed

- **VU is RMS, not peak.** Peak pins at full-scale for any loud music (needle frozen at top); RMS
  moves with loudness. Fast attack, slow decay. **Hold the last level during a read gap** (do not
  decay to zero) or the meter just mirrors the speed graph's dip - the Read Level is about the music,
  the Read Speed about the drive; keep them independent.
- Speed = progress delta over wall-clock, expressed as a multiple of realtime (1x = 75 sectors/s).

## Honesty rules

- Show a control only while its real event is happening; hide it otherwise. A clean disc shows no
  re-read box and no repair scope at all.
- Every displayed count is the real field from `ReadProgressArgs` / `CDRepairFix`. Do not scale,
  invent, or smooth the counts (smoothing the *animation* is fine; the numbers are literal).
- The read-speed dip, the progress pause, and the re-read box are the SAME real event - keep them
  consistent, never contradict each other.
- Log diagnostics privacy-safe: counts and disc-position percent only, never album/artist/track
  names (see the diagnostic-log rules).

## Verify by rendering (and how to get real data to render)

`CompositionTarget.Rendering` does not tick under a bare `RenderTargetBitmap`, so use a real
off-screen `Window`, feed the DPs on a `DispatcherTimer`, and capture each state to PNG. This works
even when the live window is blank from **GPU-compositing exhaustion** after heavy testing (the
off-screen render, the UIAutomation tree, and drive reads all still work; a fresh launch recovers the
live window). See `scratchpad/RereadRender`, `scratchpad/RepairRender`.

- **Re-read**: drive the DPs through a ramp (count climbing, errors falling to 0 = recovered, then
  clear + fade). For the real end-to-end path, rip a physically damaged disc (pin-holes in the data
  layer) and screen-grab during a live stuck window; time the grab off the per-window log line.
- **Repair**: generate a GENUINE `CDRepairFix` offline through the same AccurateRip syndrome path
  CTDB uses - `new CDRepairEncode(ar, stride)` (enables parity), write good audio, `GetSyndrome(npar)`
  + `CRC`; then a second `CDRepairEncode` over corrupted audio, `FindOffset(...)` -> `VerifyParity(...)`
  -> a real `CDRepairFix` with real `CorrectableErrors` / `AffectedSectorArray`. Corrupt a few
  contiguous regions and confirm they come back as the expected `AffectedSectors` time ranges. See
  `scratchpad/RepairReal`.
- **3D disc**: unlike the 2D `CompositionTarget` controls, `Viewport3D` / 3D content DOES compose
  under a bare `RenderTargetBitmap`, so it verifies fully off-screen - render at several `Progress` /
  `RereadFrac` / `Interactive` orbit states and check the laser radius and camera respond. See
  `scratchpad/DiscRender`. Then confirm the live zoom-to-damage on a pin-holed disc.
- Measure a suspected stutter/hang objectively with crop-hash run-lengths at ~15 fps, not by eye.

## Extending

- **New drive-state viz**: find the real field on the ripper event first; if it is not already on
  `ReadProgressArgs`, thread it out of the SCSI read loop rather than approximating.
- **New repair detail** (e.g. per-track confidence, individual Forney magnitudes): they are on the
  same `CDRepairFix` (`erroffsorted` / `forneysorted` internally) computed during the Verify pass.
- Keep the honesty rules above in sync with the controls; a control that can show a number it did not
  measure is a bug.
