# Codec Audit - CUETools 2026

Full audit of every audio codec CUETools currently supports: format, lossy/lossless, encode/decode, engine type, whether it is up to date, whether it works, better options to add, and formats not yet supported. Requested by the user 2026-07-02. Ground rule from the user: **do not remove old options that work** - additions and upgrades only.

"Current version" is verified from repo evidence (submodule pins, vendored binary metadata, upstream commit messages on this history). "Latest" is best-known as of Jan 2026 and should be re-confirmed at implementation time (`inferred`). "Works" is `verified` where TestCodecs exercises it, otherwise `inferred` (ships in upstream releases).

## Summary matrix

| Format | Loss | Enc | Dec | Engine (project) | Current | Up to date? | Works |
| --- | --- | --- | --- | --- | --- | --- | --- |
| WAV / PCM | lossless | Y | Y | managed (`CUETools.Codecs` WAV) | in-tree | n/a | verified (TestCodecs) |
| AIFF | lossless | - | Y | ffmpeg (`.ffmpeg` AiffDecoderSettings) | AutoGen 7.1.1 | current | inferred |
| FLAC | lossless | Yx3 | Yx2 | native libFLAC (`.libFLAC`), managed Flake (`.Flake`), OpenCL GPU (`.FLACCL`); decode libFLAC + Flake | libFLAC 1.5.0 | current | verified (TestCodecs) |
| FLAC (CUDA) | lossless | Y | - | CUDA GPU (`.FlaCuda`) - ORPHANED | CUDA.NET 2.3.7 (2010) | dead | n/a (not in sln) |
| ALAC | lossless | Y | Y | managed (`.ALAC`) | in-tree (Apple ref-derived) | stable | verified (TestCodecs) |
| Monkey's Audio (APE) | lossless | Y | Y | native MAC SDK (`.MACLib`) | MAC 10.86 (2024-12) | slightly behind | inferred |
| WavPack | lossless/hybrid | Y | Y | native (`.libwavpack`) | WavPack 5.8.1 (2025-02) | current | inferred |
| TTA (True Audio) | lossless | Y | Y | C++/CLI (`.TTA` + `ttalib-1.1`) | TTA1 SDK 1.1 | current (upstream dormant) | inferred |
| Shorten (SHN) | lossless | - | Y | ffmpeg (`.ffmpeg` ShnDecoderSettings) | AutoGen 7.1.1 | current | inferred |
| LossyWAV | lossy (hybrid) | Y | - | managed (`.LossyWAV`) | in-tree | stable | inferred |
| HDCD | lossless decode filter | - | filter | native (`.HDCD`) | vendored hdcd.dll (2010) | old but stable | inferred |
| MP3 | lossy | Y | Y | native LAME (`.libmp3lame`); decode via ffmpeg | LAME 3.100 (2017) | latest release (see R14) | inferred |
| WMA | lossy | Y | Y | native WM Format SDK (`.WMA` + `WindowsMediaLib`) | WM Format SDK | stable (legacy) | inferred |
| MLP / TrueHD | lossless | - | Y | ffmpeg (`.ffmpeg` MLPDecoderSettings) | AutoGen 7.1.1 | current | inferred |
| DVD/Blu-ray audio (LPCM/MPLS/ATSI) | mixed | - | Y | managed (`.MPEG` BDLPCM/MPLS/ATSI) | in-tree | stable | inferred |
| MpegTS / MPEG audio | lossy | - | Y | ffmpeg (`.ffmpeg`) | AutoGen 7.1.1 | current | inferred |
| Icecast stream out | lossy (MP3) | Y (stream) | - | LAME via `.Icecast` | LAME 3.100 | works, fragile (reflection) | inferred |
| WASAPI playback | n/a (output) | - | - | `.CoreAudio` (NAudio WASAPI) | in-tree | stable | inferred |

Notes: "Enc Yx3" for FLAC = three independent encoder implementations (native reference, CUETools' own managed Flake, and the OpenCL GPU FLACCL). `CoreAudio` is **audio output/playback (WASAPI)**, not an AAC encoder - CUETools has **no AAC encoder**. ffmpeg is decode-only in this app.

## Engine-type breakdown

- **Native P/Invoke:** libFLAC, MAC (APE), WavPack, LAME (MP3), HDCD, ffmpeg (FFmpeg.AutoGen).
- **C++/CLI:** TTA (`ttalib-1.1` compiled in-tree).
- **Managed C#:** WAV, Flake (FLAC), ALAC, LossyWAV, MPEG/BDLPCM/MPLS/ATSI containers.
- **GPU:** FLACCL (OpenCL, live), FlaCuda (CUDA, orphaned/dead).
- **OS framework:** WMA (Windows Media Format SDK), CoreAudio (WASAPI output).

## Up-to-date findings

- **Current:** libFLAC 1.5.0, WavPack 5.8.1, ffmpeg (AutoGen 7.1.1), LAME 3.100 (last release - the reason for the R14 v4 effort).
- **Slightly behind:** Monkey's Audio SDK 10.86 (verify latest; MAC releases are frequent). taglib-sharp (tagging, not a codec) is behind (~2.1-era vs 2.3.x).
- **Old but stable / dormant upstream:** TTA1 SDK, HDCD dll (2010), WMA SDK.
- **Dead:** FlaCuda (CUDA.NET 2.3.7, 2010) - orphaned, superseded by FLACCL. Retire under D5 (does not count as removing a *working* shipped option; it isn't built).

## "Does it work?" - test coverage gap

Automated coverage (TestCodecs, 34/34 green) exercises **WAV, FLAC (libFLAC + Flake), ALAC, and the SOX resampler** only. WavPack, APE, TTA, MP3, WMA, HDCD, LossyWAV, and the ffmpeg decoders are **not** in the automated suite - they are `inferred` working because they ship in upstream releases. **Recommendation:** build the golden-corpus round-trip suite (modernization idea 3 / R10) covering every encoder/decoder pair with bit-exact checks before any version bumps, so "works" becomes `verified` for all of them.

## Better options to add (keep the existing working ones)

Per the user's rule, nothing working is removed. Additive improvements:

1. **AAC encoder (new capability).** CUETools can *decode* AAC via ffmpeg but cannot *encode* it. Add AAC encoding via ffmpeg's `libfdk_aac` (best quality, licensing caveat) or the native `aac` encoder, or Apple CoreAudio AAC where available. Fills the most common missing lossy target.
2. **Route more formats through the existing ffmpeg path** rather than new native libs where a native encoder is not required (decode is already ffmpeg-backed).
3. **Managed SIMD FLAC (idea 14).** Add an AVX2 path to Flake so the managed encoder rivals FLACCL/libFLAC on the 5950X-class CPU without a GPU; a candidate to eventually supersede FlaCuda's role entirely. Additive (keep libFLAC + FLACCL).
4. **Keep three FLAC encoders but document when to use which** (libFLAC = reference/compat, Flake = pure-managed/portable, FLACCL = GPU throughput).

## Formats not yet supported (candidates to add)

Pending the user's confirmation of which to build, and whether encode/decode/both:

- **Opus** - modern lossy standard; no support today. Decode + encode via libopus (or ffmpeg). High value.
- **Ogg Vorbis** - lossy; no support today. Decode + encode via libvorbis/ffmpeg.
- **Native AAC encode** - see item 1 above.
- **DSD (DSF / DFF)** - SACD lossless 1-bit; growing demand among archivists. Decode at least; transcode-to-PCM path.
- **Musepack (MPC)** - niche lossy; low priority.
- **OptimFROG / LA / TAK** - niche lossless; TAK especially is popular but closed-source (decode-only SDK).

## Recommended sequence

1. Build the golden-corpus round-trip test suite first (makes "works" verified across the board and guards every later bump).
2. Retire FlaCuda (D5).
3. Version bumps where behind: MAC SDK, taglib-sharp (verify latest, re-apply patches, round-trip test each).
4. Additions per the user's chosen list - Opus and AAC-encode are the highest-value gaps.

## Status

Audit complete. Additions and the "which formats" choice are the user's call (Opus / Vorbis / AAC-encode / DSD / other). FlaCuda retirement folds into D5. Version bumps (MAC, taglib-sharp) queued behind the golden-corpus suite.
