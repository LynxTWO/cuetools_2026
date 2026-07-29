# Codec Refresh - Version Table and Scope (R13)

User request 2026-07-02: upgrade all codecs to latest versions/builds, add any missing ones, and retire FlaCuda in favor of FLACCL. This document preserves that survey and records the later retirement result. On 2026-07-29 the user selected xHE-AAC, OptimFROG, WavPack, and every redistributable codec available from `C:\_Audio Codecs_`; the product-choice boundary is closed.

"Current" versions are the repository snapshot observed during the 2026-07-02 survey (submodule pins, vendored binary metadata, and history). Upstream versions in the table were re-checked against authoritative release pages on 2026-07-26. A version match is not a compatibility or release-quality test.

## Current vs latest

| Codec / lib | Where | Current (verified) | Latest (inferred) | Assessment |
| --- | --- | --- | --- | --- |
| libFLAC | `ThirdParty/flac` submodule + `CUETools.Codecs.libFLAC` | 1.5.0, tag commit `1507800` | 1.5.0 | Current upstream release. |
| WavPack | `ThirdParty/WavPack` submodule + `.libwavpack` | 5.9.0, tag commit `5803634` | 5.9.0 | Current. The CUETools patch applies cleanly, both architectures rebuild without warnings, and the focused lifecycle plus real round-trip gate passes 2/2. |
| Monkey's Audio (APE) | `ThirdParty/MAC_SDK` (`MAC_1320_SDK.zip`) + `.MACLib` | 13.20 | 13.20 SDK | Current. The CUETools stream wrapper was adapted from `CIO` to `IAPEIO`; Win32/x64 builds and real 16/24-bit verified round trips passed. |
| LAME (MP3) | vendored x86/x64 `libmp3lame.dll` + `.libmp3lame` | 3.100 (RareWares DLLs built 2017-10-22; imported in 2021) | 4.0 (July 2026) | Major-version drift. The release arrived after the local LAME-v4 research started. Do not ship a blind DLL change without ABI, quality, metadata, and decode-compatibility gates. |
| ffmpeg | manual `build_ffmpeg_dlls.yml` through a pinned vcpkg commit; managed wrapper uses FFmpeg.AutoGen | 7.1.1 | 8.1.2 | Behind, but neither primary package currently ships the managed/native FFmpeg set. Refresh the standalone workflow and wrapper together if that product path is restored. |
| taglib-sharp | `ThirdParty/taglib-sharp` submodule | `b5ae84f2` / `TaglibSharp-2.3.0.0` | 2.3.0.0 | Current release, with extensive local source changes that still require provenance/diff maintenance. |
| TTA | `ttalib-1.1/` (in-repo C++) + `.TTA` (C++/CLI) | TTA library 1.1 (2005 source) | TTA C++ library 2.3 (2015) | Dormant but materially behind. Its C++/CLI and classic-only shape makes an upgrade or retirement a compatibility decision, not a blind source swap. |
| ALAC | `CUETools.Codecs.ALAC` (managed) + Apple ref | Apple ALAC reference | unchanged | Stable; no action. |
| WMA | `CUETools.Codecs.WMA` + `WindowsMediaLib` submodule | WM Format SDK wrapper | unchanged | Windows-only wrapper; stable. |
| OpenCLNet (FLACCL) | `ThirdParty/openclnet` submodule | pin `4a10612b` | dormant upstream | Used by FLACCL GPU encoder. The corrected exact-length verifier passed on an RTX 3060 across OpenCL modes 0-8, CPU workers, 24-bit input, and an exact frame boundary; additional vendors/drivers remain a compatibility matrix, not a prerequisite for calling this host observed. |
| CUDA.NET (FlaCuda) | Historical `ThirdParty/CUDA.NET.dll` 2.3.7 | abandoned (2010) | abandoned | Retired. FlaCuda projects and CUDA.NET were deleted in commit `4e1b02d`; FLACCL is the remaining GPU FLAC path. |

## FlaCuda -> FLACCL retirement (confirmed direction)

FlaCuda (`CUETools.Codecs.FlaCuda`, `CUETools.FlaCudaExe`) and its CUDA.NET dependency were deleted in commit `4e1b02d` after reachability checks confirmed that they were outside the solution and release paths. FLACCL (OpenCL, `CUETools.Codecs.FLACCL`) is the remaining GPU FLAC encoder. It now has real RTX 3060/OpenCL coverage, including modes 0-8, CPU-worker and 24-bit cases, and the exact-length boundary. One device does not prove every OpenCL implementation, so cross-vendor coverage remains desirable.

## Requested codec additions

The user requested xHE-AAC, OptimFROG, WavPack, and all redistributable encoders available in `C:\_Audio Codecs_`. WavPack is packaged in-process. The supplied command-line set contains Musepack, Ogg Vorbis, Opus, qaac, and TAK. Each command encoder still needs an exact executable contract, settings and help text, provenance, and a redistribution decision. A user import can be supported even when bundling is not permitted.

## Sequencing / risk

- Codec upgrades are behavior-affecting: bit-exactness must be preserved. Aggregate
  suite counts are refreshed by the final canonical gate; real executable/native
  coverage still depends on installed runtimes, hardware, and corpus fixtures.
- Native bumps require re-checking local patches or wrappers against the new ABI.
- Current upstream releases: libFLAC, WavPack, Monkey's Audio, and taglib-sharp. Known drift: LAME 3.100 -> 4.0, and the
  standalone/unshipped FFmpeg path 7.1.1 -> 8.1.2.
- Do one codec at a time with a decode/encode round-trip verification (extend the ziptest-style harness pattern).

## Status

The historical version survey is retained above. FlaCuda retirement is complete.
WavPack 5.9.0 and Monkey's Audio 13.20 have both-architecture build and runtime
evidence. The codec wishlist is resolved and command-line integrations are active
work. LAME 4 remains a separate major-version project. FFmpeg matters if its
currently unshipped product path returns.

## Upstream release evidence checked 2026-07-26

- FLAC: `https://github.com/xiph/flac/releases/tag/1.5.0`
- WavPack: `https://github.com/dbry/WavPack/releases/tag/5.9.0`
- Monkey's Audio SDK: `https://www.monkeysaudio.com/developers.html`
- LAME: `https://lame.sourceforge.io/`
- FFmpeg: `https://ffmpeg.org/download.html`
- taglib-sharp: `https://github.com/mono/taglib-sharp/releases/tag/TaglibSharp-2.3.0.0`
