# Unknowns: architecture pass

This file records things that could not be confidently explained during the architecture mapping pass (2026-07-02). Do not guess. Update as evidence lands.

The entry shape, confidence levels, risk levels, and status values come from `.claude/skills/anti-dark-code/references/00-conventions.md`.

## Entries

### Patch level and CVE status of vendored binaries

- **Area or file:** `ThirdParty/*.dll`, `ThirdParty/Win32`, `ThirdParty/x64`, `CUETools.CTDB.EACPlugin\Interop.HelperFunctionsLib.dll`
- **Concern:** the vendored DLL inventory is now dated and versioned (see evidence), but CVE exposure and upgrade paths are not yet mapped. `Freedb.dll` and `MusicBrainz.dll` shadow same-named source projects in this repo; which one ships is still unclear.
- **Why it matters:** unpatchable, unauditable supply-chain surface inside a tool that parses untrusted input. Two concrete concerns: `ICSharpCode.SharpZipLib.dll` 0.85.5 (committed 2010-02-08) predates all modern SharpZipLib security fixes, and `unrar.dll` 6.11 (2022-05-28) predates the 6.12 fix for the CVE-2022-30333 path traversal (CVE match is inferred from version, not tested).
- **Evidence found so far:** version/date inventory 2026-07-02: CSScriptLibrary 2.3.2, CUDA.NET 2.3.7, Freedb.dll 1.0.0.1, MusicBrainz.dll 0.0.0.0, SharpZipLib 0.85.5 (all last committed 2010-02-08); Interop.HelperFunctionsLib 1.0.0.0 (2015-02-25); hdcd.dll no version info (2010); libmp3lame 3.100 (2021, current LAME); unrar 6.11 (2022). ffmpeg DLLs are not vendored (built on demand by `build_ffmpeg_dlls.yml`).
- **Confidence:** verified (inventory), inferred (CVE exposure)
- **Likely owner:** upstream maintainer
- **Next best check:** confirm whether `CUETools.Compression.Rar` actually extracts to disk (path traversal reach) and decide replace-vs-upgrade for SharpZipLib and unrar. Reference scan 2026-07-02: `ICSharpCode.SharpZipLib.dll` is live in `CUETools.Compression.Zip.csproj:36`; `CSScriptLibrary.v1.1.dll` only in `CUETools.TestCodecs.csproj:60`; `Freedb.dll`, `MusicBrainz.dll`, `CUDA.NET.dll` are referenced by no csproj HintPath (dead-weight candidates; CUDA.NET may load at runtime, unverified).
- **Risk level:** high
- **Status:** in progress
- **Notes:** 2026-07-02 — user decision: the unreferenced DLLs (`Freedb.dll`, `MusicBrainz.dll`, `CUDA.NET.dll`) stay in the tree until the initial anti-dark-code rollout completes. The CUDA.NET runtime-load question is tracked in `docs/unknowns/coverage-pass.md`.

## Closed items

- **CUERipper.WPF intent** — resolved 2026-07-02 by user decision: keep the stub untouched until the initial anti-dark-code rollout completes, then revisit. Recorded as `deferred` in the coverage ledger.
- **Git history and remote were dropped** — resolved 2026-07-02. `https://github.com/LynxTWO/cuetools_2026` turned out to be a full mirror of upstream `gchudov/cuetools.net` history. Local `master` was reconnected to it at upstream HEAD `e90b1a86`; the working tree matched that commit exactly.
- **ThirdParty submodules are empty** — resolved 2026-07-02. `git submodule update --init --recursive` restored all five submodules at their pinned commits (flac `1507800d`, taglib-sharp `b5ae84f2`, openclnet `4a10612b`, WavPack `4827b988`, WindowsMediaLib `46d46042`), and all five `ThirdParty/*.patch` files applied cleanly per the README build steps.
- **freedb service status** — resolved 2026-07-02. The default host is `gnudb.gnudb.org` (`Freedb\FreedbHelper.cs:33`), the live community mirror, not the dead freedb.org. Still plain HTTP.
- **CTDB and AccurateRip HTTPS support** — resolved 2026-07-02 by probing. `https://www.accuraterip.com/accuraterip/DriveOffsets.bin` returns HTTP 200, so the AccurateRip client can move to HTTPS (remediation candidate). `https://db.cuetools.net` fails TLS handshake, so the CTDB client is blocked on the server operator; raise upstream before touching `CUEToolsDB.cs:74`.
