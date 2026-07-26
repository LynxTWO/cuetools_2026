# Test & Copy secure rip mode - design

Date: 2026-07-24
Status: approved, ready for implementation plan
Scope: CUETools.Wpf (RipService, a new TestAndCopyResolver, DriveCalibration flow, RipViewModel /
RipView). Builds on shipped pieces: verify-history, cache defeat, Secure + Deep recovery. No changes
to the SCSI reader are required.

## Plain English

Test & Copy proves a rip by reading the disc twice, independently, and only writing files that two
reads agree on bit-for-bit. It is the safety net for discs AccurateRip has never seen: instead of
trusting one secure pass, it produces a rip proven by a second source (a second physical read), the
way certain uploaders required back in the day. On a disc that IS in AccurateRip it adds a second,
independent line of proof on top of the AR match.

The cost is honest: two full reads on a clean disc, three on a mismatch. Test & Copy is inherently
2-3x a normal rip's read time. That is the price of the proof, and it is opt-in per rip.

## Goals

- A rip that is bit-exact-verified by at least two independent physical reads, per track.
- Correct behaviour with no AccurateRip data (bit-agreement is the proof) and with AR data (AR match
  is corroborating evidence, reported separately).
- Never write an unverified file into the output folder without a loud, explicit user choice.
- Never present a false pass: the two reads must be genuinely independent (not the same cached bytes).
- Reuse the existing read pipeline and the verify-history compare logic. Do not reinvent them.

## Non-goals (v1)

- Single-file image + cue output. v1 produces per-track files only, so a track can be sourced from
  whichever read verified it (you cannot swap one track out of one big encoded file without
  re-encoding). Full-image support is v2, planned after v1 is built, fuzzed, and tested.
- EAC-log-format compatibility. v1 writes its own clear Test & Copy log, not an EAC-parseable one.
- Unbounded re-reading. v1 stops at three reads and then holds for a user decision.

## The flow (bounded to 3 reads)

1. Test read: a Verify pass (`CUEAction.Verify`, writes no files). Produces per-track CRCs via
   `cue.ArVerify` (CRC32 = the audio checksum; AR v1/v2 = the AccurateRip checksums).
2. Copy read: an Encode pass to a temp staging folder (staging 1). Produces per-track files + CRCs.
3. Compare Test vs Copy per track (match rule below).
   - Every track agrees -> commit staging 1 to the real output folder. Verdict: "Verified by 2
     independent reads."
   - Any track differs -> one auto third read.
4. Auto third read: an Encode pass to a second temp staging folder (staging 2). Produces per-track
   files + CRCs.
5. Per-track resolve across the three reads. Each track needs an agreeing pair: two reads whose audio
   is bit-identical (CRC32 match, plus the AR check when AR data exists). The committed track file is
   taken from a STAGED read in that pair, preferring the Copy read (staging 1) when it qualifies and
   the third read (staging 2) otherwise - a deterministic choice, and since the reads are bit-identical
   the file is the same either way. The Test read has no audio, but if it agrees with a staged read,
   that staged read is proven, so its file is used.
   - Every track has an agreeing pair -> assemble the output folder (copy each track's verified file
     into place), commit. Verdict: "Verified by up to 3 reads, per track."
   - Any track has no agreeing pair (all three reads disagree on it) -> HOLD: keep both stagings,
     surface the differing tracks, and let the user choose: Re-run Test & Copy, Accept the Copy read
     anyway (written but flagged "not test-verified"), or Discard.

Any pair among three reads includes at least one staged read (the pairs are Test+Copy, Test+Read3,
Copy+Read3), so a resolved track always has audio to commit.

All reads share the same metadata, so each staged read writes identical filenames; assembly maps the
resolver's per-track verdict to files by track index. Auxiliary output files (the `.cue`, any
playlist, embedded cover art) are metadata-derived and identical across reads, so assembly takes them
from staging 1. In the 2-read happy path the whole of staging 1 is committed as-is; only the 3-read
path assembles file-by-file.

## The match rule

Two reads agree on a track when:

- their audio is bit-identical: CRC32 matches (this is the primary proof - identical CRC32 means
  byte-identical decoded audio), AND
- when the disc is present in AccurateRip, their AR verdicts are consistent (both reads produced the
  same AR outcome). With no AR data, bit-identical agreement alone is the proof.

The two checksum contracts are deliberately separate:

- verify-history compares the offset-corrected AccurateRip CRC (v2, falling back to v1), because
  history can compare reads made by different drives at different offsets;
- Test & Copy compares reads from the same drive, offset, and session, so it requires a nonzero equal
  full-range CRC32 and an equal AccurateRip CRC.

The full CRC is mandatory for Test & Copy because AccurateRip intentionally excludes the first and
last five seconds at the disc boundaries. Reusing the history comparator would let differences in
those excluded samples pass. Conversely, replacing the history comparator with raw CRC32 would break
its cross-drive contract. The AR database verdict is reported separately; the locally computed AR
checksum is the corroborating comparison signal.

The AR status is reported separately in the verdict and log, never used to fail a bit-agreed track:

- both reads AR-accurate -> "also AccurateRip-accurate, confidence N".
- disc not in AR -> "not in AccurateRip - proven by two independent reads".
- reads agree with each other but not with AR -> "faithful read; this pressing differs from
  AccurateRip's".

## Independence is mandatory (auto-calibrate)

Two reads only prove something if the second is not served from the drive's cache. Test & Copy
guarantees independence before it starts:

- Non-caching drive (calibrated "Media re-reads") -> reads are naturally independent. Proceed.
- Caching drive, calibrated (`Flush:N`) -> cache defeat is forced ON for EVERY read in the operation
  (the Test read, the Copy read, and the third read each flush before their own secure re-reads), so
  each read's per-track CRC reflects the platter and not its own cache or a prior read's cache. This
  is forced regardless of the Deep recovery toggle - independence is not optional here. Proceed.
- Caching drive, NOT yet calibrated, or drive never calibrated -> run calibration first,
  automatically, behind a modal "Calibrating drive..." dialog. When calibration finishes, proceed
  under the branch that now applies. The user does not have to know to calibrate first; the mode does
  it. (Calibration is the existing ~30 s read-only probe; it cannot affect rip output.)

Quality is forced to at least Secure. Burst is meaningless for Test & Copy, so a Burst selection is
raised to Secure for the operation; Paranoid is honoured if selected.

## Architecture and reuse

- Each read is the existing `RipService.Run(...)`: the Test read is `encode:false`; the Copy and
  third reads are `encode:true` with a TEMP output dir (staging), not the final output folder.
- `Run(...)` today upserts verify-history and writes the `.verify` sidecar per call. For Test & Copy
  those side effects must fire once, on the final committed result, not per intermediate read. Add an
  internal "staging" flag to `Run(...)` that suppresses the verify-history upsert and sidecar; the
  orchestrator does the final upsert, sidecar, and Test & Copy log after assembly.
- New pure resolver `TestAndCopyResolver`: input is the list of per-read records (each carrying
  per-track CRC32 + AR v1/v2 + AR verdict) plus whether the disc is in AR; output is, per track, the
  verdict (agreeing pair or unresolved) and which read index sources the file, plus the overall
  outcome (Passed / Held) and the count of reads used. This reuses the verify-history per-track CRC
  comparison; extract that comparison into a shared helper both call. The resolver has no hardware or
  file dependencies, so it is fully unit-testable.
- New orchestrator `RipService.RunTestAndCopy(...)`: ensures independence (auto-calibrate as above),
  runs the reads, calls the resolver, assembles/commits or holds, cleans up stagings, and returns a
  result carrying the per-track verdicts, reads used, committed-or-held status, and the AR status.
- Staging folders live under the temp path, one per staged read, and are deleted on commit, on hold
  after the user's choice, and on stop/cancel.

## Outputs

- Per-track files in the output folder (v1).
- A readable Test & Copy log written into the output folder: disc id, drive, read offset, per-track
  CRC32 for each read, which reads agreed, per-track AR status, the disc-level CTDB status, and the
  overall verdict
  ("Test & Copy PASSED - every track verified by >=2 independent reads", with the read count). This
  is the local, shareable proof.
- The existing `.verify` sidecar (verify-history record) for the committed read.
- The shareable DiagnosticLog line stays ids and numbers only, no titles or paths, following the
  verify-history privacy rule: `testcopy disc=<id> reads=<n> passed=<0|1> heldTracks=<m>`.

## UI

- A third primary action on the Rip page next to Verify and Rip: "Test & Copy".
- Progress text reflects the stage: "Test read (1 of 2)...", "Copy read (2 of 2)...", "Confirming
  (read 3)...". A modal "Calibrating drive..." appears first only when auto-calibration is needed.
- Result card: PASSED shows "Verified by N independent reads" (plus the AR status line); HELD shows
  "Held - differs on track M" with the three buttons (Re-run / Accept anyway / Discard).
- This card later adopts the cool/warm verify-vs-rip visual treatment (separate design).

## Error handling and edge cases

- Track count / TOC mismatch between reads (should not happen for the same disc) -> treat as an error,
  discard stagings, report.
- Stop or cancel mid-operation -> stop at the next safe point, delete all stagings, no output written.
- Disk space: two or three stagings plus the final output exist briefly; stagings are temp and cleaned
  up promptly. A low-space failure during a staged encode surfaces as a normal encode error and aborts
  the operation without touching the output folder.
- Calibration fails during auto-calibrate -> report it and do not proceed (cannot guarantee
  independence).
- "Accept the Copy read anyway" writes the Copy read's files but marks them and the log
  "not test-verified".
- Unrecoverable read errors: if a read reports sectors it could not recover on a track (the existing
  bad-sector tracking), that is surfaced in the verdict and log even when the two reads agree on that
  track - two identical reads over a damaged region is consistency, not proof the region is pristine.
  AccurateRip, when present, is the tie-breaker; without it the log flags the track "agreed, but had
  unrecoverable sectors".

## Testing

- `TestAndCopyResolver` unit tests (MSTest, in the existing WPF test project): all-agree (2 reads,
  commit); one track differs, resolved by the third read (pick the agreeing staged read); a track that
  never agrees across three reads (Held); no-AR fallback (CRC32-only agreement); AR-consistent pass;
  AR-inconsistent-with-disc but reads agree (Passed, AR status reported).
- Fuzz test the resolver with random per-read CRC vectors and read counts, asserting invariants: a
  Passed outcome never sources a track from a read outside its agreeing pair; a Held outcome always
  names at least one unresolved track; the committed source read is always a staged read.
- Hardware orchestration (`RunTestAndCopy`, auto-calibrate, staging, commit) is verified live on the
  drive: a clean disc (2 reads, PASSED, bit-exact to a normal rip) and a marginal disc (third read
  and/or Held path).

## v2 follow-up (planned, not in v1)

Single-file image + cue output. Requires either re-encoding from spliced per-read audio or a
whole-disc agreement fallback (commit only when two reads agree across the entire image). Plan it
after v1 is built, fuzzed, and tested.
