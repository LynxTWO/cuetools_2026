# Unknowns: critical paths

Open questions from critical-path comment passes (pass 03). Entry shape and vocabulary come from `.claude/skills/anti-dark-code/references/00-conventions.md`.

## Entries

### Reserved device names and trailing dots survive metadata cleansing

- **Area or file:** `CUETools.Processor\CUEConfig.cs:565` (`CleanseString`), path construction in `CUESheet.cs`
- **Concern:** `CleanseString` (read 2026-07-02) strips every `Path.GetInvalidFileNameChars()` char, which on Windows includes `\`, `/`, and `:`, so per-field separator injection and `..` traversal from remote metadata are blocked. It does not handle Windows reserved device names (CON, PRN, NUL, COM1..9, LPT1..9) or trailing dots/spaces.
- **Why it matters:** a track titled e.g. `NUL` produces a path Windows treats as the null device; trailing dots/spaces get silently trimmed by the OS, causing surprising or failed writes. Not a traversal risk, but a correctness/robustness gap on attacker- or typo-supplied metadata.
- **Evidence found so far:** implementation read and commented; separator handling verified via the invalid-chars set; reserved-name handling absent.
- **Confidence:** verified
- **Likely owner:** upstream maintainer
- **Next best check:** decide whether to add reserved-name and trailing-dot handling to `CleanseString` (a small, safe hardening change; candidate for a future remediation batch).
- **Risk level:** low
- **Status:** open

### MusicBrainz client library provenance

- **Area or file:** `MusicBrainz/`
- **Concern:** appears to be a mirrored copy of the old musicbrainz-sharp library (MSPL-licensed headers, v1.1-era API); local modifications versus upstream are unrecorded. Inline comments were deliberately withheld in the S1 pass because mirrored trees stay out of comment churn.
- **Why it matters:** the library targets an API generation MusicBrainz has deprecated; replacement (not patching) is the likely remediation, and knowing local diffs tells us what behavior must survive.
- **Evidence found so far:** directory shape, csproj vintage (v1.1 target framework entries), old API URL style in `MusicBrainzObject.cs:393`.
- **Confidence:** inferred
- **Likely owner:** upstream maintainer
- **Next best check:** diff against the last musicbrainz-sharp release; record verdict (mirror vs forked-and-owned) in the coverage ledger.
- **Risk level:** low
- **Status:** open

## Closed items

- **Is repaired audio re-verified after a CTDB parity repair?** — resolved 2026-07-02 (verified). Yes. `CUETools.AccurateRip\CDRepair.cs` `VerifyParity` (the implementation wired into the live CTDB repair flow, not the standalone `CUETools.CDRepair` copy) folds each Forney correction's effect into a running CRC, XORs in `ar.CTDBCRC(-actualOffset)` (the CRC of the actual local rip), and sets `canRecover = false` unless the residual CRC is exactly zero. So a correction set is accepted only if applying it to *this* rip reproduces the expected CRC. Parity fetched over unauthenticated HTTP therefore cannot silently rewrite audio: corrupted or forged parity fails the CRC gate. Caveat worth noting for a future threat-model pass: CRC32 is not collision-resistant, so an adversary who fully controls the parity blob is bounded by the RS structure plus a 32-bit check, not by a cryptographic hash - adequate against corruption and casual tampering, not a signature. Commented at the gate.
