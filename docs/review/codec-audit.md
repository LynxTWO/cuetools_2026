# Codec Audit - Current State

Current-state refresh: 2026-07-26. This supersedes the 2026-07-02 snapshot in this
file. The earlier snapshot treated solution membership, packaging, and runtime
reachability as one graph. They are different here.

This audit covers the two primary Windows products, their optional external encoder
paths, and codec projects that remain in the solution but are not in either primary
package. It does not treat a project as shipped merely because it builds in the
solution.

## Evidence and status vocabulary

The main evidence anchors are:

- [WPF project and codec copy allowlist](../../CUETools.Wpf/CUETools.Wpf.csproj)
- [WPF release contract](../../eng/release/wpf-win-x64.manifest.json)
- [classic collection script](../../collect_files.bat)
- [classic release contract](../../eng/release/classic-win.manifest.json)
- [plugin discovery and trust gate](../../CUETools.Processor/CUEProcessorPlugins.cs)
- [base format and command-line configuration](../../CUETools.Codecs/CUEToolsCodecsConfig.cs)
- [WPF external encoder catalog](../../CUETools.Wpf/Services/EncoderCatalog.cs)
- [native dependency ledger](../../eng/release/native-dependencies.json)
- [test-suite contract](../../eng/ci/test-suites.json)

Status terms:

- `reachable-observed`: the named build or test loaded and exercised the path.
- `configured-not-observed`: source, build, package, and loader edges exist, but this
  audit did not observe the final runtime behavior.
- `blocked`: a required dependency or toolchain was unavailable.
- `not shipped`: the project may remain buildable, but neither primary release copies it.

## Product boundaries

### WPF / .NET 8 x64

The WPF product has a curated in-process plugin set. Its project copies exactly nine
managed codec plugins and five x64 native codec libraries. The release contract requires
34 paths, hash-binds 14 runtime files, expects nine plugin files to register 19 types, and
runs five native probes. The 19 registrations are nine encoders, nine decoders, and the
HDCD filter. WAV contributes one encoder and one decoder from the base codec assembly and
is not part of that plugin count.

The nine managed plugins are ALAC, Flake, HDCD, libFLAC, libmp3lame, libwavpack, MACLib,
MPEG, and WMA. The five native files are `hdcd.dll`, `libFLAC_dynamic.dll`,
`libmp3lame.dll`, `wavpackdll.dll`, and `MACLibDll.dll`, all under the architecture
directory inside `plugins`.

The package is fail-closed by default. `CUEProcessorPlugins` reads the generated hash
manifest, filters entries for the current architecture, rehashes and preloads each
required native dependency by approved full path, verifies the returned module path,
and then loads only approved managed plugins. Native handles remain loaded and wrapper
loaders have no bare-name fallback. Loose discovery requires the explicit
local-development environment switch.

Observed on 2026-07-26:

- native codec integration tests: 16 passed, 0 skipped;
- focused native-wrapper lifetime tests: 10 passed, 0 skipped;
- focused manifest/load-time trust tests: 13 passed, 0 skipped;
- libFLAC, WavPack, and Monkey's Audio round trips ran through the net8 wrappers;
- the net47 lifetime gate added real libFLAC/WavPack/LAME encodes plus HDCD valid
  and rejected construction paths;
- HDCD processing and synthetic Blu-ray LPCM decode ran;
- plugin trust tests rejected malformed, reordered, wrong-path, and wrong-identity inputs.

### Classic / .NET Framework 4.7

Classic is broader. `collect_files.bat` and the classic release contract include the WPF
codec families plus FLACCL/OpenCL and architecture-specific TTA. The contract requires 63
paths. Its trust manifest contains 34 entries, of which 26 are applicable to an x64
runtime. The remaining entries include the Win32 TTA/RAR/native variants used by the x86
build.

Classic TTA is not C#. It is a C++/CLI `.vcxproj` over vendored `ttalib-1.1`, built
separately for Win32 and x64. See the
[TTA wrapper project](../../CUETools.Codecs.TTA/CUETools.Codecs.TTA.vcxproj).

The classic package graph is `configured-not-observed` in this local audit. The full
AnyCPU/x64/Win32 release matrix requires the hosted Visual Studio toolchain. The net47
codec test assembly did run locally, but that does not exercise every classic packaged
plugin.

## Primary codec matrix

| Format or function | WPF x64 | Classic | Engine and current status |
| --- | --- | --- | --- |
| WAV / PCM | base encode + decode registered | same | Managed base codec; its net47 codec tests passed. This audit did not run a WPF-specific WAV codec probe. |
| FLAC | Flake + libFLAC encode/decode registered | Flake + libFLAC plus FLACCL/OpenCL | The libFLAC round trip ran on net8; managed Flake ran in the net47 suite. libFLAC uses source-built 1.5.0. FLACCL is classic-only and has the residual defect below. |
| ALAC / M4A | managed encode/decode registered | same | Its net47 codec tests passed. Verify defaults on. Path output is staged and published after finalization; this audit did not run a WPF-specific ALAC round trip. |
| WavPack | native wrapper encode/decode, observed on net8 | packaged | Source-built WavPack 5.8.1; finalized output is independently decoded and compared when verify is on. Upstream 5.9.0 is pending compatibility work. |
| Monkey's Audio / APE | native wrapper encode/decode, observed on net8 | packaged | Source-built MAC 10.86; finalized output is independently decoded and compared when verify is on. Upstream 13.20 is pending wrapper/corpus work. |
| WMA | lossless/lossy encode + decode registered | same | Uses the Windows Media runtime. The lossless verification harness passed; the real round trip was skipped because this host lacks the lossless codec capability. |
| MP3 | LAME VBR/CBR encode registered | same | Vendored LAME 3.100 x64/x86 DLLs; a real current-wrapper encode passed. Upstream released 4.0 in July 2026. Neither primary package registers an MP3 decoder, and 4.0 ABI/quality/decode compatibility remains unobserved, so the major bump is not safe to infer. |
| TTA | not shipped | encode + decode configured | C++/CLI wrapper over `ttalib-1.1`; configured for Win32/x64, not observed in this audit. |
| DVD-A / Blu-ray LPCM | ATSI, BDLPCM, MPLS decoders registered | same | Managed MPEG plugin. Synthetic BDLPCM decode passed on net8. |
| HDCD | native decode filter registered and observed | packaged | Managed wrapper plus vendored native `hdcd.dll`; original source/build provenance remains unknown. |
| TAK | optional external executable | optional external executable | `takc.exe` encode/decode contract. Lossless output cannot be used without its self-decoder verification contract. |
| Ogg Vorbis / Opus | optional external encode | optional external encode | `oggenc.exe` and `opusenc.exe`; no in-process implementation or primary-package decoder. |
| Musepack | optional WPF import | user-configurable external path | WPF registers `mpcenc.exe` output only when usable; no bundled executable or decoder. |
| AAC / M4A | optional external lossy encode | optional external lossy encode | WPF curates `qaac.exe`; base config also retains qaac/Nero command entries. ALAC remains the in-process M4A lossless path. |

Availability in the table means the named package or explicit external-executable path.
It does not mean every format key in `CUEToolsCodecsConfig` has a usable encoder and
decoder. The UI must still apply `EncoderCatalog.IsUsable`.

## Projects outside the primary package graph

| Project or capability | Current disposition |
| --- | --- |
| `CUETools.Codecs.ffmpeg` | Still in the solution and references FFmpeg.AutoGen 7.1.1, but `CUETools.Codecs.ffmpegdll.dll`, `FFmpeg.AutoGen.dll`, and the FFmpeg native DLL family are no longer copied by classic or WPF. It is `not shipped`. |
| FFmpeg native DLL workflow | The manual workflow still produces standalone x86/x64 FFmpeg 7.1.1 artifacts while upstream stable is 8.1.2. Those artifacts are not inputs to either primary release. |
| `ffmpeg.exe` command encoder | Separate from the managed wrapper. The built-in external ALAC command path remains supported and its real self-verification test passed when `ffmpeg.exe` was present. |
| LossyWAV | The classic collection script copies the standalone DLL and CLI. It is not a dynamically registered `IAudioEncoderSettings` plugin, so this audit does not present `lossy.flac` as a proven integrated GUI codec pipeline. |
| CoreAudio, DirectSound, Icecast | Solution components used by the separate player/output tools. They are not WPF or classic CUETools release codec plugins. |
| FlaCuda | Deleted. There is no current source directory, solution entry, copy rule, or release-manifest entry. FLACCL remains the only GPU FLAC encoder. |

The managed FFmpeg wrapper must not be described as a primary-package decoder for AIFF,
Shorten, MLP, MPEG audio, or any other format. Those settings remain in its source, but
the wrapper and required native DLLs do not ship.

## Verification and publication tiers

Verification is an assurance contract, not one Boolean shared by all lossless formats.

| Tier | Codecs | What is actually checked |
| --- | --- | --- |
| A: finalized-file independent decode | WavPack, Monkey's Audio, WMA Lossless, lossless command encoders | Finalize the staged output, reopen or independently decode it, compare PCM format, exact sample count, and SHA-256 of the accepted PCM, then publish. Command encoders also require a successful verifier process and final output drain. |
| B: encoder-integrated stream verification | libFLAC | libFLAC verifies encoded audio internally; `finish()` is checked so a final-block mismatch cannot close as success. The work file publishes only after finish succeeds. This is not an independent reopen of the final container. |
| C: per-frame decode and compare | Flake, ALAC | Each encoded frame is decoded and every sample is compared. Verify defaults on. This catches frame corruption but does not independently reopen and validate the complete container. ALAC now stages path output before publication; Flake still writes its requested file directly. |
| D: no current whole-output oracle | WAV, TTA, FLACCL | WAV and TTA have no independent post-output PCM proof. FLACCL has an optional per-frame verifier, but it defaults off and has the exact-length defect below. |

Lossy formats are not bit-exact contracts. Their gates should check process/finalization
success, output existence, decodability, duration, and format rather than PCM equality.

Relevant implementation evidence:

- [shared finalized-output verification](../../CUETools.Codecs/LosslessPcmVerification.cs)
- [external lossless verification](../../CUETools.Codecs/CommandLine/LosslessOutputVerifier.cs)
- [WMA lossless verification](../../CUETools.Codecs.WMA/WmaLosslessVerification.cs)
- [libFLAC writer](../../CUETools.Codecs.libFLAC/Writer.cs)
- [WavPack writer](../../CUETools.Codecs.libwavpack/Writer.cs)
- [Monkey's Audio writer](../../CUETools.Codecs.MACLib/AudioEncoder.cs)
- [Flake writer](../../CUETools.Codecs.Flake/AudioEncoder.cs)
- [ALAC writer](../../CUETools.Codecs.ALAC/ALACWriter.cs)

## Test evidence

The net47 codec suite was run on 2026-07-26:

```text
Total: 99
Passed: 97
Skipped: 2
Failed: 0
```

The two skips are expected and bounded by `eng/ci/test-suites.json`:

- `AccurateRipVerifyTest.asdaTest` is explicitly ignored and contains a hard-coded
  developer LocalDB scratch path.
- `RealLosslessRoundTripVerifiesWhenWindowsCodecIsAvailable` reports inconclusive when
  the host does not expose Windows Media Lossless.

The suite now covers WAV primitives, managed FLAC and ALAC behavior, libFLAC,
finalization and publication failures, external-process watchdogs, real FFmpeg ALAC
self-verification when available, WMA verification logic, and output race handling. It
does not turn TTA, FLACCL, MP3, or every external executable into
`reachable-observed`.

Focused WPF codec/trust tests also passed 16/16 on net8, while the focused
manifest trust selection passed 13/13. The source is in:

- [codec import integration tests](../../CUETools.Wpf.Tests/CodecImportIntegrationTests.cs)
- [runtime trust tests](../../CUETools.Wpf.Tests/RuntimeTrustTests.cs)
- [command encoder tests](../../CUETools/CUETools.TestCodecs/CommandLineEncoderTest.cs)
- [WMA verification tests](../../CUETools/CUETools.TestCodecs/WmaLosslessVerificationTest.cs)

## Open codec risks

### FLACCL exact-length verification defect

FLACCL is reachable only through the classic net47/OpenCL package. Its verify setting is
`[DefaultValue(false)]`. When enabled, it passes the exact encoded frame length to the
managed FLAC decoder:

```csharp
task.verify.DecodeFrame(task.frame.writer.Buffer, task.frame.writer_offset, fs)
```

It did not receive Flake's bounded verify lookahead or the decoder end bound that fixed
the managed FLAC exact-length failure. The shared GPU task buffer can have very little
trailing slack on a one-compute-unit device, so this must be fixed and run on an OpenCL
host rather than patched by analogy. Until then, do not enable FLACCL verification by
default or claim its verify path is safe. Evidence:
[FLACCL settings and writer](../../CUETools.Codecs.FLACCL/FLACCLWriter.cs).

### Remaining evidence gaps

- Run the full classic Release AnyCPU, x64, and Win32 artifact gates on hosted Visual
  Studio, including TTA selection and invocation.
- Run FLACCL exact-length and final-frame tests on real OpenCL hardware before changing
  its default.
- Add real MP3 encode/decode-duration coverage. The primary packages currently encode
  MP3 but do not register an MP3 decoder.
- Run the WMA Lossless real round trip on a Windows image that exposes that codec.
- Decide whether WAV and TTA need finalized-file independent verification or a narrower
  UI claim.
- Treat the standalone FFmpeg workflow as a separate optional distribution unless a
  primary release deliberately imports and validates the complete native family.

## Historical corrections

The 2026-07-02 audit remains useful as a record of the questions asked, but these claims
are superseded:

- `34/34` is no longer the codec-suite result; the current result is 97 passed and 2
  expected skips out of 99.
- TTA is C++/CLI, not managed C#.
- FlaCuda is deleted, not merely orphaned.
- WPF and classic do not have the same codec set.
- The managed FFmpeg wrapper is not shipped by either primary product.
- Ogg, Opus, Musepack, TAK, and AAC output are optional external-executable capabilities,
  not bundled in-process codecs.
- Frame verification and finalized-file independent verification are different assurance
  tiers.
