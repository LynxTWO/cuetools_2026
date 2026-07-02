# Remediation Backlog

Pass 11, 2026-07-02. Consolidates every finding from the anti-dark-code passes into ranked, bucketed work. Sources: coverage ledger, logging audit, adversarial pass, scenario stress-test, unknowns files, decisions-needed. Vocabulary: `.claude/skills/anti-dark-code/references/00-conventions.md`.

Buckets: **A** safe to do now (behavior-preserving / additive / docs), **B** approval-gated or behavior-changing, **C** needs more evidence first.

## Ranked items

### R1. BitReader out-of-bounds read on malformed input — bucket C→A, risk high

- **Where:** `CUETools.Codecs\BitReader.cs:110` (`fill`), shared by managed FLAC/ALAC/lossyWAV decoders.
- **Why:** no bounds check vs `buffer_len_m`; crafted/truncated stream reads past the buffer (SC2).
- **Next step:** (C) trace decoder buffer sizing/padding to establish exploitability; then (A) add a bounded fill or a guaranteed-padding contract. Good SharpFuzz target (idea 9).
- **Verify:** fuzz corpus of truncated frames; TestCodecs stays green.

### R2. MOTD image fetched over HTTP and rendered via GDI+ — bucket B, risk medium

- **Where:** `CUETools\frmCUETools.cs` MOTD path (S9, SC3).
- **Next step:** move to HTTPS; consider dropping the remote image or making it opt-in default-off. Approval-gated (GUI network behavior).
- **Verify:** MOTD still displays over HTTPS; feature flag honored.

### R3. AccurateRip → HTTPS (decision D1) — bucket B, approved

- **Where:** `CUETools.AccurateRip\AccurateRip.cs:829,1230`.
- **Next step:** flip the two scheme literals; http fallback on TLS failure; verify a real lookup + DriveOffsets.bin.
- **Verify:** live lookup parses; offsets download.

### R4. CTDB HTTPS — file upstream (decision D2) — bucket B, approved

- **Next step:** open a tracking issue on LynxTWO/cuetools_2026 for the db.cuetools.net TLS request; revisit `CUEToolsDB.cs:74` when the server answers TLS. No client change now.

### R5. unrar.dll upgrade (decision D3) — SCOPED, DEFERRED 2026-07-02 (hygiene)

- Current: both DLLs 6.11.0. rarlab's old direct download URL 404s (now versioned). Hygiene-only (CVE vector not reachable, pass 07) and can't be functionally verified headless (no RAR encoder for a test archive), so not swapped blind. Full plan + verification steps in `decisions-needed.md` (D3). Pick up with the codec refresh (R13) when a test RAR is available.

### R6. SharpZipLib upgrade (decision D4) — DONE 2026-07-02

- SharpZipLib 1.4.2 via NuGet for net47/netstandard2.0 (net20 keeps vendored 0.85.5). Password path adapted for the modern API (up-front password for AES). Packaging scripts fixed. Verified with a net8 round-trip harness (6/6 checks incl. AES).

### R7. MusicBrainz client replacement (decision D6) — SCOPED; STOPPED FOR USER DECISION 2026-07-02

- **Finding:** no live MusicBrainz client exists to replace — the `MusicBrainz/` library is dead (unbuilt, unreferenced) and CUERipper's direct query is commented out; MB metadata comes via CTDB's proxy + Freedb fallback. Full scope + MB v2/CoverArtArchive reference + options in `docs/review/musicbrainz-replacement-scope.md`. Awaiting the user's choice: (A) rely on CTDB, delete dead lib [recommended]; (B) add a new direct MB v2 provider; (C) leave as-is.

### R8. CUEControls resgen under dotnet build (decision D7) — PARTIAL, 2026-07-02

- **Done:** repo-root `Directory.Build.targets` (Core-MSBuild-gated) makes all SDK-style net47 first-party projects build under `dotnet build`; zero impact on the shipping devenv/CI build.
- **Remaining (folded into R12):** SDK-style conversion of the old-style WinForms GUIs (`CUETools`, `CUERipper`, `CUEPlayer`, `CUETools.eac3ui`, old-style `CLParity`/`FLACCL`) so they too `dotnet build`. Needs GUI runtime verification; not a blind headless change.

### R9. ProxyPassword stored plaintext at rest (F1) — bucket B, risk medium

- **Where:** `CUEConfigAdvanced.ProxyPassword` + SettingsWriter.
- **Next step:** (C) confirm persistence format; then DPAPI / Credential Manager, or document the exposure. Approval-gated (credentials).

### R10. Synthetic test fixtures to unblock TestProcessor / TestRipper — bucket A, risk medium

- **Where:** `docs/unknowns/coverage-pass.md`.
- **Next step:** generate small CC0 CUE fixtures (gaps variants, CD-Extra, one-track) for TestProcessor; rework TestRipper against synthetic multi-pass dumps. Then add CI steps.

### R11. CleanseString: reserved names + trailing dots (SC4) — bucket A, risk low

- **Next step:** small hardening in `CUEConfig.CleanseString`; add unit tests.

### R12. Modernization program (net8 SDK-style, async, HttpClient, SIMD, installer, dead-code removal D5) — bucket B, large

- **Reference:** the 14-item modernization list delivered 2026-07-02. Sequence: foundation (already: git/submodules/tests/CI) → framework migration → async → the rest. D5 (delete FlaCuda/dead DLLs) revisit with D6's outcome.

### R13. Codec version refresh + FlaCuda retirement + add missing codecs (user request 2026-07-02) — bucket B, large

- **Scope (user):** upgrade all codecs to their latest versions/builds, add any missing/wanted ones, and note that FlaCuda has effectively been superseded by FLACCL (OpenCL), which confirms the CUDA path is a dead ancestor rather than a parallel feature.
- **What this touches:**
  - ThirdParty submodules and their local patches: `flac` (libFLAC 1.5.0 pinned; check for newer), `WavPack` (5.8.1; check newer), MAC_SDK (10.86; APE), taglib-sharp, `libmp3lame` (3.100, already current), ffmpeg (built on demand by `build_ffmpeg_dlls.yml`). Each bump means re-checking that `ThirdParty/*.patch` still applies.
  - Managed wrappers in S6/S7 that must match new native ABIs (P/Invoke signatures, struct layouts).
  - FlaCuda (`CUETools.Codecs.FlaCuda`, `CUETools.FlaCudaExe`): confirmed orphaned (absent from sln) and superseded by FLACCL; retire under D5 once D6 lands. Verify FLACCL itself builds/runs on current OpenCL runtimes and consider whether it should replace FlaCuda outright or be modernized to managed SIMD (idea 14).
  - Missing/desired codecs: enumerate against current CUETools upstream and user wants (e.g. Opus, newer ALAC, DSD?) — needs a requirements list from the user before implementation.
- **Why it needs care:** codec upgrades are behavior-affecting (bit-exactness must be preserved; the golden-corpus tests in idea 3 should exist first). Approval-gated where they touch release output.
- **Next step:** produce a per-codec version-vs-latest table (current pin → latest stable) and a patch-reapply risk note; get the user's list of missing codecs to add; then upgrade one codec at a time with round-trip verification. Sequence after R8 (buildable) and ideally after golden-corpus tests (R10/idea 3).
- **Confidence:** the FlaCuda→FLACCL supersession is `inferred` from the orphaned-project evidence plus the user's note; the specific latest versions need a lookup pass.
- **Status 2026-07-02:** full audit DONE (`docs/review/codec-audit.md`) — every codec's format/engine/version/works status, plus the version table (`codec-refresh-scope.md`). Key results: no AAC *encoder* (decode-only via ffmpeg); test coverage only spans WAV/FLAC/ALAC (build golden-corpus suite to make all "works" verified); highest-value additions are Opus and AAC-encode. libFLAC/WavPack/LAME/ffmpeg current; MAC/taglib-sharp behind; FlaCuda dead (retire under D5). Additions still BLOCKED on the user's format list.

### R14. LAME v4 modernization initiative (user request 2026-07-02) — bucket B, large, separate project

- **Scope (user):** improve the LAME MP3 encoder enough to justify a version 4 release (stuck at 3.100 since 2017). Source at `C:\Users\usaft\Downloads\lame-3.100`. Separate from CUETools; start after the CUETools decisions are resolved. Permission granted to run any tests needed.
- **Next step:** build 3.100 as a baseline; assess plausible 20-year gains (AVX2 SIMD for FFT/psychoacoustics/quantization hot loops — Zen 3 is AVX2, not AVX-512; multithreaded frame encoding; build/CI modernization; VBR/reservoir quality tuning) and quantify with speed + quality benchmarks before proposing a v4 feature set. Bench on the 5950X (see hardware note).
- **Confidence:** unscoped; needs the baseline build + benchmark pass first.
- **Status:** open (not started; queued after CUETools)

## Ordering

1. R8 (unblocks local builds — do first, enables verifying everything else)
2. R3, R4 (approved, small, high-value: AR HTTPS + file CTDB issue)
3. R5, R6 (approved dependency upgrades)
4. R1, R9, R11 (safety/hardening; R1 needs the evidence step)
5. R7 (MusicBrainz — scope, then go/no-go)
6. R2 (MOTD HTTPS/removal)
7. R10 (test depth), then R12 (modernization) as a program

## Holes / external boundaries

- CTDB TLS (R4) is out-of-repo (server operator).
- CI depends on GitHub-hosted Windows image + VS Enterprise devenv path.
- Vendored binary provenance (idea 7) remains a supply-chain surface until moved to NuGet with SBOM.
- MusicBrainz/gnudb/AccurateRip/CTDB servers are external; their behavior can change independent of this repo.

## Changelog

- 2026-07-02 — backlog created from the first full anti-dark-code rollout (comment loop S1-S13, logging audit, adversarial, scenario passes) and the user's decisions D1-D7.
