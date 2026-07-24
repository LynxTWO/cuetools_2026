# Design: rip-accuracy SCSI features + tamper-evident rip logs

Date: 2026-07-10. Status: approved in brainstorm, pending written-spec review.
Scope: three drive-level accuracy features (cache defeat, lead-in/lead-out overread,
adaptive read speed) built on the existing `Bwg.Scsi` primitives, unified under a
hybrid per-drive calibration model; plus a tamper-evident rip-log scheme.

This is a design, not an implementation. It sequences AFTER the base rip flow
(R12 Phase 3). It does not change the shipping CUERipper; it targets the new WPF app and
the `CDDriveReader` SCSI layer, which now also builds for net8.0-windows (Phase 1).

## Goals

- Make secure ripping measurably more accurate by defeating the drive cache, reading the
  true first/last samples, and reading at a speed the disc can sustain cleanly.
- Never re-probe a drive it has already characterized (owner decision: hybrid).
- Degrade honestly: when a drive cannot do something, say so in the UI and the report
  rather than silently faking it.
- Give every rip log tamper evidence, so an "accurately ripped" claim can be trusted.

## Non-goals

- No new SCSI command plumbing. Every primitive already exists in `Bwg.Scsi/Device.cs`
  (`Read(force,...)` FUA, `ModeSense`/`ModeSelect`, `ReadCDDA`/`ReadCDAndSubChannel` at
  raw LBA, `SetCdSpeed`/`SetStreaming`/`GetSpeed`).
- No dual-drive cross-verification here (separate future feature).
- Not changing the CTDB server contract in this spec; see the log-integrity dependency.

## The hybrid per-drive calibration model

One `DriveCalibration` record per physical drive, keyed by the drive's AccurateRip
signature (`ICDRipper.ARName`), stored next to the existing per-drive settings
(`CUERipperConfig` already keys `DriveOffsets` / `DriveC2ErrorModes` / `ReadCDCommands`
by that signature). New fields:

| Field | Meaning |
|---|---|
| `CacheDefeat` | method + parameters (see Cache defeat) |
| `Overread` | { leadIn: bool, leadOut: bool } |
| `MaxSpeedKbps` | highest speed that read a disc cleanly during calibration |
| `Confidence` | per-feature label: Confirmed / Estimated / Unconfirmed |
| `CalibratedUtc`, `RipperVersion` | provenance |

Calibration runs once, the first time a drive is seen, transparently during the first
rip (the cache/overread/speed probes work on whatever disc is inserted - they do not need
a known-AccurateRip disc, unlike offset calibration). A manual "Recalibrate" button
re-runs it (for firmware changes or a swapped drive). Results and their confidence labels
show in the Drive & Read panel. A normal rip reads the saved record and never probes.

Unit boundaries: `DriveCalibrationService` (owns detect + persist + load), consumed by
`CDDriveReader`. Each feature below is an independent strategy the service produces and
the reader consumes; each can be understood and tested on its own.

## Feature 1: cache defeat

Problem: on a re-read of the same sectors a drive may return a cached copy, so a secure
"re-read" is not a real read. Measuring cache size reliably is not possible; defeating the
cache without measuring it is. Strategy, chosen once at calibration:

1. **FUA test.** Issue `Read(force:true, ...)` for one sector twice and time both. If both
   take a full media-read time (no speed-up on the second), the drive honors Force Unit
   Access for audio -> `CacheDefeat = FUA`. Cheapest; nothing extra at rip time.
2. **Flush sizing (EAC-style), when FUA is not honored.** Estimate the eviction size by
   timing: read sector X, read a flush region of size S far away, re-read X; if the re-read
   is fast, S did not evict -> grow S; binary-search the smallest S that evicts. Save
   `CacheDefeat = Flush(S + margin)`.
3. **Conservative fallback.** If the estimate is inconclusive, `CacheDefeat = Flush(8 MB)`
   - larger than any real optical read cache. Correctness over speed.

At rip time, before each secure re-read the reader applies the saved method (FUA bit, or an
unrelated over-read of the saved flush size). `ModeSelect` of the caching mode page (RCD
bit, page 0x08) is attempted opportunistically as a bonus but never relied on.

Confidence label: FUA -> Confirmed; sized flush -> Estimated; conservative flush ->
Unconfirmed. Surfaced verbatim in Drive & Read and the report.

## Feature 2: lead-in / lead-out overread

Problem: with a nonzero read offset the true first samples come from the lead-in and the
true last samples from the lead-out; a drive that cannot read there forces zero-fill, which
breaks AccurateRip on track 1 and the final track.

Calibration probe: `ReadCDDA` one sector before the program area (into lead-in) and one
past lead-out. If the drive returns data without error, set the corresponding
`Overread.leadIn` / `Overread.leadOut` true.

At rip time: when overread is available, the offset-shifted boundary samples are read from
lead-in/lead-out instead of zero-filled. When it is not, zero-fill as today AND record
"boundary samples zero-filled (drive cannot overread lead-in/out)" in the report, so the
result is honest rather than a silent inaccuracy.

## Feature 3: adaptive read speed

Default `Auto`: start at the drive's max reported speed; on a cluster of C2 flags or
pass-to-pass mismatches, drop one step from the drive's `GetSpeed` list; after a clean
region, ease back up. The speed timeline in the UI shows the drops (educational + true).

Per-drive `MaxSpeedKbps`: during calibration, find the highest speed that reads a sample
region with zero C2 errors; cache it as the Auto ceiling. Manual override: a dropdown of
this drive's real supported speeds (from `GetSpeed`) plus Auto and Max - no arbitrary
fractions, since drives expose discrete speeds. Applied via `SetCdSpeed` / `SetStreaming`.

## Tamper-evident rip log (model B: self-check + authoritative record)

Two layers, because a hash embedded in an open-source app's own log is only casual-tamper
evidence (a determined editor can recompute it).

1. **Local self-checksum.** Compute SHA-256 over the log's canonical text: UTF-8, LF line
   endings, with the checksum footer line excluded. Embed as a footer, e.g.
   `==== Log integrity: SHA-256 <hex> ====`. Verifying recomputes and compares; a mismatch
   means the text was altered. Always available, offline. Lives in `CUESheetLogWriter`
   (write) and a `LogIntegrity` verifier (check).
2. **Authoritative record.** The log's verifiable claims (per-track CRCs, AccurateRip and
   CTDB confidences) are re-checked against the databases, which independently know the
   correct values for the disc. A forged log that fakes accuracy fails this cross-check
   even if its self-checksum is valid. Where the CTDB server can store the log's SHA-256
   against the disc id, the log checker also compares the embedded hash to the stored one
   for a direct match.

Verification surfaces in the Verify & Repair mode's "Check a rip log" action and in the
Report: "Log integrity: self-check OK; results confirmed against CTDB (conf 47) /
AccurateRip".

### Dependency / risk (log integrity)

The stored-hash direct match needs the CTDB server (db.cuetools.net, third-party) to record
an arbitrary log hash; that is not in today's submit contract. Two mitigations, in order:
(a) ship the local self-checksum plus the re-verify-against-CTDB/AccurateRip cross-check
first - this already delivers the anti-forgery guarantee for the rip *results* with no
server change; (b) coordinate an upstream CTDB field, or a CUETools-2026 log-registry
endpoint, to store the hash for a direct match later. The spec does not block on (b).

## Data / persistence

- Extend the per-drive record (add `DriveCalibration` alongside the existing per-drive
  dictionaries). One settings service reconciles it with `CUEConfig` / `CUEConfigAdvanced`
  per the unified-app IA.
- Log checksum is text in the log file; no schema change.

## UI surfacing

- **Drive & Read** panel: per-feature status + confidence (Cache: FUA / flush N MB /
  unconfirmed; Overread: lead-in/lead-out yes/no; Max speed; Offset), a Recalibrate button.
- **Rip** view: the speed graph reflects adaptive speed; the confidence map already
  reflects the C2 vote.
- **Report**: cache/overread/offset/speed used, honest zero-fill note if applicable, and
  the log-integrity result + embedded SHA-256.

## Testing

- Headless (this box, no disc-dependent paths): unit-test `LogIntegrity` (hash canonical
  form, tamper detection), the speed-step state machine, the flush-size search logic
  (against a simulated cache-timing oracle), and the calibration-record persistence.
- On hardware (owner's ASUS BW-16D1HT + disc): the FUA/overread/speed probes and a full
  secure rip with cache defeat on; confirm AccurateRip verifies track 1 and the last track
  (the overread payoff).

## Sequencing

After R12 Phase 3 (base rip flow through `CUESheet`). Order within: (1) log integrity
self-check (pure, testable now), (2) cache defeat, (3) overread, (4) adaptive speed,
(5) the authoritative log record once a server field exists.

## Live-wiring finding (2026-07-24): mid-read SET CD SPEED crashes the read path

First attempt at Feature 3 (adaptive read speed) wired the AdaptiveSpeedController into RipService:
the two SCSI primitives (`GetSupportedSpeeds`, `SetReadSpeed`) were added to `CDDriveReader`, the
initial speed set at rip start, and speed changes applied mid-read from the `ReadProgress` handler
on stuck windows / clean stretches. Results on the ASUS BW-16D1HT:

- **Init works.** GetSpeed returns only the drive's MAX (one descriptor), so we synthesize the
  standard CD ladder from it: "adaptive speed on: 9 steps 704-8468 kB/s, start 8468" (48x). Setting
  the initial speed before reading is safe.
- **Mid-read SET CD SPEED CRASHES.** ~18 s into a verify on the pin-holed disc, at the first stuck
  window where the handler issued SET CD SPEED, the very next READ CD failed with
  `SCSIException: illegal request: INVALID FIELD IN CDB` (FetchSectors). Because that read runs on a
  background thread with no catch, the unhandled exception TERMINATED the app.

Two root causes, both to fix before Feature 3 ships:
1. **SET CD SPEED must not be issued concurrently with, or interleaved badly against, an in-flight
   READ CD.** The `ReadProgress` event and the prefetch read are not the same thread / not
   serialized against the device handle. The speed change has to be applied INSIDE the reader,
   between reads, on the read thread (e.g. a volatile "pending speed" the read loop consumes), not
   from an external event handler. It's also possible the drive rejects READ CD immediately after a
   CLVandNonPureCav SET CD SPEED and needs the read mode re-established - needs a per-drive probe.
2. **A read-thread SCSI exception terminates the whole app** (no handler on the AudioPipe/prefetch
   thread). This pre-existing fragility should be caught and surfaced as a failed rip, not a crash -
   independent of Feature 3, and worth its own fix.

Status: reverted to stable. Feature 3 needs the serialized-apply redesign + the read-thread crash
guard before another live attempt. The two primitives and the synthesized-ladder logic are sound
and saved (scratchpad SCSIDrive-adaptivespeed-attempt.cs / RipService-adaptivespeed-attempt.cs).
