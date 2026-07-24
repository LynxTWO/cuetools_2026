# Secure re-read observation diagnostic - design

Date: 2026-07-24
Status: approved, pending implementation
Scope: CUETools.Ripper.SCSI (CDDriveReader) + CUETools.Wpf (RipService). Read-only instrumentation.

## Plain English

On a scratched or pinholed disc, the secure ripper re-reads a stuck window many times to
confirm the audio. Watching the live error count on the pinholed test disc, it does not just
count down and stop. It sawtooths: a window converges to about 190 disagreeing sectors, then a
single pass jumps the count back to the full window (2400), then converges again, then jumps
back. Progress is being made and then undone.

Before changing the recovery algorithm, we instrument it so the fix is designed from measured
numbers, not a guess. This spec covers ONLY the measurement. It changes no rip decision and no
output byte. The actual recovery fix (quarantine slip passes, slow the drive on stuck windows,
re-read only the still-bad sectors) is a separate spec that consumes the log this one produces.

## What the sawtooth already tells us (receipts)

- 2400 is the window size, not a sector count: `const int MSECTORS = 2400`
  (CUETools.Ripper.SCSI/SCSIDrive.cs:52). "2400 disagree" means every sector in the re-read
  window was flagged on that pass - one read came back wholesale-wrong.
- The re-read loop runs up to `max_scans = 16 << correctionQuality` passes (32 at Secure,
  correctionQuality 1) and breaks early when `_currentErrorsCount == 0`
  (SCSIDrive.cs:1333, 1370). So the cap is a give-up ceiling, not a fixed retry count.
- The decision is a weighted majority vote per sample: a C2-clean read of a bit counts 128, a
  C2-flagged read counts 1, and a sample is trusted only when its winning margin clears
  `er_limit = 128 * (1 + correctionQuality) - 1`, i.e. at least `(1 + correctionQuality)` clean
  agreeing passes (SCSIDrive.cs:1224-1234). A slipped/garbage pass gets folded into that same
  vote, which is what resets the count.

Inference (to be confirmed by this diagnostic): the 2400 passes are the drive losing streaming
sync near the damage and returning a misaligned read, most likely worse at the high adaptive
read speed. A blind higher pass cap would just stop on a random tooth of the sawtooth.

## Goal

Produce one `rip.recovery` log from a single verify of the pinholed disc that quantifies, per
re-read window:

- the full per-pass sawtooth, separating genuine remaining damage from slip passes,
- how many passes and how much recovery the slips cost (min error count reached vs. where the
  window was capped),
- whether slip rate correlates with drive speed.

That log is the input to the recovery-fix spec.

## Non-goals

- No change to the correction/vote logic, the pass cap, the early-break, or any output byte.
- No recovery behavior change (no quarantine, no speed policy, no targeted re-read) - that is
  the next spec.
- No new persisted files; the data goes to the existing diagnostic log only.

## Design

Two observation points. Neither feeds a decision.

### A. In-ripper per-pass counter (read-only), SCSIDrive.cs

Today `CorrectSectors` maintains only the running consensus count `_currentErrorsCount`, which
is delta-adjusted across passes (SCSIDrive.cs:1270-1276). That running count cannot distinguish
"190 sectors genuinely still bad" from "a slip just re-flagged the whole window," because both
show up as a high number.

Add one field, `_thisPassErrors`:

- Reset to 0 at the top of each pass in `PrefetchSector`, before the sector loop
  (near SCSIDrive.cs:1334).
- Incremented once per sector whose `fError` is true on this pass, inside `CorrectSectors`
  (the existing per-sector `fError` at SCSIDrive.cs:1247, 1270).

It is strictly read-only: it increments an int and reads nothing that feeds `bestValue`,
`currentData`, `_currentErrorsCount`, `m_retryCount`, or the early-break condition. On a good
aligned pass its value is the true remaining-damage count; on a slip it is near the full window.
That single number is the signal the running count cannot provide.

The value rides out on the existing `ReadProgress` event by adding one field to its args
(alongside `Pass`, `ErrorsCount`, `PassStart`, `PassEnd` at SCSIDrive.cs:1358-1368), for example
`ThisPassErrors`.

### B. App-layer logging, RipService.cs

The `ReadProgress` handler already runs in RipService (for adaptive speed and level metering).
It also logs recovery telemetry to a dedicated `rip.recovery` category:

Per pass, on a pass or window boundary (Pass increments or PassStart changes):

```text
rip.recovery window=<lba> pass=<n> running=<x> fresh=<y>/<windowSize> speed=<s>x slip=<0|1>
```

Per window, on advance to a new window:

```text
rip.recovery window=<lba> DONE passes=<n> converged=<0|1> minFresh=<m> slipPasses=<k> speed=<s>x
```

Definitions:

- `fresh` is `ThisPassErrors` (Observation A). `running` is the existing `_currentErrorsCount`.
- A per-pass line is only emitted for passes that actually run the vote, i.e.
  `pass >= correctionQuality`, since `CorrectSectors` (and therefore both counters) does not run
  on the earlier raw passes (SCSIDrive.cs:1353). Earlier passes carry no error signal to log.
- The per-window summary normally fires when the window advances. The final window of the disc
  never advances, so its summary is flushed when the read completes, so no window is dropped.
- `windowSize` is `PassEnd - PassStart`.
- slip = `fresh >= 0.85 * windowSize`. Measured on the fresh counter, which is the correct
  signal for a wholesale slip.
- `converged` = the window reached 0 errors before the cap; otherwise it was capped.
- `speed` is the current adaptive read speed in multiples of 176 kB/s.

Privacy: numbers only. No album, artist, track, username, or path. This keeps the diagnostic
log's existing privacy invariant (see DiagnosticLog).

## Bit-exact safety gate (non-negotiable)

The output audio must be byte-for-byte identical before and after this change. Two lines of
proof:

1. By construction: the counter and logging are read-only. Verify by inspection that no added
   line writes to any variable that feeds the correction decision.
2. Deterministic replay: the author's offline harness ("the algorithm TestRipper exercises
   offline", SCSIDrive.cs:1233) replays fixed read data through `CorrectSectors`. Identical
   harness output before and after is a deterministic proof the decision path is unchanged.
   Confirm this harness still exists during implementation and use it as the gate.
   Fallback if the harness is gone: a clean-disc (Genesis) AccurateRip/CTDB result identical to
   the pre-change build. Damaged-disc rips are not reproducible read-to-read, so they are never
   byte-compared.

## Deliverable

- `_thisPassErrors` counter + one new `ReadProgress` arg (SCSIDrive.cs).
- `rip.recovery` per-pass and per-window logging in RipService.
- One captured `rip.recovery` log from a pinholed-disc verify.
- Confirmation that the bit-exact gate passed (harness or clean-disc AR/CTDB).

## Follow-up (separate spec, out of scope here)

The recovery fix designed from this log: quarantine a slip pass so it does not pollute the vote
or reset progress; slow the drive on a stuck window via real SET CD SPEED; re-read only the
still-erroneous sectors so extra passes are cheap; and a progress-aware plateau rule that
replaces the blind pass cap.
