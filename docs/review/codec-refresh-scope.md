# Codec Refresh - Version Table and Scope (R13)

User request 2026-07-02: upgrade all codecs to latest versions/builds, add any missing ones, and retire FlaCuda in favor of FLACCL. This document preserves that survey and records the later retirement result. The "which codecs to add" part needs an explicit product choice and is not guessed here.

"Current" versions are the repository snapshot observed during the 2026-07-02 survey (submodule pins, vendored binary metadata, and history). Upstream versions in the table were re-checked against authoritative release pages on 2026-07-26. A version match is not a compatibility or release-quality test.

## Current vs latest

| Codec / lib | Where | Current (verified) | Latest (inferred) | Assessment |
| --- | --- | --- | --- | --- |
| libFLAC | `ThirdParty/flac` submodule + `CUETools.Codecs.libFLAC` | 1.5.0, tag commit `1507800` | 1.5.0 | Current upstream release. |
| WavPack | `ThirdParty/WavPack` submodule + `.libwavpack` | 5.8.1 | 5.9.0 | Behind one maintenance release. The submodule has local project changes, so preserve and reconcile them during the upgrade. |
| Monkey's Audio (APE) | `ThirdParty/MAC_SDK` (`MAC_1086_SDK.zip`) + `.MACLib` | 10.86 | 13.20 SDK | Substantially behind. Upstream changed its DLL interface generation after this pin; rebuild the wrapper and golden corpus rather than swapping binaries. |
| LAME (MP3) | vendored x86/x64 `libmp3lame.dll` + `.libmp3lame` | 3.100 (2017 release; DLLs built in 2021) | 4.0 (July 2026) | Major-version drift. The release arrived after the local LAME-v4 research started. Do not ship a blind DLL change without ABI, quality, metadata, and decode-compatibility gates. |
| ffmpeg | manual `build_ffmpeg_dlls.yml` through a pinned vcpkg commit; managed wrapper uses FFmpeg.AutoGen | 7.1.1 | 8.1.2 | Behind, but neither primary package currently ships the managed/native FFmpeg set. Refresh the standalone workflow and wrapper together if that product path is restored. |
| taglib-sharp | `ThirdParty/taglib-sharp` submodule | `b5ae84f2` / `TaglibSharp-2.3.0.0` | 2.3.0.0 | Current release, with extensive local source changes that still require provenance/diff maintenance. |
| TTA | `ttalib-1.1/` (in-repo C++) + `.TTA` (C++/CLI) | TTA1 SDK 1.1 | TTA1 SDK ~1.x (stable/dormant) | Effectively current; upstream is dormant. |
| ALAC | `CUETools.Codecs.ALAC` (managed) + Apple ref | Apple ALAC reference | unchanged | Stable; no action. |
| WMA | `CUETools.Codecs.WMA` + `WindowsMediaLib` submodule | WM Format SDK wrapper | unchanged | Windows-only wrapper; stable. |
| OpenCLNet (FLACCL) | `ThirdParty/openclnet` submodule | pin `4a10612b` | dormant upstream | Used by FLACCL GPU encoder; verify against current OpenCL runtimes. |
| CUDA.NET (FlaCuda) | Historical `ThirdParty/CUDA.NET.dll` 2.3.7 | abandoned (2010) | abandoned | Retired. FlaCuda projects and CUDA.NET were deleted in commit `4e1b02d`; FLACCL is the remaining GPU FLAC path. |

## FlaCuda -> FLACCL retirement (confirmed direction)

FlaCuda (`CUETools.Codecs.FlaCuda`, `CUETools.FlaCudaExe`) and its CUDA.NET dependency were deleted in commit `4e1b02d` after reachability checks confirmed that they were outside the solution and release paths. FLACCL (OpenCL, `CUETools.Codecs.FLACCL`) is the remaining GPU FLAC encoder. FLACCL still needs real OpenCL-device coverage; deletion of FlaCuda does not prove FLACCL runtime compatibility.

## Missing codecs - NEEDS USER INPUT

Candidates from the original survey were **Opus**, **Vorbis**, **AAC** (beyond the available ffmpeg route), **DSD/DSF** (SACD), **Musepack**, and **True Audio v2**. Adding a format changes product support, packaging, verification, and long-term dependency ownership. It remains a product decision: define the wanted formats and whether encode, decode, or both before implementation.

## Sequencing / risk

- Codec upgrades are behavior-affecting: bit-exactness must be preserved. The current codec suite discovers 109 tests (107 pass and 2 environment-dependent skips), but real executable/native coverage still depends on installed runtimes and corpus fixtures.
- Native bumps (libFLAC, WavPack, MAC) require re-checking that `ThirdParty/*.patch` still applies and the managed P/Invoke wrappers still match the ABI.
- Current upstream releases: libFLAC and taglib-sharp. Known drift: WavPack
  5.8.1 -> 5.9.0, Monkey's Audio 10.86 -> 13.20, LAME 3.100 -> 4.0, and the
  standalone/unshipped FFmpeg path 7.1.1 -> 8.1.2.
- Do one codec at a time with a decode/encode round-trip verification (extend the ziptest-style harness pattern).

## Status

The historical version survey is retained above. FlaCuda retirement is complete.
Format additions still require a product-support decision. The 2026-07-26
upstream refresh makes WavPack, Monkey's Audio, and LAME active upgrade work;
FFmpeg matters if its currently unshipped product path returns.

## Upstream release evidence checked 2026-07-26

- FLAC: `https://github.com/xiph/flac/releases/tag/1.5.0`
- WavPack: `https://github.com/dbry/WavPack/releases/tag/5.9.0`
- Monkey's Audio SDK: `https://www.monkeysaudio.com/developers.html`
- LAME: `https://lame.sourceforge.io/`
- FFmpeg: `https://ffmpeg.org/download.html`
- taglib-sharp: `https://github.com/mono/taglib-sharp/releases/tag/TaglibSharp-2.3.0.0`
