# Unknowns: Critical Paths

Current-state refresh: 2026-07-26. The three findings that were open in this
ledger on 2026-07-02 are now resolved. Remaining transport, runtime, logging,
and provenance gaps live in their topic-specific ledgers.

## Entries

No open entry remains in this ledger.

## Closed items

### RarStream backward seek could accept stale EOF

- **Area or file:** `CUETools.Compression.Rar/RarStream.cs`,
  `CUETools/CUETools.TestCodecs/RarCompressionProviderTest.cs`
- **Resolution:** fixed 2026-07-26. A backward seek asks the worker to abort and
  reopen the archive, but `Read` could see the completed prior pass's `_eof`
  before rewind was acknowledged and return zero bytes. The wait predicate now
  treats `_rewind` as pending work and waits for replay data or terminal state.
- **Evidence:** the committed 280-byte RAR5 fixture and 2,083-byte source oracle
  exercise the production provider/stream path. The focused case passed, followed
  by 20/20 repeated no-build full-read/backward-seek runs.
- **Boundary:** this closes the concrete replay race; broad malformed-RAR and
  concurrency fuzzing remain separate coverage.
- **Status:** closed

### BitReader buffer over-read

- **Area or file:** `CUETools.Codecs/BitReader.cs`
- **Resolution:** fixed in commit `624879c`. Speculative cache top-up now reads
  zero beyond the logical end instead of dereferencing beyond the supplied
  buffer, while unbounded unary/Rice scans detect the end and throw.
- **Evidence:** implementation and focused boundary tests inspected 2026-07-26;
  aggregate totals are refreshed by the final canonical gate.
- **Boundary:** this closes the concrete shared `BitReader` out-of-bounds read.
  It is not a claim that every managed/native codec parser is exhaustively safe
  against malformed input.
- **Status:** closed

### Windows reserved names and trailing dots/spaces

- **Area or file:** `CUETools.Processor/CUEConfig.cs`,
  `CUETools/CUETools.TestProcessor/CleanseStringTest.cs`
- **Resolution:** fixed in commit `e93532e`. `CleanseString` maps trailing
  dot/space runs to underscores and prefixes reserved DOS device-name
  components, including names with extensions.
- **Evidence:** implementation and focused tests inspected 2026-07-26; aggregate
  totals are refreshed by the final canonical gate.
- **Boundary:** filesystem path length, permissions, reparse points, and
  non-Windows filesystem semantics are separate concerns.
- **Status:** closed

### Legacy MusicBrainz client provenance and reachability

- **Area or file:** historical `MusicBrainz/` project
- **Resolution:** the client source/project was deleted in commit `63d7de6`
  after direct lookup was retired. Current metadata paths use the CTDB proxy and
  freedb/gnudb fallback. Browser links and MusicBrainz tag field names remain,
  but they do not load an in-repo client library.
- **Evidence:** directory absence, commit history, current project references,
  and GUI call sites inspected 2026-07-26.
- **Boundary:** CTDB/gnudb transport and external metadata behavior remain open
  in `docs/unknowns/architecture-pass.md`.
- **Status:** closed

### CTDB repair result verification

- **Area or file:** `CUETools.AccurateRip/CDRepair.cs`
- **Resolution:** verified 2026-07-02. The reachable `VerifyParity` path folds
  corrections into a running CRC, combines the current rip's CTDB CRC, and
  refuses recovery unless the residual is zero.
- **Evidence:** repair call path and invariant reviewed. A later live opt-in run
  detected and repaired a deliberately damaged known image, independently
  post-verified the published sibling, and confirmed source hashes were unchanged.
- **Boundary:** CRC32 detects ordinary corruption but is not a cryptographic
  server signature. CTDB HTTP transport remains an open architecture concern.
- **Status:** closed
