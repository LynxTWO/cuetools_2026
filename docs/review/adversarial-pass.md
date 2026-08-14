# Adversarial Edge-Case Review

## Damaged-disc recovery addendum - 2026-08-01

This pass challenged the complete damaged-media recovery surface at commit
`51b7823`: the secure read loop and its retry/fallback policies, SCSI transport
and sense handling, the sector vote and C2 evidence, CTDB parity repair and its
WPF transaction, verify and Test & Copy held states, the classic parallel path,
and the recovery closure claims in the backlog and ledger. Method: orchestrated
pass 07 with 8 blind challenger lenses, adversarial verification (refuter plus
trigger-tracer for high-risk claims), one bounded reality-check of improvement
suggestions, and a completeness critic (26 agents total, read-only on code).
The verification stage capped at 16 verifier calls with 63 wanted-but-dropped,
so most lens claims entered at `inferred`; the orchestrator then personally
re-opened every citation for the findings labeled verified below. No build,
test, or hardware was executed; all activation-evidence judgments are from
source, tests, and recorded receipts.

### What the earlier recovery work got right

- The R55-R105 classifier architecture holds up under challenge. Parent/child
  sense identity is preserved through batch decomposition, rejected payload
  bytes are never consumed, the 08/0A retry is bound to the exact drive and
  command shape, and cache eviction is complete-or-fatal. No lens found a path
  that consumes a rejected payload or reports a child failure with its parent's
  range.
- The WPF calibration gate fails closed exactly as documented: Secure and
  Paranoid rips refuse to start without an independent-reread strategy
  (`RipService.cs:288-315`, `1689-1696`).
- The held-state design intent is real: a failed confirming read holds the
  completed Copy instead of deleting it (`RipService.cs:1344-1349`), and the
  repair transaction seals SHA-256 proofs before the atomic move.
- The vote core is a single shared implementation (`SecureSectorVote.cs`)
  exercised by the deterministic TestRipper corpus, not a drifted test copy.

### Verified findings (orchestrator re-opened every citation)

1. **DeepRecovery breaks the failed-sector sentinel; never-converged sectors
   are reported clean.** Risk high, defect. `SCSIDrive.cs:213` marks a sector
   failed only on exact equality with the sentinel `(16 << cq) + 1`;
   `SCSIDrive.cs:1798` stores `pass + 2`; the deep-recovery extension zone
   (`SCSIDrive.cs:2283`, `2439-2443`) lets the last failing pass exceed the
   baseline cap, so the stored count overshoots the sentinel and the sector is
   never flagged. The rip log then prints no suspicious positions for audio
   that never converged (`CUESheet.cs:2519-2526`, `2719-2722`). The converse
   also holds: a sector whose last failure was exactly at `max_scans - 1` but
   which converges during the extension stays flagged as failed. DeepRecovery
   defaults on (`AppSettings.cs:48`). Three lenses filed this independently.
   Secondary: past pass 253 the `(byte)` cast at `SCSIDrive.cs:1798` wraps and
   the `olderr` comparison at `1795` can no longer match.
2. **A Stop during the confirming read deletes the completed Copy, and
   StopOnUnrecoverable issues that Stop automatically.** Risk high, defect.
   `RipService.cs:1342-1343` returns plain `Fail` for `"Stopped."` while every
   other confirming-read failure takes the `BuildHeld` path two lines later;
   the finally block then disposes the staging workspace holding the completed
   Copy. `RipViewModel.cs:1270-1274` auto-invokes Stop on unrecoverable damage,
   so a damaged disc can trigger the deletion with no user action. Refuter and
   trigger-tracer both confirmed.
3. **Eject or a tray event discards the held Copy without confirmation.** Risk
   high, defect. `RipViewModel.cs:1982-1990` (`ClearDiscView`) discards the
   held staging on any tray-open or media-gone signal. The discard on a real
   disc change is deliberate and documented at `RipViewModel.cs:1978-1981`
   (accept-anyway must not commit the previous disc's audio); the defect is
   that a user eject while the held panel offers "accept it anyway" deletes the
   only completed result with no prompt, and a multi-poll phantom tray-open (a
   measured 15 s phantom is recorded at `RipViewModel.cs:688-696`) could do the
   same with the disc still loaded.
4. **Damaged-consistent Test & Copy results are labeled Verified in the report,
   badge, and history.** Risk high, defect. `RipReport.cs:56-59` derives
   `Verified` from read agreement alone; the report model carries no damage
   field, so a CONSISTENT completion with failed windows renders "Verified by
   independent reads" (`ReportViewModel.cs:69-72`) and a clean history row
   (`HistoryStore.cs:96-100`) while the Test & Copy log for the same job
   correctly says CONSISTENT. Contradicts the ledger S15 claim that damaged
   agreement is distinguished from clean verification on every surface, and
   narrows the R71 closure wording.
5. **Committed AR/CTDB evidence binds to the newest read, not the committed
   read.** Risk high, defect. `RipService.cs:1737-1748` builds the committed
   `VerifyRecord` under a comment promising "the single committed read's own
   checksums" but takes `ArConfidence`/`CtdbConfidence` from
   `reads[reads.Count - 1]`. In a three-read run that commits read 1, the
   certificate can carry read 2's database verdict for bytes it did not verify.
   The held path (`RipService.cs:1379`, `1397-1408`) has the same shape.
6. **Classic Test & Copy never compares Test CRC to Copy CRC and prints
   "Copy OK" unconditionally.** Risk high, defect. `CUESheetLogWriter.cs:184`
   and `200-203` print both CRCs and then an unconditional "Copy OK";
   repo-wide, `_arTestVerify` is written (`CUESheet.cs:2830-2834`) and printed
   but never compared. A drive glitch between passes publishes mismatched audio
   under a log line asserting verification. Refuter and tracer both confirmed.
7. **Classic Secure/Paranoid rips run with no calibration gate and no cache
   defeat.** Risk high, gap. `frmCUERipper.cs:715` sets only
   `CorrectionQuality`; `ICDRipper` does not expose the calibration or
   cache-defeat members, so on a caching drive every secure re-read can be
   served from cache and the vote agrees with itself - the exact condition the
   WPF path fails closed on. Classic also publishes directly to the final
   destination with no staging or held state (`CUESheet.cs:2871-2879`,
   refuter-confirmed), and persisted Paranoid downgrades to Secure on restart
   (`frmCUERipper.cs:192`). CLAUDE.md states the calibration and held-state
   invariants unscoped; the classic path implements none of them.
8. **StopOnUnrecoverable fires mid-final-pass on the running error count and
   makes the DeepRecovery extension unreachable.** Risk medium, defect.
   `RipViewModel.cs:1266-1274` latches Stop when `reReads >= max` and the
   running count is nonzero, which occurs during the final baseline pass while
   later chunks could still converge; because the extension zone begins only
   after that pass ends with errors, StopOnUnrecoverable plus DeepRecovery
   means the extension never runs. CLAUDE.md requires the stop only after the
   evidence policy classifies a sector unrecoverable.
9. **CTDB repair applies the first server-ordered variant.** Risk medium,
   defect. `RepairTransaction.cs:68` hard-codes `e.selection = 0`;
   `CUESheet.cs:4764-4783` builds the recoverable-entry list in raw server
   order with no confidence sort, so a conf=1 pressing listed first wins over a
   conf=40 pressing, and the post-repair gate only requires confidence > 0.
10. **The SCSI pass-through never checks residual transfer length.** Risk
    high, gap (latent). `Device.cs:836-852` (and the 32-bit twin) returns
    Success purely on `ScsiStatus == 0`; `DataTransferLength` is never re-read
    after the ioctl, and the reused read buffer is only canary-filled under
    debug (`SCSIDrive.cs:1474-1479`), so a GOOD-status underrun would fold
    stale bytes from the previous batch into the vote as a clean pass. Whether
    any real drive/bridge produces GOOD-status underruns is unknown (entry
    filed).
11. **Two identical C2-clean passes are a confident vote at quality 1.** Risk
    medium, design gap. `SecureSectorVote.cs:26-49` uses an absolute margin
    (`128*(1+cq)-1`), not an agreement quorum, and `SCSIDrive.cs:2431-2432`
    ends the window at the first zero-error pass, so a drive doing stable
    error concealment (identical wrong bytes, no C2 flags) converges "secure"
    on pass 1. Cache defeat makes the two reads independent media reads, which
    is the real mitigation; drives that autodetect to `C2ErrorMode.None`
    (`SCSIDrive.cs:1296-1298`) vote with majority-only evidence and no surfaced
    downgrade warning.

### Inferred findings (cited by lenses; not personally re-opened)

- A multi-sector 24/00 during a speed or cache transition is decomposed as
  transfer-shape evidence before the transition-bound retry filters can see it
  (`SCSIDrive.cs:1519-1529` vs the catch filters at `2313-2345`), so the R57
  one-shot transition retry is reachable only for single-sector commands and a
  transition-state rejection can mark good sectors untrusted via per-child
  24/00 corroboration.
- The legacy 64/00 batch-split path gates its keep-fatal guard on
  `mediumError`, so transport, hardware, and not-ready child failures can fall
  through to `MarkSectorUnreadable` (`SCSIDrive.cs:1631-1637`, `1723`).
- Deep-recovery pass counts above 255 would wrap the byte-sized vote
  accumulators and C2 counters (`SCSIDrive.cs:1438-1447`,
  `SecureSectorVote.cs:33-46`); reachability needs sub-500 ms passes inside
  the 120 s ceiling, which plausibly requires cache-served re-reads (entry
  filed).
- `failedWindows` counts a window as given-up even when deep recovery later
  converges it (`RipService.cs:695-698`), overstating damage in telemetry.
- Transport-layer latents: `SCSIException` NRE when autosense is absent
  (`Device.cs:1077`), stale sense reported after `IoctlFailed`
  (`Device.cs:828`), `m_max_sectors` computable to 0 (`SCSIDrive.cs:331`),
  `Device.Seek` never writes the LBA (`Device.cs:3175`, no live callers),
  `RequestSense`/`Verify` declare buffers with the wrong direction
  (`Device.cs:3097`), and the EAC-style log hardcodes "Make use of C2
  pointers : No" and "Defeat audio cache : Yes" regardless of reality
  (`CUESheetLogWriter.cs:115`).
- The repair machinery has a dead drifted duplicate (`CUETools.CDRepair/`
  lacks the miscorrection CRC gate present in
  `CUETools.AccurateRip/CDRepair.cs`), the classic repair script publishes
  with no post-apply cross-check, and parity capacity exceedance is reported
  as generic "could not be verified".

### Overclaims and activation-evidence gaps in the records

- Backlog R55, R57, R58, R59 and the cache-defeat transition retry have no
  deterministic in-repo test that activates the orchestration branches (the
  tests cover the pure classifiers and a source-string contract). Some have
  recorded live hardware activation (R105 crossed four retained 08/0A
  addresses on H:); state these as "no in-repo automated activation evidence",
  not "never ran".
- The "closed through R105" ordering line and ledger S2 `tested` status read
  stronger than that evidence; R71's "damaged agreement is reported as
  CONSISTENT" is true of the log and Rip page but not of the report headline,
  badge, or history rows (finding 4).
- `docs/review/scenario-stress-test.md` predates the R55-R105 recovery wave
  and contains no scenarios for it; pass 08 should refresh it.
- CLAUDE.md's calibration, Test & Copy CRC, and held-state invariants are
  written unscoped but hold only for the WPF path (finding 7); steering pass
  01 should scope them or the classic path needs a decision.

### Improvement opportunities (reality-checked against the code)

Feasible within the current architecture (one bounded reality-check pass; not
adversarially verified):

1. Fix the failed-sector sentinel and gave-up accounting under deep recovery
   (finding 1 plus the `failedWindows` mislabel). Small effort, restores the
   integrity of every damage report downstream.
2. Re-read only still-disagreeing sectors instead of the whole window on every
   retry pass (`SCSIDrive.cs:2298-2311` re-reads the full window). Large
   effort; multiplies useful passes on the damaged region inside the same time
   budget and shrinks the accumulator-overflow exposure.
3. Apply requested speed drops at pass boundaries inside a stuck window. The
   adaptive controller exists (`AdaptiveSpeedController.cs`) but a mid-window
   drop takes effect only at the next fresh window, so a stuck window keeps
   rereading at the speed that produced the errors. Medium effort; must
   respect the R57 transition serialization.
4. CTDB-guided second-chance rereads: when a rip ends with unrecoverable
   windows and parity identifies the exact bad sectors, re-read just those
   before surrendering, instead of only post-rip parity repair. Large effort.
5. Expose Reed-Solomon repair headroom (worst stride-column utilization vs
   npar/2) so the user can choose re-rip vs repair. Small effort.
6. Persist per-sector evidence so a second session on the same damaged disc
   resumes instead of restarting. Large effort.
7. Adaptive vote quorum in high-disagreement regions (demand more than the
   absolute margin where passes have been flapping; finding 11). Medium
   effort.

Rejected by the reality check: realigning slipped reads into the vote via the
slip correlator (the correlator is a diagnostic; realignment contradicts the
untrusted-evidence design), and cross-drive tie-break reads for held tracks
(contradicts the one-drive-per-job lease and process-per-drive invariants).

### Risks that moved

- Up: DeepRecovery-mode damage reporting (finding 1) and Test & Copy held-state
  lifecycle (findings 2, 3) are the highest-risk recovery defects now known.
- Up: classic-path secure ripping is now evidence-backed as materially weaker
  than its log claims (findings 6, 7), previously recorded only as
  "per-file rather than album-transactional".
- Down: none. The classifier core survived; its risks were confirmed bounded.

### Coverage of this pass

Not read by any lens (confirmed present by the completeness critic):
`DriveService.cs` SCSI gate, `OpticalDriveLease.cs`, AccurateRip verdict
internals (`AccurateRip.cs:54-80`), classic CUETools CTDB submit path
(`frmCUETools.cs:1012-1057`), `CacheDefeatSearch.cs`,
`DriveCalibration.cs`/`GzJson.cs` persistence, `CUETools.Fuzz` (fuzzes six
Bwg.Scsi parsers; none of the recovery orchestration), and
`CUETools.Ripper.Console` (a third rip frontend exposing Paranoid with the
same missing gates as classic). 57 of 67 lens findings were not adversarially
verified due to the verifier cap; the verified list above reflects the
orchestrator's own re-checks instead.

## R69 cache-defeat addendum - 2026-07-29

This pass reviewed only the secure reread cache-eviction slice at commit
`8798b3c` plus the pending per-command retry change.

### What the earlier work got right

- Cache eviction still completes the full requested sector count or fails. A
  successful command is the only path that advances the completed range.
- Rejected scratch bytes never enter the audio vote or output.
- The final status and sense are captured before another command can overwrite
  them. A different retry failure stops the 24/00 fallback.
- The three address candidates and 16/8/4/2/1 shapes stay inside the audio
  program and outside the current target window.

### What it missed

- The first 24/00 consumed one retry counter shared by the entire eviction.
  K:'s 2026-07-29 receipt proved that fourteen later address/shape commands did
  not receive the intended command-local retry. The pending change gives each
  exact command one retry and keeps the aggregate diagnostic count.
- `_cacheDefeatBytes + 2351` used unchecked `int` arithmetic. A corrupted
  `Flush:2147483647` setting wraps the numerator negative and reduces the
  required eviction to one sector. This violates complete-or-fail even though
  normal calibration values are small. The calculation must use checked-width
  arithmetic and fail when the requested sector count exceeds the disc.
- The first payload read after a completed eviction retries every
  `SCSIException` while `_cacheDefeatJustFlushed` is set. That is broader than
  the documented 24/00 transition exception. The retry filter must preserve the
  exact status, sense, ASC, and ASCQ rule used by the other control transition.
- Calibration consumption parsed `Flush:` independently from the first-read
  gate. A current-version corrupt value such as `Flush:not-a-number` satisfied
  the gate by prefix while installing no cache defeat in `Run`. That is a
  fail-open independent-read claim. Both decisions must use the same positive
  flush parser, with malformed and zero values rejected.

### Coverage and remaining proof

The `SetCacheDefeat` call-site search found three candidates: one production WPF
path and two opt-in hardware tests. No second production cache-eviction
implementation was found. This is slice coverage, not a claim about all raw
SCSI commands in `Bwg.Scsi`.

R69 stays high risk until the fixed build passes the configured K: damaged
window and full Test & Copy. Windows reported K: with no ready media during this
pass, so the hardware result remains unknown.

No protected-area approval is needed. The changes preserve the existing
fail-closed read contract and do not publish or repair user data.

### Hardware receipt after the first correction

The source-bound K: rerun proved the per-command scope: all fifteen
address/shape commands received one retry, and all fifteen still returned exact
`24/00`. The run failed closed before Copy. After the process released its
handle, Windows reported no media and a new raw SCSI handle could not open K:.
The user independently reports that physical media reload wakes this drive
after read failures.

That evidence permits one narrower recovery experiment: after the complete
exact-invalid-field ladder is exhausted, send one `START UNIT` through the
handle that is still open, require readiness, and repeat the full eviction.
It does not permit a general device reset, tray load, C2 disable, dropped cache
proof, or unbounded retry. The result remains unknown until the same dormant
state is crossed in a live run.

The next live Copy crossed that state. `START UNIT` succeeded, but its immediate
`TEST UNIT READY` returned the same exact `24/00`, and CUETools failed closed.
This proves the wake command is accepted and that readiness is another bounded
firmware transition; it does not prove the drive is ready. The next experiment
may settle before readiness and retry that exact readiness CDB once. A general
readiness retry or successful-wake assumption remains out of scope.

The following source-bound Test disproved the assumption that one settle and
retry would make readiness authoritative. After 946 seconds, both readiness
CDBs returned exact `24/00`; the second followed the bounded settle, and Windows
again reported no loaded media after the fail-closed result. Repeating the same
advisory query with a larger time budget has no evidence behind it.

The narrower continuation does not omit proof. Only after successful `START
UNIT` and those two exact transition results may the code classify readiness as
indeterminate and repeat the one already-bounded full eviction. That unrelated
payload read is stronger evidence: it must access media and complete the
measured cache volume before the target reread can proceed. Any other readiness
failure or another complete-ladder exhaustion remains fatal. Hardware proof of
that continuation is still pending.

### Final damaged-disc receipt

Commit `17d984e` completed an uninterrupted K: Test & Copy in 2,275 seconds.
Both reads were consistent, the encoded output passed the final PCM check, and
the run crossed the earlier 92-percent Copy failure boundary. The optical
reader reported three exhausted windows and six suspicious sectors. CTDB
published a repaired sibling with source and output receipts covering 25 files
each. Independent decoded-PCM comparison found 86 changed channel samples in
67 stereo frames across the same six sectors. The repaired output matched
AccurateRip at 55/82 and CTDB at 207/234. All 24 audio basenames, descriptive
tags, and stream layouts were preserved; proof tags were intentionally replaced
by `repair.verify` and the fresh repair reports.

The run proves the final user outcome, not activation of the intermittent wake
branch. Every cache-defeat wake, readiness, retry, and chunk-fallback counter
was zero. Keep the exact dormant-drive branch as a residual hardware unknown
until a future run records a positive branch counter or a safe deterministic
fault-injection method exercises it.

## Artwork import and optional-provider addendum - 2026-07-28

This pass challenges the artwork selector before local drag-and-drop and
TheAudioDB are added. No application code changed during the pass.

### A6. A local image path is not a stable selection snapshot

Keeping a dropped path until Rip would permit the file to change between preview
and publication. A large file, directory, unsupported image, or decoder bomb
could also cross the UI boundary before the network loader's limits apply.

The import boundary must accept exactly one regular JPEG, PNG, or BMP file, open
it once, enforce the encoded-byte cap while reading, validate magic and dimensions
before full decode, enforce the shared pixel cap, and retain only the resulting
bytes. The diagnostic log must not record the path. PNG and BMP inputs are always
encoded as JPEG. JPEG is also re-encoded when it exceeds the configured side
limit; an in-limit JPEG may remain byte-identical. The frozen JPEG byte array,
not the path or a mutable bitmap, enters a job.

### A7. A local override needs a release-scoped lifetime

An override that survives a release or disc change can attach the wrong cover.
An override that disappears on an unrelated provider refresh does not satisfy
the user's explicit replacement choice.

The override is authoritative only for the current release-selection generation.
It survives background provider completion and a size-setting change, which
re-derives it from the retained master bytes. It is cleared by a release or disc
change, `No cover`, or an explicit return to automatic selection.

### A8. TheAudioDB uses an API key, not an account password

The documented V1 API places a key in the URL path. Premium V2 uses an
`X-API-KEY` header. The service documentation does not define username/password
authentication for application calls. Collecting an account password would add
a credential CUETools cannot use safely or correctly.

The setting must accept only an API key, keep the provider off by default, protect
the saved value with a purpose-specific DPAPI entropy value, redact it before any
request, and avoid logging request URLs. V1 support is needed for user-supplied
free or premium keys; a later V2 mode can use header authentication. Results must
show TheAudioDB attribution. Rate limiting, 429 handling, response limits, host
allowlisting, and the shared image loader remain mandatory.

### A9. Browser-only art must not become an automatic cover

The first Cover Art Archive slice retained front images only. Adding back, medium,
booklet, or other images to meet the `All artwork` browser contract creates a
ranking trap: a non-front exact-release image could otherwise be selected
automatically when no front image exists.

Candidates need an explicit automatic-eligibility fact. Only validated front
images and user imports are eligible. Browser sorting remains independent of that
gate.

### A10. Unknown image facts need an honest enrichment path

The current table displays `unknown` for dimensions or encoded size that a
manifest omits. That is honest, but it does not fulfill the intended inspection
workflow when the provider can supply the facts cheaply.

The loader should enrich candidates with bounded header data where possible.
It must not download every master simply to fill the table. A bounded thumbnail
load may populate thumbnail facts, while a selected master must replace them with
the original dimensions and byte count. Unknown remains a valid value when the
provider and bounded probe cannot establish the fact.

### A11. Provider refresh and an active Rip are separate ownership domains

Network discovery, thumbnails, and user selection can continue while another
drive window owns a Rip. A running job must keep the JPEG bytes captured at job
start. Later selection, refresh, or local import can affect only the next job.

The current byte-array snapshot already provides this separation as long as no
code mutates that array. Tests must hold a job snapshot while the visible
selection changes and prove the original bytes remain unchanged.

### Required verification

- malformed, oversized, multi-file, directory, renamed, truncated, and
  over-pixel local inputs;
- PNG and BMP conversion, oversized JPEG resizing, alpha flattening, and
  in-limit JPEG behavior;
- local override survival, reset, and immutable job capture;
- protected API-key round trip, corrupt-key recovery, clear behavior, and log
  redaction;
- TheAudioDB release-group and text fixtures, 404, 429, malformed JSON, unsafe
  URLs, response limits, and cancellation;
- non-front candidates visible under `All artwork` but excluded from automatic
  selection; and
- dark, light, narrow, keyboard, drop-target, and real embedded-output evidence.

## Current-state addendum - 2026-07-26

The body below is preserved as the 2026-07-02 challenge pass. Its formerly open
claims were re-tested during the 2026-07-26 audit:

- managed FLAC and ALAC reader bounds are now enforced and corpus-fuzzed;
- MOTD is bounded strict text over HTTPS only, with no remote image decode or cache;
- reserved/trailing Windows names are hardened;
- packaged plugin sets are path/identity/architecture/hash-bound. Managed and native
  bytes are rechecked at their actual load boundary; native module paths are verified,
  handles remain loaded, and bare-name fallback is forbidden. A deliberately enabled
  local-development plugin boundary is still not equivalent to publisher signing;
- managed external encoders are rehashed through a deny-write/delete lease retained
  across immediate or deferred launch and self-verification;
- existing album reservation sentinels are never reclaimed by path after inspection;
  they consume one candidate and force a numbered sibling;
- archive input remains streamed rather than extracted;
- the newly exposed high-risk axes were publication races, repair destination
  initialization, external-process lifetime, WMA whole-output verification, and
  release proof. Those are remediated locally and detailed in
  `2026-07-26-autonomous-audit.md`.

Later live evidence closed the broad availability gaps: FLACCL's exact-length path
passed on an RTX 3060, WMA Lossless completed a real independent-decode round trip,
Icecast 2.5.0 completed source/auth/metadata/listener/teardown smoke, two optical drives
completed full-disc reads, H: completed a full FLAC rip and a two-read Test & Copy with
committed output-assurance proof, and staged CTDB repair successfully repaired and
post-verified a deliberately damaged image without changing the source.

Remaining adversarial checks are narrower: hostile replacement of an external
encoder's shared work file, cross-vendor OpenCL behavior, Icecast TLS/certificate and
Mono behavior, CTDB server authentication/TLS, public-trust publisher signing,
and optical-drive cancellation/disagreement/failure injection.

Pass 07, 2026-07-02. Purpose: attack the current picture of the repo, not extend it. Vocabulary: `.claude/skills/anti-dark-code/references/00-conventions.md`.

## Areas reviewed

Everything the loop claimed this session: the coverage ledger (S1-S14), the comment passes, the logging audit, and the resolved/open unknowns.

## What earlier passes got right

- **The CRC self-check on repair (S5) is real and load-bearing.** The claim that plain-HTTP parity is safe holds because `AccurateRip\CDRepair.cs` rejects any fix whose corrections do not reproduce the local rip's CRC. This is genuine defense, not an overclaim - verified at the code.
- **Test suites are honestly scoped.** TestParity/TestCodecs are green and CI-gated;
  TestProcessor's fixtures were restored, and TestRipper's private capture/stale copied
  vote algorithm has been replaced by SDK net47 tests against the same production
  `SecureSectorVote` helper used by `SCSIDrive`. Its canonical result is now 3 passed,
  0 failed, and 0 skipped, matching the enrolled three-test/zero-skip contract.
- **Out-of-repo surfaces are named, not flattened:** CTDB/AccurateRip/gnudb/MusicBrainz servers, the EAC host, GitHub runners, cue.tools. Good.

## What earlier passes overstated or missed

### A1. "S4 archive handling: mapped" understates a real, unreviewed attack surface

The ledger marks S4 `mapped` and defers it to a "07 focused review" - which is now. The concrete gap: **whether the RAR/Zip input path ever extracts to attacker-controlled paths on disk was never read.** CUETools advertises "use a RAR archive as input without unpacking," which suggests streamed reads (`RarStream`, `SeekableZipStream`) rather than extraction - but that was assumed, not verified. Combined with unrar 6.11 (pre-CVE-2022-30333), this is the single most important unreviewed path. **Raising S4 to high-priority for a real read** (does `RarStream` ever hit `RARProcessFile` with an extract-to-path, or only in-memory?). This directly gates decision D3.

**RESOLVED same pass (2026-07-02):** read `RarStream.Decompress`. The input path opens the archive in Extract mode but reads the target entry via `_unrar.Test()` with a `DataAvailable` callback (`unrar_DataAvailable`), i.e. it streams the decompressed bytes into memory and never calls `RARProcessFile(Operation.Extract, destinationPath, ...)`. The `Extract`/`ExtractToDirectory` methods on the `Unrar` wrapper exist but are not on the input path (`RarCompressionProvider` -> `RarStream` -> Test). So the CVE-2022-30333 extract-to-path traversal vector is not reachable through normal CUETools use.

**D3 CLOSED 2026-07-26:** official signed UnRAR 7.23.0 x86/x64 DLLs replaced
6.11. Both expose the required ABI, and the production
`RarCompressionProvider`/`RarStream` path passed RARLAB's real test archive,
including exact full reads and backward seeks, under both process architectures.
Turning that manual oracle into a committed TestCodecs RAR5 fixture exposed a
backward-seek/stale-EOF race: replay could return zero before the worker
acknowledged rewind. `Read` now waits while rewind is pending; the focused case
and 20/20 repeated full-read/seek runs pass. The historical 6.11 provenance
remains recorded.

### A2. "S1 commented" is true but uneven

S1 is marked `commented`, but MusicBrainz was deliberately skipped (mirrored tree) and is now slated for replacement (D6). So S1's verification/metadata-clients claim is really "AccurateRip + CTDB + freedb commented; MusicBrainz untouched pending replacement." The ledger evidence column says this, but a reader skimming the status word could over-read it. Acceptable given D6 is tracked.

### A3. Large god-classes remain largely unread

S3 is `commented` but `CUESheet.cs` (212 KB) had only its sanitization/plugin/DB choke points read; the write/verify orchestration is unread. S2 is `commented` but `Bwg.Scsi\Device.cs` (131 KB raw SCSI) is unread. Both are honestly noted in the evidence column, but "commented" is a slice-level status, not a whole-file guarantee. No false coverage claim, but the depth is shallow relative to the code volume - fine for a first pass, worth stating plainly.

### A4. The BitReader out-of-bounds finding needs an exploitability verdict

`BitReader.fill()` reads past the buffer on malformed input (logged high-risk). What is NOT yet known: whether the managed decoders pre-size/pad their buffers so the overrun is bounded in practice. Until that is traced, the risk is "verified missing check, unknown exploitability." Do not downgrade it on the assumption that callers are safe.

### A5. Plugin loading + unsigned DLLs is an accepted trust boundary, not a closed one

`CUEProcessorPlugins` loads any `CUETools.*.dll` from the plugins folder unsigned (commented S3). This is documented but not mitigated. For a tool users install to `Program Files`, write access to the plugins folder generally requires admin, which bounds the risk - but that assumption is unverified against the actual install layout (the portable/zip distribution may put plugins in a user-writable folder). Worth confirming with the installer/collect_files layout.

## Risks that moved after this review

- **S4 archive handling: medium -> high priority** for review (not necessarily higher risk, but higher urgency - it is the biggest unreviewed attacker-controlled path and gates a CVE decision).
- **BitReader OOB (S6): stays high**, now with an explicit "trace caller padding" next step before any risk change.

## Protected areas needing approval before edits

Unchanged from `00-conventions.md` plus repo specifics: the CRC repair gate (S5), the plugin loader (S3), the EAC plugin/installer (S12), config credential storage (F1), and the release/CI path (S13). The approved decisions (D1-D4, D6, D7) will touch some of these; each should go through its own smallest-safe-edit + verification.

## Rules honored

No code changed in this pass. No suspicion promoted to fact: A1/A4/A5 are marked as unverified gaps with a named next check, not asserted vulnerabilities.
