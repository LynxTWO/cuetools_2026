# Deep recovery - design

Date: 2026-07-24
Status: approved, pending implementation plan
Scope: CUETools.Ripper.SCSI (CDDriveReader), CUETools.Wpf (RipService, RipViewModel, RipView,
DriveCalibration). Opt-in behavior; the default rip path is unchanged.

## Plain English

On a scratched or pinholed disc the secure ripper re-reads a stuck window up to a blind cap of
`16 << correctionQuality` passes (32 at Secure), then gives up. The observation diagnostic
(2026-07-24-secure-reread-observation-diagnostic-design.md) measured what that cap throws away:
of 8 failed windows on the pinholed test disc, 6 were still genuinely converging when the cap cut
them off (three within 2-5 sectors of clean), 1 was a persistent slip (pegged near-full, no
consensus ever forming), and 1 oscillated (a floor of dead sectors plus a marginal band that
flip-flops).

Deep recovery is an opt-in mode that recovers more of those windows without ever risking the
audio. It has two parts. Part 1 (progress-aware cap + slow-to-floor) is a pure loop-control and
read-speed change - it recovers the 6 converging windows and cannot alter the output. Part 2 (the
slip-recovery probe) is the one data-path change: it tries to rescue a persistent-slip window by
detecting and correcting read misalignment, backed by a safety argument that makes accepting wrong
data impossible.

This spec is the design. The bit-exact behavior and the failure-mode receipts come from the
diagnostic spec and its recorded findings; they are the foundation and are not repeated in full.

## Foundation (from the diagnostic run, 2026-07-24)

- The re-read loop lives in `CDDriveReader.PrefetchSector` (SCSIDrive.cs). `max_scans = 16 << cq`
  is the blind cap (SCSIDrive.cs:1333); it breaks early only when `_currentErrorsCount == 0`
  (SCSIDrive.cs:1370). The per-sample vote is in `CorrectSectors` (SCSIDrive.cs:1224-1278).
- Adaptive read speed (Feature 3) already applies SET CD SPEED at fresh-window boundaries via a
  pending value (never mid-window - that crashed the drive). Deep recovery reuses that same path.
- Per-drive calibration already exists (DriveCalibration + DriveCalibrationStore, keyed by AR
  name), with a max-speed field. Deep recovery adds a min-speed field to it.
- Windows categorized: 6 converging-but-capped (min 2-201), 1 persistent slip (45600, slipPasses
  30/30, min 2250), 1 oscillating (285600, floor ~1172 + marginal band). A transient slip is
  harmless: window 288000 converged in 5 passes despite slipPasses=1, so the vote already absorbs
  the occasional slip.

## Control model: the Deep recovery toggle

A "Deep recovery" toggle on the Rip page, next to the Secure/Paranoid picker, OFF by default. It
is orthogonal to the quality mode (Secure + Deep recovery is a valid combination). When OFF, the
existing `16 << cq` cap and read path run exactly as today - byte-for-byte unchanged, zero risk.
Everything in Parts 1 and 2 happens only when the toggle is ON.

Persisted in AppSettings like the other rip options. Plumbed to the reader as a single flag
(for example `reader.DeepRecovery = true`) set before the rip starts, alongside CorrectionQuality.

## Part 1: progress-aware cap + slow-to-floor (no output change)

### Progress-aware cap

When Deep recovery is on, replace the blind ceiling for a stuck window with a progress rule.
Track `bestErrors` = the lowest `_currentErrorsCount` this window has reached. Keep re-reading
while it is still improving; stop when any of:

- `_currentErrorsCount == 0` (converged - identical to today's early break), or
- `bestErrors` has not improved for **8 consecutive passes** (plateau), or
- the window has spent **120 seconds** of wall-clock time (hard ceiling).

Against the diagnostic data this recovers all six near-miss and slow-converging windows (they
were still descending at the old cap) and bails out of the persistent-slip and oscillating windows
in about the time they already take (their `bestErrors` flatlines fast, so the 8-pass plateau
fires). The plateau rule is what fail-fasts the hopeless windows; the time ceiling guarantees a
slow drive cannot grind forever.

Numbers (plateau = 8, ceiling = 120 s) are starting points chosen from the run; the implementation
keeps them as named constants so they are easy to retune with more discs.

### Slow-to-floor

A stuck window whose `bestErrors` stalls at the current speed gets the drive stepped DOWN toward
its slowest speed before Deep recovery gives up. Slow reads track scratches and marginal pits
better than fast ones. This reuses the Feature 3 SET CD SPEED path (applied only at fresh-window
boundaries, the proven-safe point) - it is a speed change, not a data change, so a re-read at 4x
returns the same bytes as at 48x.

Minimum-speed probe (answers the earlier owner request to detect the drive's floor): step SET CD
SPEED down (48x, 24x, ... 4x, 2x, 1x) until the drive refuses the command or the achieved read
rate stops falling; that lowest accepted speed is the floor. Store it in the DriveCalibration
record next to MaxSpeedKbps (new field, for example MinSpeedKbps), so it is probed once per drive
and reused. Runs read-only, outside a rip, under the app SCSI gate - same shape as the existing
cache/behavior probe.

### Why Part 1 is bit-safe

It changes only WHEN to stop re-reading and WHAT speed to read at. The vote (`CorrectSectors`) and
the accepted bytes are untouched. Whenever today's rip succeeds, Part 1 produces the identical
result; it only adds passes that can find MORE agreement, never different agreement.

## Part 2: the slip-recovery probe (one data-path change, safety-critical)

Aimed at the persistent-slip window (45600): the whole window disagrees every pass and no
consensus forms. Hypothesis: this can be JITTER - the drive returns the same good audio shifted by
some samples each pass (lost streaming sync near the defect), so a sample-by-sample vote disagrees
everywhere even though the data is fine. If so, detecting and correcting the shift recovers it. If
not (genuine destruction), nothing aligns and the probe gives up cleanly.

Not aimed at the oscillating window (285600): its marginal band is individual unstable sectors,
not a whole-window shift, so slow-to-floor (Part 1) is the right tool there, not re-alignment.

### Mechanism

1. Trigger only when Deep recovery is on AND a window is a PERSISTENT slip - error count pegged at
   `>= 0.85 * windowSize` for several consecutive passes with no plateau-improvement (the same
   `slip=1` signature that caught 45600). Never runs on normal or converging windows.
2. Cross-correlate two raw reads of the window (before they are folded into the vote) to find the
   sample offset that best aligns them. A strong, consistent correlation peak means jitter (shifted
   copies); a flat correlation means destruction (unrelated garbage).
3. If jitter at offset N: shift subsequent raw reads by N before folding them into the vote, so
   they line up and consensus can form.

### The safety guarantee (the whole ballgame)

Re-alignment can help reads AGREE but can never FAKE agreement:

- The accepted audio is still validated by the SAME `(1 + correctionQuality)` clean-agreeing-reads
  vote in `CorrectSectors`. Re-alignment only re-positions reads so they CAN agree; it does not
  lower or bypass the bar.
- Offset right (real jitter): re-aligned reads genuinely agree on the true data -> accepted,
  agreement-backed, correct.
- Offset wrong (spurious peak, or it was really destruction): re-aligned reads do NOT reach clean
  agreement -> the sector stays flagged -> not accepted.
- So the worst case of a mis-detected offset is "we still fail to recover" - never "we accept wrong
  data." Accepted bytes are always agreement-validated, exactly as today.

Guards: require the offset to be consistent across multiple reads (real jitter has a stable offset
relative to the true position), require the correlation peak to clear a strength threshold before
it is trusted, and require the re-aligned reads to still clear the full clean-agreement vote.

### Where it touches code

One spot: in `FetchSectors`, before `ReorganiseSectors` folds a raw read into `UserData` /
`C2Count`, apply the detected shift. That is the only data-path change in the whole design, gated
behind BOTH the toggle and the persistent-slip trigger. On normal or converging windows the probe
is never invoked; on aligned reads the detected offset is 0, so it is a no-op.

## Bit-exact safety gates

Part 1 (loop control + speed): code inspection that the vote and accepted bytes are untouched, plus
the clean-disc check below.

Part 2 (the data-path change) earns a stronger gate:

1. The agreement backstop (above): accepted data is always agreement-validated, so wrong data
   cannot be accepted.
2. Off-path identity: Deep recovery off -> the probe is never entered -> byte-identical to today.
3. Clean-disc test: a Genesis (clean) disc must still verify AccurateRip-accurate with Deep
   recovery ON (offset detection finds 0 on aligned reads -> no-op on clean audio).
4. The AccurateRip oracle: if Deep recovery turns the pinholed disc from `0/82` toward a match,
   AccurateRip (millions of independent rips) has confirmed the recovered bytes are correct - the
   rescue is self-validating. If it does not match, nothing was corrupted; the sector stays
   flagged / interpolated exactly as today.

## UI

- A "Deep recovery" toggle on the Rip page near the quality picker, off by default, with a short
  caption that it trades time for recovery on damaged discs.
- While a window is in deep recovery, the existing re-read visualization (RereadScope / the codec
  scope veil) shows the extra state: pass count, best-so-far error count, current speed, and
  "re-aligning" when the slip probe is active - so a long grind reads as deliberate work.

## Non-goals

- No change to the default (toggle-off) rip path.
- No change to the vote / secure decision itself. Deep recovery changes when to stop, what speed to
  read at, and (Part 2 only) the ALIGNMENT of raw reads before the unchanged vote.
- Not aimed at rescuing the oscillating marginal-band window via re-alignment (Part 1 covers what
  is coverable there).

## Risks and honest caveats

- The ASUS BW-16D1HT is largely "accurate stream," so real jitter may be rare and Part 2's payoff
  could be one window or none. The probe handles that correctly (no consistent offset -> clean give
  up), so it is safe regardless; the payoff is upside, not a guarantee.
- Cross-correlation over a 2400-sector window is compute-heavy. It fires only on rare persistent
  slips and will be multithreaded (per the standing perf directive), with a weak-hardware fallback
  of simply skipping the probe (degrades to Part 1 behavior on that window).
- Plateau (8) and ceiling (120 s) are first estimates from one disc; kept as named constants for
  retuning.

## Follow-ups (out of scope here)

- Retune the plateau / ceiling / slow-to-floor step schedule against more damaged discs.
- Consider a targeted re-read (only still-erroneous sectors) to make extra passes cheaper - a pure
  optimization, safe to add later.

## Implementation finding (2026-07-24): Part 2 re-scoped to detection-only

Plain English: while implementing the slip-recovery re-alignment, the "never corrupt" safety
argument turned out to have a hole. The auto-apply shift is deferred; the read-only classification
half shipped.

The hole: the safety rested on "the unchanged clean-agreement vote is the sole gate, so a wrong
offset just fails to recover." But the vote validates AGREEMENT, not correctness relative to the
true audio position. Re-alignment works by shifting reads to match a reference read. If that
reference is itself jitter-shifted by K samples (which is exactly what a slipping drive produces -
every read is at some varying offset), aligning the others to it makes them all agree on audio
shifted by K. The vote sees agreement and accepts it. The result is uniformly shifted - wrong.
AccurateRip catches it (its CRC is position-sensitive), so on a disc that is in AccurateRip the
rescue is self-checking. But a disc NOT in AccurateRip would get a silently-shifted rip. That
violates the standing rule that recovery must never corrupt, so the auto-apply is not shipped.

Shipped instead (read-only, cannot corrupt): `ClassifySlip` raw-reads a persistent-slip window's
first chunk a few times into its OWN buffer and cross-correlates the extra reads against the first
via SlipCorrelator. It never goes through FetchSectors/ReorganiseSectors, so the vote and the audio
are untouched. It classifies the slip - recoverable jitter (strong correlation at a nonzero offset),
identical reads (strong at offset 0), or dead media (weak) - and logs the verdict. This answers the
open question from the diagnostic (is window 45600 recoverable jitter or dead media?) at zero risk,
and tells us whether the gated auto-apply is even worth building.

Deferred (follow-up): the auto-apply shift, and ONLY with a safety gate - either accept a
slip-recovered window solely when the final AccurateRip matches, or mark it "unverified, check
against AccurateRip" so a shifted result can never masquerade as a secure read. Decide after the
detection data shows whether real jitter occurs on this drive.
