# Adversarial Edge-Case Review

## R69 cache-defeat addendum - 2026-07-29

This pass reviewed only the secure reread cache-eviction slice at commit
`a82bf88` plus the pending per-command retry change.

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

Commit `5fa2c65` completed an uninterrupted K: Test & Copy in 2,275 seconds.
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
Mono behavior, CTDB server authentication/TLS, frozen classic artifact receipts and
hosted parity, and optical-drive cancellation/disagreement/failure injection.

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
