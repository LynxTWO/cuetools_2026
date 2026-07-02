# Unknowns: architecture pass

This file records things that could not be confidently explained during the architecture mapping pass (2026-07-02). Do not guess. Update as evidence lands.

The entry shape, confidence levels, risk levels, and status values come from `.claude/skills/anti-dark-code/references/00-conventions.md`.

## Entries

### Provenance and patch level of vendored binaries

- **Area or file:** `ThirdParty/*.dll`, `ThirdParty/Win32`, `ThirdParty/x64`, `CUETools.CTDB.EACPlugin\Interop.HelperFunctionsLib.dll`
- **Concern:** prebuilt DLLs (SharpZipLib, CUDA.NET, CSScriptLibrary, ffmpeg builds, EAC interop) have no recorded version, source, or hash. `Freedb.dll` and `MusicBrainz.dll` shadow same-named source projects in this repo; which one ships is unclear.
- **Why it matters:** unpatchable, unauditable supply-chain surface inside a tool that parses untrusted input.
- **Evidence found so far:** directory listing; `build_ffmpeg_dlls.yml` exists for ffmpeg but the checked-in DLLs' origin is unrecorded. Upstream history (now available locally) may date some of them.
- **Confidence:** unknown
- **Likely owner:** upstream maintainer
- **Next best check:** inventory file versions/hashes of every binary in `ThirdParty/`, cross-reference `git log` for when each landed, and map each to a source and CVE status.
- **Risk level:** high
- **Status:** open

### CUERipper.WPF intent

- **Area or file:** `CUERipper.WPF/`
- **Concern:** single-window WPF stub referencing .NET 3.0/3.5-era assemblies; unclear if it is historical debris or a planned direction.
- **Why it matters:** modernization planning needs to know whether to delete it or build on it.
- **Evidence found so far:** project contains only `App.xaml`, `Window1.xaml`, `depprop.txt`. Upstream history now available for a date check.
- **Confidence:** verified (contents), unknown (intent)
- **Likely owner:** repo owner
- **Next best check:** ask the user; if unwanted, remove from the solution.
- **Risk level:** low
- **Status:** open

## Closed items

- **Git history and remote were dropped** — resolved 2026-07-02. `https://github.com/LynxTWO/cuetools_2026` turned out to be a full mirror of upstream `gchudov/cuetools.net` history. Local `master` was reconnected to it at upstream HEAD `e90b1a86`; the working tree matched that commit exactly.
- **ThirdParty submodules are empty** — resolved 2026-07-02. `git submodule update --init --recursive` restored all five submodules at their pinned commits (flac `1507800d`, taglib-sharp `b5ae84f2`, openclnet `4a10612b`, WavPack `4827b988`, WindowsMediaLib `46d46042`), and all five `ThirdParty/*.patch` files applied cleanly per the README build steps.
- **freedb service status** — resolved 2026-07-02. The default host is `gnudb.gnudb.org` (`Freedb\FreedbHelper.cs:33`), the live community mirror, not the dead freedb.org. Still plain HTTP.
- **CTDB and AccurateRip HTTPS support** — resolved 2026-07-02 by probing. `https://www.accuraterip.com/accuraterip/DriveOffsets.bin` returns HTTP 200, so the AccurateRip client can move to HTTPS (remediation candidate). `https://db.cuetools.net` fails TLS handshake, so the CTDB client is blocked on the server operator; raise upstream before touching `CUEToolsDB.cs:74`.
