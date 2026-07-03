# Codec Refresh - Version Table and Scope (R13)

User request 2026-07-02: upgrade all codecs to latest versions/builds, add any missing ones, and retire FlaCuda in favor of FLACCL. This doc is the survey + plan. The "which codecs to add" part **needs the user's list** and is not guessed here.

"Current" versions are verified from the repo (submodule pins / vendored binary metadata / upstream commit messages on this history). "Latest" is best-known as of the Jan 2026 knowledge cutoff and must be re-checked at implementation time (marked `inferred`).

## Current vs latest

| Codec / lib | Where | Current (verified) | Latest (inferred) | Assessment |
| --- | --- | --- | --- | --- |
| libFLAC | `ThirdParty/flac` submodule + `CUETools.Codecs.libFLAC` | 1.5.0 (commit "Update libFLAC from 1.4.3 to 1.5.0", 2025-03) | 1.5.0 | Current. Re-check for 1.5.x point releases. |
| WavPack | `ThirdParty/WavPack` submodule + `.libwavpack` | 5.8.1 (commit, 2025-02) | 5.8.1 | Current. |
| Monkey's Audio (APE) | `ThirdParty/MAC_SDK` (MAC_1086_SDK.zip) + `.MACLib` | 10.86 (commit "Update MAC_SDK from 10.74 to 10.86", 2024-12) | ~10.87+ / possibly 11.x | Likely slightly behind; verify latest MAC SDK. |
| LAME (MP3) | `ThirdParty/Win32/libmp3lame.dll` 3.100 + `.libmp3lame` | 3.100 (2017 release, DLL built 2021) | 3.100 (no newer release) | Already latest release - this is why R14/LAME v4 exists. |
| ffmpeg | built on demand (`build_ffmpeg_dlls.yml`) via FFmpeg.AutoGen | AutoGen 7.1.1 (commit "Update FFmpeg.AutoGen 6.1.0 -> 7.1.1", 2025-04) | ffmpeg 7.1.x | Reasonably current; re-check ffmpeg point release used by CI. |
| taglib-sharp | `ThirdParty/taglib-sharp` submodule | pin `b5ae84f2` (~2.1.0-dev era) | 2.3.x | Behind; candidate bump (tagging library, not a codec). |
| TTA | `ttalib-1.1/` (in-repo C++) + `.TTA` (C++/CLI) | TTA1 SDK 1.1 | TTA1 SDK ~1.x (stable/dormant) | Effectively current; upstream is dormant. |
| ALAC | `CUETools.Codecs.ALAC` (managed) + Apple ref | Apple ALAC reference | unchanged | Stable; no action. |
| WMA | `CUETools.Codecs.WMA` + `WindowsMediaLib` submodule | WM Format SDK wrapper | unchanged | Windows-only wrapper; stable. |
| OpenCLNet (FLACCL) | `ThirdParty/openclnet` submodule | pin `4a10612b` | dormant upstream | Used by FLACCL GPU encoder; verify against current OpenCL runtimes. |
| CUDA.NET (FlaCuda) | `ThirdParty/CUDA.NET.dll` 2.3.7 | abandoned (2010) | abandoned | Retire: FlaCuda is orphaned (absent from sln) and superseded by FLACCL. See D5. |

## FlaCuda -> FLACCL retirement (confirmed direction)

FlaCuda (`CUETools.Codecs.FlaCuda`, `CUETools.FlaCudaExe`) is already orphaned (not in `CUETools.sln`) and its CUDA.NET reference points outside the repo. FLACCL (OpenCL, `CUETools.Codecs.FLACCL`) is the living GPU FLAC encoder. Action: delete the FlaCuda projects and `CUDA.NET.dll` under D5 (still deferred by the user until rollout completes). No functional loss - FLACCL replaces it.

## Missing codecs - NEEDS USER INPUT

Not present today (candidates to add, pending the user's list): **Opus**, **Vorbis**, **AAC** (beyond ffmpeg), **DSD/DSF** (SACD), **Musepack**, **True Audio v2**. ffmpeg is already wired, so some of these could be exposed through the existing ffmpeg path rather than new native libs. **Do not implement additions until the user specifies which formats they want and whether encode, decode, or both.**

## Sequencing / risk

- Codec upgrades are behavior-affecting: bit-exactness must be preserved. The golden-corpus tests (modernization idea 3) should exist before bumping decoders/encoders; TestCodecs (34/34) covers a subset today.
- Native bumps (libFLAC, WavPack, MAC) require re-checking that `ThirdParty/*.patch` still applies and the managed P/Invoke wrappers still match the ABI.
- Most-current already: libFLAC, WavPack, LAME, ffmpeg. Most-behind: taglib-sharp, possibly MAC.
- Do one codec at a time with a decode/encode round-trip verification (extend the ziptest-style harness pattern).

## Status

Version table done (this doc). Additions blocked on the user's codec list. FlaCuda retirement folds into D5. Native version bumps queued after golden-corpus tests exist.
