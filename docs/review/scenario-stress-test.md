# Scenario Stress-Test

Pass 08, 2026-07-02. Synthetic abuse/failure scenarios that fit CUETools' actual shape, used to test whether the current map and comments would hold. Score key: 0 = the current understanding misses it, 1 = partly covered, 2 = clearly covered. Scorecard in `scenario-scorecard.md`.

## Scenarios

### SC1. Malicious CTDB parity blob tries to rewrite audio

Plausible: parity is fetched over unauthenticated HTTP. An on-path attacker returns a crafted parity file to corrupt a "repaired" rip.
- **Holds?** Yes. `AccurateRip\CDRepair.cs VerifyParity` accepts a fix only when the corrections reproduce the local rip's CRC (residual == 0). A forged blob fails the gate → `canRecover = false`. Commented at the gate (S5).
- **Residual gap:** CRC32 is not collision-resistant; an adversary fully controlling parity is bounded by RS structure + 32-bit check, not a signature. Adequate vs corruption, noted for a future threat-model pass. **Score 2.**

### SC2. Malformed FLAC/ALAC file triggers an out-of-bounds read

Plausible: users open arbitrary lossless files; decoders parse untrusted bitstreams in `unsafe` code.
- **Holds?** Partly. `BitReader.fill()` has no bounds check (documented S6, high-risk unknown). Whether callers pad/size the buffer to bound the overrun is untraced. A crafted truncated frame is a real crash candidate. **Score 1.**

### SC3. Crafted MOTD image exploits GDI+

Plausible: `frmCUETools` fetches `motd.jpg` over plain HTTP and renders via `Image.FromStream`. MITM or a compromised cue.tools serves a malformed JPEG.
- **Holds?** Partly. The trust boundary is commented (S9) and gated behind `checkForUpdates` (once/day), but the code path exists and hands remote bytes to a native decoder over HTTP. D1-style HTTPS is not yet applied to MOTD. **Score 1.**

### SC4. Metadata with a Windows reserved name or trailing dot

Plausible: gnudb/MusicBrainz return an album titled `NUL` or `CON`, or with trailing dots.
- **Holds?** Partly. `CleanseString` strips path separators and `:` (traversal blocked, verified S3) but not reserved device names or trailing dots. Result is a failed/surprising write, not a traversal. Logged in critical-paths. **Score 1.**

### SC5. Poisoned plugin DLL in the plugins folder

Plausible: a `CUETools.Evil.dll` dropped into `<exe>\plugins` is loaded and instantiated unsigned, in-process.
- **Holds?** Partly. Documented as a trust boundary (S3). Mitigation (folder write-perms) is assumed for the Program Files install but unverified for the portable/zip layout. No signature check. **Score 1.**

### SC6. MITM forges AccurateRip confidence over plain HTTP

Plausible: attacker on the network returns a dBAR response claiming a bad rip is "accurately ripped."
- **Holds?** Yes, by design intent. AR/CTDB results are treated as corroboration of locally computed CRCs, not independent proof (commented S1). D1 (HTTPS for AR) is approved and will close the transit gap. **Score 2.**

### SC7. RAR with path-traversal filenames (`../../evil`)

Plausible: a malicious RAR used as input.
- **Holds?** Yes. The input path streams via `Unrar.Test()` + DataAvailable callback; it never extracts to a filesystem path, so traversal filenames have nowhere to land (verified S4, pass 07). **Score 2.**

### SC8. Proxy password read from the profile at rest

Plausible: malware or another user reads the CUETools profile file.
- **Holds?** Partly. `ProxyPassword` is stored plaintext (F1, logging audit). Not logged, but readable on disk. Remediation queued. **Score 1.**

### SC9. A tampered release ships because tests never ran

Plausible (historical): before this session, CI built but ran no tests, so a regression in parity/codec math could ship.
- **Holds now?** Yes. TestParity/TestCodecs are `dotnet test`-gated in both CI and release workflows (S13/S14). Regression in the covered math now blocks the build. Gap: TestProcessor/TestRipper still can't run (fixtures/hardware). **Score 2 for covered math, 1 for engine/ripper.**

## Themes

1. The **integrity story is strong** (CRC repair gate, corroborative verification, no extract-to-disk) — SC1/SC6/SC7 score 2.
2. The **untrusted-parser story is the weak axis** — SC2 (BitReader OOB) and SC3 (GDI+ MOTD) are the real exposure, both on attacker-controlled bytes. These are the top candidates for the fuzzing/hardening work (modernization idea 9).
3. **Data-at-rest and reserved-name edges** (SC4, SC8) are low-severity robustness/hardening gaps, tracked.
