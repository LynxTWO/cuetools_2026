# Adversarial Edge-Case Review

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
