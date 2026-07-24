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

## Feature 3 SHIPPED (2026-07-24) - adaptive read speed, with both fixes

The two required fixes from the previous finding are done and the feature is proven on the
pin-holed disc:

- **Fix A - read-thread crash guard.** `AudioPipe.Decompress`'s try/catch is now unconditional
  (was `#if !DEBUG`), so a SCSI read failure mid-rip becomes a clean exception rethrown on the
  consumer thread (a failed rip) instead of an unhandled background-thread exception that
  terminates the process. Same pattern as backlog R17.
- **Fix B - serialized speed apply.** `CDDriveReader.RequestReadSpeed(kbps)` only STORES a pending
  value (volatile); the read loop applies it in `PrefetchSector` at the next fresh-window boundary
  (on the read thread, after TestReadCommand, before any FetchSectors of that window) - exactly
  where the original author's commented SetCdSpeed sat. No more mid-window SET CD SPEED, so no more
  INVALID FIELD IN CDB. A refused command is swallowed and stops further attempts.
- **Policy.** RipService builds an `AdaptiveSpeedController` from the synthesised ladder (steps
  clearly below the drive's reported max, then the true max on top - no near-duplicate top step),
  requests one step down when a stuck window starts, and eases up one step per clean ~5% stretch.
  Setting: "Adaptive read speed" (on by default), persisted.

Live proof on the pin-holed disc (ASUS BW-16D1HT): init "9 steps 704-8467 kB/s, start 48x", then
across the 10-13% pin-holed zone the speed stepped 48x -> 40x -> 32x -> 24x through hundreds of
errors and several "unreadable by drive" give-ups, with ZERO crash lines (attempt 1 died at the
first stuck window). Verify stopped cleanly by the user after 219 s.

Still open in this program: Feature 1 (cache defeat, touches the data path), Feature 2 (lead-in/out
overread), and the per-drive calibration record + Drive & Read surfacing.

## Calibration foundation SHIPPED (2026-07-24) - cache-behaviour probe + Drive & Read surfacing

The per-drive calibration record is now real and populated by a read-only probe:

- `CDDriveReader.Probe()` (new, read-only): after TestReadCommand autodetects the drive's read
  command, it warms a mid-disc audio region, times an immediate re-read (cached => fast), reads a
  ~6.5 MB unrelated region to flush the cache, then times the target again (a real media read if
  evicted). Every ReadCDDA/ReadCDAndSubChannel status is checked (a failure => Probed false =>
  honest "Unconfirmed"). Reads audio sectors only; writes nothing; runs OUTSIDE a rip under the app
  SCSI gate. NOTE: the probe MUST use the autodetected read command (ReadCdBEh/D8h with the
  drive's main-channel + C2 mode) and the matching per-sector buffer (2352 + C2 size); plain
  ReadCDDA fails on drives that need READ CD (which is why the first attempt read 0-1 ms garbage).
- `DriveCalibrationService` maps the probe to a `DriveCalibration` (CacheDefeat + confidence,
  MaxSpeedKbps) keyed by AR name, persisted to drive-calibration.json.
- Drive & Read: a "Calibrate" button (needs a disc) + a CALIBRATION panel (cache behaviour,
  max speed, confidence, timestamp).

Live proof: on the ASUS BW-16D1HT the probe correctly found **"Caches re-reads - flush needed"
(Estimated)** - warm re-read 1 ms vs post-flush media read 388 ms - and persisted it. This is
exactly the drive fact that says a secure re-read there is not genuine without cache defeat.

Still to do in this program (all documented, not blocking): the flush-SIZING search
(CacheDefeatSearch core exists; needs the timing oracle wired), the overread lead-in/out probe
(finicky addressing), and the DATA-PATH APPLICATION - actually flushing before each secure re-read,
and reading boundary samples from lead-in/out - which touches the rip read loop and needs bit-exact
verification, the careful part.

## Cache-defeat DATA-PATH application: ATTEMPTED, REVERTED (2026-07-24)

Plain English: turning cache defeat on in the rip read loop was too aggressive and destabilized
the drive. It is backed out. The read-only calibration above (probe + display) stays; only the
data-path flush is removed. The crash guard did its job - the failure surfaced as a failed rip,
not an app crash.

What was tried: `CDDriveReader.SetCacheDefeat(flushSectors)` plus a `FlushCache()` that read a
~6.5 MB unrelated region into a scratch buffer (audio never touched) before a secure re-read pass,
injected in PrefetchSector at `pass > 0`, enabled from calibration when the drive caches re-reads.

Why it failed (measured): at Secure (correctionQuality = 1) EVERY window is read twice for the
comparison (pass 0 + pass 1), so `pass > 0` fired the 6.5 MB flush before every single window
across the whole disc - not just before error-recovery re-reads of stuck windows. That is both far
too slow and, on the ASUS BW-16D1HT, it put the drive into a state where the NEXT window read was
rejected with `INVALID FIELD IN CDB`. A whole-disc verify failed at 18 s (07:29 run). The crash
guard (unconditional AudioPipe catch, shipped with Feature 3) turned it into "rip failed after 18s"
with the app still alive - exactly the graceful-failure design. An earlier run survived 318 s before
being stopped, so the destabilization is intermittent, which is why it was not caught immediately.

What it needs to be done right (follow-up, not blocking):
- Flush SIZING first (CacheDefeatSearch core exists): find the SMALLEST region that actually evicts
  the target, so a flush is milliseconds, not ~6.5 MB, before it is ever put in the read loop.
- Gate the flush to ACTUAL error-recovery re-reads (pass > correctionQuality), not the normal
  secure 2-pass comparison - or restructure to re-read stuck spots after a full pass.
- Re-test that the drive tolerates the flush read interleaved with window reads at each speed,
  and that the whole disc still verifies bit-exact.

The attempt is saved under scratchpad (SCSIDrive-cachedefeat-attempt.cs, RipService-cachedefeat-attempt.cs)
for the redo; it is NOT in the tree.
