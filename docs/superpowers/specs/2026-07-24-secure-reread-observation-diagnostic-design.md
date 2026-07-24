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
   offline", SCSIDrive.cs:1233) was meant to replay fixed read data through `CorrectSectors`.

   FINDING (2026-07-24, during implementation): TestRipper is NOT usable as a gate as it stands.
   It reads fixed dumps from a hardcoded `Y:\Temp\dbg\960\` path that does not exist on this
   machine, and it re-implements the vote math inline (CUETools/TestRipper/CDDriveReaderTest.cs:148-175)
   rather than calling the production `CorrectSectors`, so it proves nothing about the real code
   path. Making it a real gate would mean adding fixtures and exposing `CorrectSectors` for test,
   which is its own task, out of scope for this read-only diagnostic.

   Gate actually used:
   - Code inspection (conclusive for this change): `_thisPassErrors` is written in exactly three
     places - reset to 0 at each pass top, `++` on a flagged sector, and copied to
     `progressArgs.ThisPassErrors` - and read in exactly one - that same copy. It never appears in
     `bestValue`, `currentData`, `_currentErrorsCount`, `m_retryCount`, or the early-break, so the
     audio and the secure decision are unchanged by construction.
   - Clean-disc corroboration: a Genesis (clean) verify must still report AccurateRip-accurate
     after the change, confirming the normal audio path is intact. Damaged-disc rips are not
     reproducible read-to-read, so they are never byte-compared.

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

## Findings (2026-07-24 pinholed-disc run)

The instrument ran the whole pinholed disc (Surround Sounds, 24 tracks) end to end. Final result:
`ar_conf=0/82 accurate=False reread_windows=14 failed_windows=8`, elapsed 2611 s. That matches the
pre-diagnostic baseline (15 reread / 10 failed) within run-to-run variance, which corroborates that
the read-only instrumentation changed nothing about the rip (on top of the code-inspection proof).

The 14 re-read windows, categorized:

| Mode | Windows (minFresh) | Signature | More passes help? |
|------|--------------------|-----------|-------------------|
| Converged | 33600, 48000, 184800, 264000, 288000, 290400 | resolved within the 30-pass cap | n/a |
| Near-miss capped | 259200 (2), 266400 (3), 36000 (5) | monotonic descent, cut off 2-5 short | YES (few more passes) |
| Slow-converging capped | 40800 (110), 38400 (158), 43200 (201) | steady descent, still dropping at the cap | YES (many more passes) |
| Persistent slip | 45600 (2250) | pegged ~2400, slipPasses=30, never a consensus | NO |
| Oscillating | 285600 (1172) | hard floor ~1172 + a ~125-sector band flip-flopping 1172<->1297 | NO (marginal reads are unstable) |

Key reads:

- 6 of the 8 FAILED windows (the near-miss + slow-converging groups) were still genuinely
  converging when the blind 30-pass cap cut them off. A progress-aware cap would likely rescue
  them, which could move the disc from 0/82 toward an AccurateRip match. This is the primary win.
- 1 window (45600) is a persistent slip: 30 of 30 passes near-full, no consensus ever forms.
- 1 window (285600) oscillates: a floor of genuinely-dead sectors plus a marginal band whose reads
  come back different every pass, so those never get two clean agreeing reads.
- The vote is robust to a TRANSIENT slip: window 288000 converged in 5 passes despite slipPasses=1.
  So the recovery fix should react to PERSISTENT slips, not the occasional one.

Correction to the design's premise: the added `_thisPassErrors` counter came out equal to the
running `_currentErrorsCount` on every line, because `fError` is a consensus verdict recomputed each
pass, not a comparison of this one raw read against the consensus. It still flags a consensus-level
slip (count near-full), which is what "2400" looks like in the UI, and that flag correctly caught
45600. But telling a recoverable slip (jitter/misalignment) from an unrecoverable one (destruction)
needs a different measurement - cross-correlating consecutive RAW reads - which belongs to the
recovery-fix spec, not here.

Implication for the recovery fix: a progress-aware cap (keep going while minFresh is still falling,
stop on a plateau) addresses 6 of 8 failures; a persistent-slip detector plus a raw-read
correlation probe (jitter vs destruction) addresses the other 2. Slip QUARANTINE is unnecessary -
the vote already absorbs transient slips.
