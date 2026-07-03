# Adversarial Edge-Case Review

Pass 07, 2026-07-02. Purpose: attack the current picture of the repo, not extend it. Vocabulary: `.claude/skills/anti-dark-code/references/00-conventions.md`.

## Areas reviewed

Everything the loop claimed this session: the coverage ledger (S1-S14), the comment passes, the logging audit, and the resolved/open unknowns.

## What earlier passes got right

- **The CRC self-check on repair (S5) is real and load-bearing.** The claim that plain-HTTP parity is safe holds because `AccurateRip\CDRepair.cs` rejects any fix whose corrections do not reproduce the local rip's CRC. This is genuine defense, not an overclaim - verified at the code.
- **Test suites are honestly scoped.** TestParity/TestCodecs are green and CI-gated; TestProcessor/TestRipper are correctly marked blocked-on-fixtures rather than passing. No green-wash.
- **Out-of-repo surfaces are named, not flattened:** CTDB/AccurateRip/gnudb/MusicBrainz servers, the EAC host, GitHub runners, cue.tools. Good.

## What earlier passes overstated or missed

### A1. "S4 archive handling: mapped" understates a real, unreviewed attack surface

The ledger marks S4 `mapped` and defers it to a "07 focused review" - which is now. The concrete gap: **whether the RAR/Zip input path ever extracts to attacker-controlled paths on disk was never read.** CUETools advertises "use a RAR archive as input without unpacking," which suggests streamed reads (`RarStream`, `SeekableZipStream`) rather than extraction - but that was assumed, not verified. Combined with unrar 6.11 (pre-CVE-2022-30333), this is the single most important unreviewed path. **Raising S4 to high-priority for a real read** (does `RarStream` ever hit `RARProcessFile` with an extract-to-path, or only in-memory?). This directly gates decision D3.

**RESOLVED same pass (2026-07-02):** read `RarStream.Decompress`. The input path opens the archive in Extract mode but reads the target entry via `_unrar.Test()` with a `DataAvailable` callback (`unrar_DataAvailable`), i.e. it streams the decompressed bytes into memory and never calls `RARProcessFile(Operation.Extract, destinationPath, ...)`. The `Extract`/`ExtractToDirectory` methods on the `Unrar` wrapper exist but are not on the input path (`RarCompressionProvider` -> `RarStream` -> Test). So the CVE-2022-30333 extract-to-path traversal vector is not reachable through normal CUETools use. The D3 unrar upgrade remains worthwhile as hygiene (the DLL still decompresses attacker data in memory) but is not urgent.

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
