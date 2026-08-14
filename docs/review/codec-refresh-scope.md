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
| ffmpeg | manual `build_ffmpeg_dlls.yml` through pinned vcpkg commit `9e593bb`; managed wrapper uses FFmpeg.AutoGen | 8.1.2 / AutoGen 8.1.0 | 8.1.2 | Current standalone path. Both architectures are source-built and runtime-probed, with immutable build/license/hash evidence. Neither primary package ships the managed/native FFmpeg set. |
| taglib-sharp | `ThirdParty/taglib-sharp` submodule | `b5ae84f2` / `TaglibSharp-2.3.0.0` | 2.3.0.0 | Current release, with extensive local source changes that still require provenance/diff maintenance. |
| TTA | `ttalib-1.1/` (in-repo C++) + `.TTA` (C++/CLI) | TTA library 1.1 (2005 source) | TTA C++ library 2.3 (2015) | Dormant but materially behind. Its C++/CLI and classic-only shape makes an upgrade or retirement a compatibility decision, not a blind source swap. |
| ALAC | `CUETools.Codecs.ALAC` (managed) + Apple ref | Apple ALAC reference | unchanged | Stable; no action. |
| WMA | `CUETools.Codecs.WMA` + `WindowsMediaLib` submodule | WM Format SDK wrapper | unchanged | Windows-only wrapper; stable. |
| OpenCLNet (FLACCL) | `ThirdParty/openclnet` submodule | pin `4a10612b` | dormant upstream | Used by FLACCL GPU encoder. The corrected exact-length verifier passed on an RTX 3060 across OpenCL modes 0-8, CPU workers, 24-bit input, and an exact frame boundary; additional vendors/drivers remain a compatibility matrix, not a prerequisite for calling this host observed. |
| CUDA.NET (FlaCuda) | Historical `ThirdParty/CUDA.NET.dll` 2.3.7 | abandoned (2010) | abandoned | Retired. FlaCuda projects and CUDA.NET were deleted in commit `dc0a70b`; FLACCL is the remaining GPU FLAC path. |

## FlaCuda -> FLACCL retirement (confirmed direction)

FlaCuda (`CUETools.Codecs.FlaCuda`, `CUETools.FlaCudaExe`) and its CUDA.NET dependency were deleted in commit `dc0a70b` after reachability checks confirmed that they were outside the solution and release paths. FLACCL (OpenCL, `CUETools.Codecs.FLACCL`) is the remaining GPU FLAC encoder. It now has real RTX 3060/OpenCL coverage, including modes 0-8, CPU-worker and 24-bit cases, and the exact-length boundary. One device does not prove every OpenCL implementation, so cross-vendor coverage remains desirable.

## Requested codec additions

The user requested xHE-AAC, OptimFROG, WavPack, and all redistributable encoders available in `C:\_Audio Codecs_`. WavPack is packaged in-process. The supplied command-line set contains Musepack, Ogg Vorbis, Opus, qaac, and TAK. The WPF catalog now provides exact encode contracts, selectable implementations, archival defaults, compatible executable aliases, history/best-use help, and receipt-bound user imports for that set plus exhale/xHE-AAC and OptimFROG. User imports take precedence over the packaged fallback.

The WPF package includes three provenance-complete command encoders:

- a deterministic x64 opus-tools 0.2 build using libopusenc 0.3, libopus
  1.6.1, and libogg 1.3.6 under their BSD terms;
- RareWares oggenc2 2.88/libVorbis 1.3.7 with the exact corresponding source
  archive and GPL-2.0 notice;
- a deterministic x64 Musepack SV8 r495 source build with its complete upstream
  archive, CUETools correctness/licensing patch, CMake recipe, build notes, and
  LGPL-2.1 notice.

`eng/release/external-command-encoders.json` pins each download URL, archive
size and SHA-256, executable SHA-256, license, source archive, patch, and build
recipe. Preparation refuses byte drift. The artifact contract repeats the
hashes, and runtime resolution binds the executable to the same
non-replaceable launch lease used for an imported encoder. Real stdin encodes
passed with all three packaged files. Two independent clean Musepack builds
produced the same 378,368-byte executable and SHA-256. Two independent clean
Opus builds produced the same 665,088-byte executable and SHA-256.

The Musepack build deliberately excludes `common/tags.c`: Debian's preserved
copyright inventory records that file separately as all-rights-reserved without
a clear grant, while the encoder/psychoacoustic sources used by the executable
are LGPL-2.1-or-later or BSD. CUETools writes metadata after encoding, so the
ambiguous file is unnecessary and encoder-side tag arguments are rejected.

TAK remains import-only due to its distribution boundary. qaac remains
import-only because it requires Apple's CoreAudioToolbox runtime. exhale
1.2.2 was built from its official source and its actual stdin contract was
verified, but remains import-only because its license explicitly grants no
patent rights. OptimFROG 5.100 passed a real encode/self-decode round trip and
has a complete lossless verifier contract, but its redistribution terms require
notification to the author before an unmodified CLI is packaged.

### Owner codec collection review, 2026-07-29

The expanded `C:\_Audio Codecs_` collection was treated as evidence and source
material, not as an implicit redistribution grant. The repeat census covered
5,233 files (148,270,296 bytes), including 185 files whose names identify
license, notice, version, author, source, or SDK evidence:

- its FLAC 1.5.0 and WavPack 5.9.0 trees match the versions CUETools already
  source-builds;
- its Ogg executable is byte-identical to the packaged, source-accompanied
  RareWares encoder;
- its Musepack binary and source versions do not correspond, which led to the
  reproducible r495 build above instead of copying either opaque binary;
- its `bp0/libhdcd` tree is valid official v1.4 source, but failed the recorded
  old-vs-new behavioral compatibility gate and therefore does not replace the
  legacy decoder;
- FDK-AAC 2.0.3 explicitly grants no patent license. The Apache-2.0 libxaac
  source is a useful xHE-AAC research input, but upstream currently recommends
  only the 64 and 96 kbps USAC operating points and still describes quality
  work in progress. It also supplies a raw testbench rather than CUETools'
  required production M4A publication/metadata transaction;
- `enc_xheaac.dll` and `enc_xheaacf.dll` are validly signed Poikosoft
  EZ CD Audio Converter product components, not a redistributable SDK or
  CUETools command-encoder contract;
- the unversioned `opus-main` snapshot is not a release provenance anchor.
  The packaged Opus encoder instead uses the four official release archives,
  two checked patches, and the deterministic build and behavior gate described
  above;
- qaac, TAK, and OptimFROG retain the import/notification boundaries documented
  above. The presence of binaries in the owner collection does not change
  those upstream terms.

## Sequencing / risk

- Codec upgrades are behavior-affecting: bit-exactness must be preserved. Aggregate
  suite counts are refreshed by the final canonical gate; real executable/native
  coverage still depends on installed runtimes, hardware, and corpus fixtures.
- Native bumps require re-checking local patches or wrappers against the new ABI.
- Current upstream releases: libFLAC, WavPack, Monkey's Audio, taglib-sharp,
  and the standalone FFmpeg 8.1.2 path. Known drift remains LAME 3.100 -> 4.0.
- Do one codec at a time with a decode/encode round-trip verification (extend the ziptest-style harness pattern).

## Status

The historical version survey is retained above. FlaCuda retirement is
complete. WavPack 5.9.0 and Monkey's Audio 13.20 have both-architecture build
and runtime evidence. The command-line catalog and the three safe bundled
integrations are complete for the current release recipe; the deliberately
import-only boundaries above are recorded rather than silently bypassed. LAME
4 remains a separate major-version project. The unshipped FFmpeg path is
modernized and evidence-bound; importing it into either primary product would
still be a separate reachability and packaging decision.

## Upstream release evidence checked through 2026-07-29

- FLAC: `https://github.com/xiph/flac/releases/tag/1.5.0`
- WavPack: `https://github.com/dbry/WavPack/releases/tag/5.9.0`
- Monkey's Audio SDK: `https://www.monkeysaudio.com/developers.html`
- LAME: `https://lame.sourceforge.io/`
- FFmpeg: `https://ffmpeg.org/download.html`
- taglib-sharp: `https://github.com/mono/taglib-sharp/releases/tag/TaglibSharp-2.3.0.0`
- Opus/libopusenc: `https://opus-codec.org/downloads/`
- libogg: `https://xiph.org/downloads/`
- libxaac: `https://github.com/ittiam-systems/libxaac`
- FDK-AAC license: `https://github.com/mstorsjo/fdk-aac/blob/master/NOTICE`
- Musepack preserved source: `https://packages.debian.org/source/stable/libmpc`
