# Plan: rip-accuracy SCSI features - implementation

Date: 2026-07-11. Follows the design in
`docs/superpowers/specs/2026-07-10-rip-accuracy-and-log-integrity-design.md`.
Split into what is done and proven headless, and what needs the owner's drive.

The design deliberately isolates each feature into a pure decision core (testable now, no
drive) and a thin hardware layer (SCSI I/O on `CDDriveReader`, verifiable only on hardware).
This plan builds the cores first, then wires the hardware.

## Done and proven (headless, this box)

Built in `CUETools.Wpf/Accuracy/` and verified by `scratchpad/AccuracyTest` (all PASS):

- **Log integrity self-check** - `CUETools.Wpf/Services/LogIntegrity.cs`. SHA-256 over the
  canonical log body; seal/verify/tamper round-trip proven on the shipped DLL. Already used by
  the Report certificate. (Spec model B, layer 1.)
- **Adaptive-speed state machine** - `AdaptiveSpeedController`. Start at max, drop one step on a
  C2/mismatch cluster, ease up after N clean regions, honor a calibrated ceiling. Proven:
  start-at-max, drop, ease-up-after-4, ceiling cap, single-speed drive.
- **Cache-defeat flush search** - `CacheDefeatSearch.SmallestEvicting`. Binary search over the
  monotonic "does flush size S evict?" predicate. Proven against a simulated timing oracle:
  finds the ~2 MB evictor, conservative fallback when nothing in range evicts, min-evicts case.
- **Calibration record + persistence** - `DriveCalibration` + `DriveCalibrationStore` (JSON at
  `%AppData%\CUETools2026\drive-calibration.json`, keyed by drive signature). Proven: unknown
  drive returns null, full-field round-trip, multiple drives coexist.

## Remaining (needs the owner's ASUS BW-16D1HT + a disc)

These are the hardware halves. Each calls a `Bwg.Scsi/Device.cs` primitive that already exists
(spec Non-goals: no new SCSI plumbing). Verify each on hardware as noted.

1. **DriveCalibrationService** (new, app layer). Owns detect + persist + load. On first sight of
   a drive signature with no saved record, run the probes below transparently during the first
   rip; write the `DriveCalibration`. A "Recalibrate" button re-runs it. A normal rip reads the
   record and never probes.

2. **Cache defeat probe** (Feature 1). FUA test: `Read(force:true,...)` one sector twice, time
   both; equal media-read times -> `CacheDefeat = FUA` (Confirmed). Else run
   `CacheDefeatSearch.SmallestEvicting` with a real timing oracle (read X, flush S, re-read X;
   fast re-read = not evicted) -> `Flush:<bytes>` (Estimated) or the 8 MB fallback (Unconfirmed).
   At rip time, before each secure re-read, apply the saved method.
   Verify: on hardware, confirm the FUA path reports Confirmed and a secure re-read is a real
   read (timings), and that flush sizing converges.

3. **Overread probe** (Feature 2). `ReadCDDA` one sector into lead-in and one past lead-out; data
   without error sets `Overread.leadIn/leadOut`. At rip time, read the offset-shifted boundary
   samples from lead-in/out when available; otherwise zero-fill AND record the honest note in the
   report. Verify: AccurateRip verifies track 1 and the final track (the overread payoff).

4. **Adaptive speed wiring** (Feature 3). Feed `AdaptiveSpeedController` from the ripper's C2 /
   pass-mismatch signal; apply `CurrentSpeed` via `SetCdSpeed`/`SetStreaming`; the supported list
   comes from `GetSpeed`. During calibration, find the highest zero-C2 speed -> `MaxSpeedKbps`
   ceiling. The Rip speed graph already exists to show the drops. Verify: speed drops on a
   scratched disc region and eases back on clean regions.

5. **UI surfacing.** Drive & Read lamps switch from static "planned" to the real per-feature
   calibration state + confidence; a Recalibrate button. Report adds cache/overread/offset/speed
   used + the honest zero-fill note. (The lamps are honestly marked "planned" until this lands.)

## Ordering

Per the spec: (1) log integrity self-check [done], (2) cache defeat, (3) overread, (4) adaptive
speed, (5) authoritative log record once a CTDB server field exists. The cores for 2/3/4 are
done; each remaining item is one hardware probe + one rip-time application + its UI line.

## Notes

- Keep the "degrade honestly" rule: when a drive cannot do something, say so in the UI and the
  report; never silently zero-fill or fake a confidence.
- The pure cores have no SCSI dependency; they can move into `CUETools.Ripper` when
  `CDDriveReader` consumes them directly, but living at the app/service layer matches the spec's
  unit boundary (DriveCalibrationService owns detect/persist/load, consumed by the reader).
