# Repo Slices

Breakdown of the repo into slices that later anti-dark-code passes can work through. Produced by pass 05 (coverage slicing) on 2026-07-02, from the pass 02 system map. Each slice is large enough to matter and small enough to verify in one pass.

Vocabulary comes from `.claude/skills/anti-dark-code/references/00-conventions.md`.

## Slice entries

### S1: Network verification and metadata clients

- **Scope (repo paths):** `CUETools.AccurateRip/`, `CUETools.CTDB/`, `CUETools.CTDB.Types/`, `Freedb/`, `MusicBrainz/`
- **Why this slice:** every byte these parse arrives over plain HTTP from external servers; they also decide verification verdicts users trust.
- **Risk class:** high
- **Classification:** owned-risky (AccurateRip, CTDB), legacy-unclear (Freedb, MusicBrainz)
- **Related runtime units or flows:** rip verification, CTDB submit/repair, metadata lookup in CUERipper and CUETools GUIs
- **Blockers:** none
- **Exit criteria:** 03 comments on trust boundaries and checksum/verdict invariants; 04 audit of what request/response data is logged; HTTPS switch decision recorded for AccurateRip
- **Next pass:** 04 logging audit, then 03 critical-path comments
- **Status:** open

### S2: Ripping and SCSI stack

- **Scope (repo paths):** `CUETools.Ripper/`, `CUETools.Ripper.SCSI/`, `CUETools.Ripper.Console/`, `Bwg.Scsi/`, `Bwg.Hardware/`, `Bwg.Logging/`
- **Why this slice:** raw device I/O via P/Invoke (`Bwg.Scsi\Device.cs`, 131 KB), C2 error-mode quirk tables per drive model, read-command construction. Data integrity of every rip depends on it.
- **Risk class:** high
- **Classification:** legacy-unclear
- **Related runtime units or flows:** CUERipper GUI, console ripper, EAC-independent rip path
- **Blockers:** hardware-dependent behavior cannot be fully verified without a drive
- **Exit criteria:** 03 comments on C2/offset/cache invariants and drive quirk tables; unknowns recorded for untestable device paths
- **Next pass:** 03 critical-path comments
- **Status:** open

### S3: Processor engine

- **Scope (repo paths):** `CUETools.Processor/`
- **Why this slice:** the orchestration core. `CUESheet.cs` (212 KB) owns CUE parsing, conversion, verification flow, filename templating, tagging; `CUEProcessorPlugins.cs` is the unsigned plugin-loading trust boundary; `CUEToolsLocalDB` is the persistent rip database.
- **Risk class:** high
- **Classification:** owned-risky
- **Related runtime units or flows:** every GUI and CLI conversion/verification flow
- **Blockers:** none
- **Exit criteria:** 03 comments on CUE-style invariants, plugin loading, local DB save semantics; candidate seams for later decomposition recorded
- **Next pass:** 03 critical-path comments
- **Status:** open

### S4: Archive handling

- **Scope (repo paths):** `CUETools.Compression/`, `CUETools.Compression.Rar/`, `CUETools.Compression.Zip/`, plus bundled `unrar.dll` 6.11 and `ICSharpCode.SharpZipLib.dll` 0.85.5
- **Why this slice:** parses untrusted archives; both native and managed dependencies are old, and unrar 6.11 predates the CVE-2022-30333 fix (inferred from version).
- **Risk class:** high
- **Classification:** owned-risky, third-party mirror (binaries)
- **Related runtime units or flows:** "rip from RAR/Zip without unpacking" feature in CUETools
- **Blockers:** upgrade-vs-replace decision for both libraries
- **Exit criteria:** confirm whether extraction ever writes attacker-controlled paths to disk; record upgrade path; 03 comments on the archive trust boundary
- **Next pass:** 07 adversarial review (focused), then 11 remediation item
- **Status:** open

### S5: Parity and repair math

- **Scope (repo paths):** `CUETools.Parity/`, `CUETools.CDRepair/`, `CUETools.AccurateRip\CDRepair.cs`
- **Why this slice:** Reed-Solomon encode/decode used for CTDB parity repair. Errors here silently corrupt audio while reporting success. Has real test coverage (`CUETools.TestParity`) that CI never runs.
- **Risk class:** high
- **Classification:** owned-risky
- **Related runtime units or flows:** CTDB repair flow, verify tasks
- **Blockers:** none
- **Exit criteria:** tests running in CI; 03 comments on Galois-field and syndrome invariants
- **Next pass:** 03 critical-path comments (after tests are wired into CI)
- **Status:** open

### S6: Codec core and managed codecs

- **Scope (repo paths):** `CUETools.Codecs/`, `CUETools.Codecs.Flake/`, `CUETools.Codecs.ALAC/`, `CUETools.Codecs.HDCD/`, `CUETools.Codecs.LossyWAV/`, `CUETools.Codecs.MPEG/`, `CUETools.Codecs.WMA/`, `CUETools.Codecs.TTA/` (C++/CLI), `ttalib-1.1/`
- **Why this slice:** decoders parse untrusted files in `unsafe` code; encoders define bit-exactness guarantees. Partially covered by `CUETools.TestCodecs`.
- **Risk class:** high
- **Classification:** owned-risky; `ttalib-1.1` third-party mirror
- **Related runtime units or flows:** all conversion flows
- **Blockers:** none
- **Exit criteria:** 03 comments on bitstream invariants and unsafe buffer bounds assumptions; fuzz-target candidates listed
- **Next pass:** 03 critical-path comments
- **Status:** open

### S7: Native codec wrappers

- **Scope (repo paths):** `CUETools.Codecs.libFLAC/`, `CUETools.Codecs.libwavpack/`, `CUETools.Codecs.libmp3lame/`, `CUETools.Codecs.lame_enc/`, `CUETools.Codecs.MACLib/`, `CUETools.Codecs.ffmpeg/`
- **Why this slice:** ~100 `DllImport` declarations marshaling buffers into native codecs built from the ThirdParty submodules; marshaling mistakes corrupt memory.
- **Risk class:** high
- **Classification:** owned-risky (wrappers), cross-repo-boundary (native code lives in submodules)
- **Related runtime units or flows:** default FLAC/WavPack/APE/MP3 encode-decode paths
- **Blockers:** none
- **Exit criteria:** 03 comments on ownership/lifetime rules at each P/Invoke edge
- **Next pass:** 03 critical-path comments
- **Status:** open

### S8: GPU codecs and GPU parity

- **Scope (repo paths):** `CUETools.Codecs.FLACCL/`, `CUETools.FLACCL.cmd/`, `CUETools.Codecs.FlaCuda/`, `CUETools.FlaCudaExe/`, `CUETools.CLParity/`
- **Why this slice:** OpenCL kernels (FLACCL, CLParity) plus two orphaned CUDA projects. FlaCuda is not in `CUETools.sln` at all; no build touches it, and its `CUDA.NET` reference points outside the repo (verified 2026-07-02).
- **Risk class:** medium (FLACCL/CLParity); FlaCuda is dead weight
- **Classification:** legacy-unclear
- **Related runtime units or flows:** optional FLACCL encoder, CLParity
- **Blockers:** none
- **Exit criteria:** keep/replace decision recorded for FLACCL/CLParity; FlaCuda removal decision revisited after the initial rollout (user deferral)
- **Next pass:** 07 adversarial review (focused evidence check)
- **Status:** open

### S9: GUI applications

- **Scope (repo paths):** `CUETools/` (app project), `CUERipper/`, `CUEPlayer/`, `CUETools.eac3ui/`, `CUEControls/`, `ProgressODoom/`, `CUERipper.WPF/`
- **Why this slice:** WinForms shells over the engine. Contains the plain-HTTP MOTD fetch that renders remote content (`frmCUETools.cs:654,699`). `CUEPlayer` and `eac3ui` internals are unscanned. User decision 2026-07-02: `CUERipper.WPF` stays until the initial anti-dark-code rollout completes.
- **Risk class:** medium (high for the MOTD path)
- **Classification:** owned-risky (CUETools, CUERipper), legacy-unclear (CUEPlayer, eac3ui, ProgressODoom), deferred (CUERipper.WPF)
- **Related runtime units or flows:** all user-facing flows
- **Blockers:** none
- **Exit criteria:** MOTD path commented and remediation item filed; CUEPlayer/eac3ui at least inventoried; ProgressODoom provenance recorded
- **Next pass:** 02 architecture map (fill unscanned corners), then 03
- **Status:** open

### S10: CLI tools

- **Scope (repo paths):** `CUETools.Converter/`, `CUETools.ARCUE/`, `CUETools.Flake/`, `CUETools.ALACEnc/`, `CUETools.LossyWAV/`, `CUETools.ChaptersToCue/`, `CUETools.eac3to/`, `CUETools.CTDB.Converter/`
- **Why this slice:** thin command-line wrappers over the libraries; low logic density but they define the scriptable surface.
- **Risk class:** low
- **Classification:** owned-clear
- **Related runtime units or flows:** batch/scripted conversion and verification
- **Blockers:** none
- **Exit criteria:** spot-check that argument parsing does not bypass library-level safety decisions
- **Next pass:** 03 (light)
- **Status:** open

### S11: DSP and audio output

- **Scope (repo paths):** `CUETools.DSP.Mixer/`, `CUETools.DSP.Resampler/`, `CUETools.Codecs.CoreAudio/`, `CUETools.Codecs.DirectSound/`, `CUETools.Codecs.Icecast/`
- **Why this slice:** resampling math (correctness) and the Icecast writer (an outbound network path with credentials in config).
- **Risk class:** medium
- **Classification:** legacy-unclear
- **Related runtime units or flows:** CUEPlayer playback, streaming output
- **Blockers:** none
- **Exit criteria:** Icecast credential handling audited (04); resampler correctness assumptions commented
- **Next pass:** 04 logging audit (Icecast), then 03
- **Status:** open

### S12: EAC plugin and installer

- **Scope (repo paths):** `CUETools.CTDB.EACPlugin/`, `CUETools.CTDB.EACPlugin.Installer/`
- **Why this slice:** runs inside the third-party EAC process, targets .NET Framework 2.0, ships `Interop.HelperFunctionsLib.dll` (2015, unverified provenance), and is distributed via a vdproj MSI. Duplicated `SettingsReader/Writer` sources drift from the Processor copies.
- **Risk class:** high
- **Classification:** owned-risky, approval-gated (installer/release path)
- **Related runtime units or flows:** CTDB submissions from EAC users (the main data inflow to CTDB)
- **Blockers:** vdproj requires the VS Installer Projects extension; cannot build with plain MSBuild
- **Exit criteria:** 03 comments on the EAC boundary; settings-duplication drift recorded; installer replacement decision filed as remediation item
- **Next pass:** 03 critical-path comments
- **Status:** open

### S13: Build, CI, and release control plane

- **Scope (repo paths):** `.github/workflows/`, `collect_files.bat`, `collect_files_debug.bat`, `ThirdParty/*.patch`, `CUETools.sln`, project files
- **Why this slice:** the release artifact is assembled here; version truth is split between `AssemblyInfo.cs` and a const scraped out of `CUESheet.cs` by batch script; CI builds with hardcoded VS Enterprise paths and runs no tests.
- **Risk class:** high (release integrity), as a control-plane path
- **Classification:** owned-risky, external-control-plane (GitHub-hosted runners, VS image contents)
- **Related runtime units or flows:** every shipped binary
- **Blockers:** none
- **Exit criteria:** tests wired into CI; version single-sourcing decision recorded; workflow assumptions documented
- **Next pass:** 11 remediation (CI test step is a safe fix), then 10 maintenance harness
- **Status:** open

### S14: Tests

- **Scope (repo paths):** `CUETools/CUETools.TestCodecs/`, `CUETools/CUETools.TestParity/`, `CUETools/CUETools.TestProcessor/`, `CUETools/TestRipper/`, `CUETools.TestHelpers/`
- **Why this slice:** MSTest suites exist but never run in CI; their actual pass/fail state on current code is unknown.
- **Risk class:** medium (as missing safety net)
- **Classification:** owned-clear
- **Related runtime units or flows:** S5, S6, S3 correctness
- **Blockers:** none
- **Exit criteria:** suites run locally; failures triaged; CI step added
- **Next pass:** 11 remediation Step 2 (run and wire in)
- **Status:** open

## Slice order

Suggested order for the remaining rollout (risk first, cheap enablers early):

1. S14 Tests — running them is the cheapest evidence generator for S3/S5/S6
2. S1 Network clients — highest untrusted-input exposure, smallest surface
3. S3 Processor engine — everything depends on it
4. S4 Archive handling — known-old parsers with plausible CVE exposure
5. S2 Ripping/SCSI — high value, needs hardware-aware honesty
6. S13 Build/release control plane — protects everything shipped
7. S12 EAC plugin and installer
8. S6 then S7 codecs
9. S9 GUIs (fill unscanned corners first)
10. S11 DSP, S8 GPU, S10 CLI as capacity allows

## Exclusions

Slices that will not receive code-level passes. Each is explained in the system map instead.

- **ThirdParty submodules** (`ThirdParty/flac`, `taglib-sharp`, `openclnet`, `WavPack`, `WindowsMediaLib`) — cross-repo-boundary; pinned commits plus `ThirdParty/*.patch`. Explained in system map section 7.
- **ThirdParty binaries** (`ThirdParty/*.dll`, `Win32/`, `x64/`, `MAC_SDK/` zip) — binary; inventory with versions and dates lives in system map section 7 and the architecture-pass unknowns. User decision 2026-07-02: unreferenced DLLs (`Freedb.dll`, `MusicBrainz.dll`, `CUDA.NET.dll`) stay until the initial rollout completes.
- **Generated code** (`CUETools.CTDB\GeneratedAssembly.cs`, `*.Designer.cs`, `CUEPlayer\CUEPlayerDataSet.Designer.cs`) — generated; do not comment inline.
- **`CUERipper.WPF/`** — deferred by user decision until the initial rollout completes; single-window stub, no code-level pass planned.
