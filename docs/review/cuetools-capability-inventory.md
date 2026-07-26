# CUETools Capability and Settings Inventory

Current-state refresh: 2026-07-26. The original 2026-07-10 inventory described a proposed
unified GUI. The WPF application now exists, so this document separates implemented WPF
behavior, classic behavior, optional capability, and work that remains.

For codec implementation, packaging, verification tiers, and test evidence, see the
[current codec audit](codec-audit.md).

## Evidence anchors

- [WPF startup and registered pages](../../CUETools.Wpf/App.xaml.cs)
- [WPF project and codec allowlist](../../CUETools.Wpf/CUETools.Wpf.csproj)
- [WPF release contract](../../eng/release/wpf-win-x64.manifest.json)
- [classic release contract](../../eng/release/classic-win.manifest.json)
- [classic collection script](../../collect_files.bat)
- [rip service](../../CUETools.Wpf/Services/RipService.cs)
- [verify service](../../CUETools.Wpf/Services/VerifyService.cs)
- [convert service](../../CUETools.Wpf/Services/ConvertService.cs)
- [repair transaction](../../CUETools.Wpf/Services/RepairTransaction.cs)
- [album output transaction](../../CUETools.Wpf/Services/AlbumOutputTransaction.cs)
- [settings store](../../CUETools.Wpf/Services/SettingsStore.cs)
- [WPF app settings](../../CUETools.Wpf/Services/AppSettings.cs)
- [engine configuration](../../CUETools.Processor/CUEConfig.cs)
- [base codec configuration](../../CUETools.Codecs/CUEToolsCodecsConfig.cs)
- [external encoder catalog](../../CUETools.Wpf/Services/EncoderCatalog.cs)
- [SCSI drive reader](../../CUETools.Ripper.SCSI/SCSIDrive.cs)
- [drive calibration service](../../CUETools.Wpf/Accuracy/DriveCalibrationService.cs)

## Product and mode boundaries

CUETools has two primary Windows products over much of the same engine:

| Product | Runtime | Entrypoints | Codec surface |
| --- | --- | --- | --- |
| CUETools WPF | .NET 8, x64 | Rip, Verify, Convert, Queue, Report, Drive, Settings, Naming, Advanced, Explore pages | Curated hash-bound plugin set plus explicitly usable external encoders. |
| Classic CUETools / CUERipper | .NET Framework 4.7, Win32/x64 package variants | Existing WinForms batch/convert and ripper applications | Broader package, including TTA C++/CLI and FLACCL/OpenCL. |

Rip, Verify, and Convert use `CUESheet`, metadata, AccurateRip/CTDB, naming, tagging, and
the same configuration model. They do not have identical inputs, side effects, or codec
availability.

- **Rip** opens an optical drive, reads audio, verifies it, optionally submits CTDB data,
  and can encode an album.
- **Verify** opens an existing rip set or supported lossless input and checks
  AccurateRip/CTDB evidence.
- **Repair** is a verify path with a CTDB correction. It writes into an owned staging
  area, validates the repaired result, and publishes only after success.
- **Convert** opens existing audio through the engine and encodes a selected usable
  output format.
- **Test & Copy** performs independent reads in owned staging, requires agreement, and
  can request a confirming third read when the first two do not agree.

The engine action enum remains `Encode`, `Verify`, `CreateDummyCUE`, and
`CorrectFilenames`. Classic script policies such as `repair`, `fix offset`, and
`encode if verified` remain engine behavior; the WPF services expose a narrower workflow
around them.

## Input and output shapes

`CUESheet.Open` can resolve CUE sheets, single audio files, folders/file groups, supported
archives, and M3U input. A product picker may expose a narrower extension list than the
engine. Do not infer WPF UI reachability from an engine format key alone.

`CUEStyle` still provides:

- `SingleFileWithCUE`
- `SingleFile`
- `GapsPrepended`
- `GapsAppended`
- `GapsLeftOut`

The engine can also create M3U, rip logs, AccurateRip logs, CUE files, and optional TOC
output according to configuration.

WPF rip and convert output use `AlbumOutputTransaction`. The complete album is written to
an owned same-volume sibling directory. Required outputs are checked before one directory
rename publishes the set. A final directory appearing after reservation causes
publication to fail rather than overwrite the competitor. This is atomic visibility of
the album, not a claim of durable storage.

## Repair behavior

CTDB computes `hasErrors`, `canRecover`, and a `CDRepairFix`. The repair path identifies
affected sectors, writes corrected samples through the repair engine, re-runs verification
on corrected audio, validates staged outputs, and then publishes through
`RepairTransaction`.

The original source remains unchanged on failure. A repair result is not called complete
merely because `repair.Write` returned; the staged output and expected correction evidence
must pass before publication.

## Codec availability by product

### WPF in-process set

The WPF release copies nine managed codec plugins:

- ALAC
- Flake
- HDCD
- libFLAC
- libmp3lame
- libwavpack
- MACLib
- MPEG
- WMA

It also packages five x64 native codec dependencies for HDCD, libFLAC, LAME, WavPack,
and Monkey's Audio under `plugins/x64`. WAV encode/decode lives in the base codec
assembly.

The WPF release contract requires 34 paths. Its plugin trust contract contains 14 exact
hash entries, expects nine managed plugin files, 19 registrations, and five native
probes. This is the package contract, not a count of every framework DLL in a
self-contained publish.

Before managed plugin instantiation, each required native entry is rehashed under
deny-write/delete sharing and loaded by its approved full path. The returned module
path is checked, handles remain loaded for process lifetime, and wrapper loaders no
longer fall back to an unmanifested bare DLL name.

The directly available output families are WAV, FLAC, ALAC/M4A, WavPack, APE, WMA
lossless/lossy, and MP3. Actual WMA capability still depends on the Windows Media runtime.
HDCD is a filter, not an output container. The MPEG plugin supplies ATSI, Blu-ray LPCM,
and MPLS decoders.

### Classic additions

Classic copies the same main codec families and additionally packages:

- FLACCL and its OpenCL kernel/dependency;
- Win32 and x64 TTA C++/CLI wrappers over `ttalib-1.1`;
- the standalone LossyWAV CLI and library.

The classic release contract requires 63 paths and hash-binds 34 plugin/dependency files.
Only 26 of those files apply to an x64 runtime because the manifest also contains Win32
variants.

The managed FFmpeg wrapper is no longer in either primary package. Its project remains in
the solution, and a separate manual workflow can build FFmpeg native DLL artifacts, but
that does not make its decoders reachable in classic or WPF.

### External encoders

`CUEToolsCodecsConfig` retains configurable command-line entries for FLAC, TAK, FFmpeg
ALAC, LAME, Ogg Vorbis, Opus, Nero AAC, and qaac. WPF's import UI curates five executable
identities:

- `mpcenc.exe`
- `takc.exe`
- `oggenc.exe`
- `opusenc.exe`
- `qaac.exe`

An imported executable is copied through an owned stage and recorded with name, size, and
SHA-256 approval data. The approval is local user intent, not publisher authentication.
An app-managed executable whose current bytes do not match its receipt is refused. The
approved size/hash is runtime-only (not self-authorizable JSON); the exact executable is
rehashed through a retained deny-write/delete handle immediately before immediate or
deferred launch and remains leased through self-verification.

Lossless command encoders require an independent decoder contract. The built-in verified
identities are `flac.exe`, `takc.exe`, and `ffmpeg.exe` for ALAC. The encoder path,
arguments, verifier path, and verifier arguments are frozen when encoding starts. Missing
verification fails before the producer process starts.

OptimFROG is not registered. The local SDK is decode-only and the repository does not
contain an evidenced encoder plus independent decoder contract.

### Verification levels

The UI must not give every lossless format the same guarantee:

- WavPack, Monkey's Audio, WMA Lossless, and lossless command encoders verify the
  finalized staged output through an independent decoder.
- libFLAC uses its integrated verify mode and checks `finish()`, including final-block
  failures.
- Flake and ALAC decode and compare each encoded frame.
- WAV and TTA have no independent whole-output PCM oracle.
- FLACCL verification defaults off and still has an unresolved exact-length decoder
  boundary defect. FLACCL is classic-only.

The net47 codec suite currently discovers 109 tests: 107 passed and two expected tests were
skipped on the 2026-07-26 run. Focused WPF codec-import tests passed 16/16 and
manifest trust tests passed 13/13.

## Encoder settings

The live encoder editor still binds through `EncoderListViewModel` and
`DecoderListViewModel`. Important current defaults and limits:

| Encoder | Product | Important settings or assurance |
| --- | --- | --- |
| Flake | WPF + classic | FLAC modes, padding, MD5, predictor/window/stereo controls; verify defaults on. |
| libFLAC | WPF + classic | modes 0-8, padding, MD5; native verify defaults on and final `finish()` is checked. |
| FLACCL | classic only | OpenCL settings, MD5, non-subset controls; verify defaults off pending the exact-length fix. |
| ALAC | WPF + classic | modes 0-10; verify defaults on; current encoder accepts 44.1 kHz, stereo, 16-bit PCM. |
| libwavpack | WPF + classic | fast/normal/high/high+, extra mode, MD5, threads; finalized-output verify defaults on. |
| MACLib | WPF + classic | fast through insane; finalized-output verify defaults on. |
| WMA Lossless | WPF + classic | OS-enumerated profiles; finalized-output verify defaults on. |
| WMA lossy | WPF + classic | OS-enumerated VBR/profile settings; no lossless PCM-equality claim. |
| LAME | WPF + classic | VBR and CBR settings; lossy output, no bit-exact claim. |
| TTA | classic only | C++/CLI implementation; no independent finalized-output verifier. |

The WPF one-time archival-default migration may select a stronger mode than a codec
class's type-level default. After migration, the saved user choice wins.

## Settings ownership

The current stores are product-specific:

- WPF persists engine `CUEConfig` plus WPF `AppSettings` through `SettingsStore`.
- `CUEConfigAdvanced` remains the attributed JSON portion of engine configuration.
- Classic CUERipper retains `CUERipperConfig` for per-drive XML state.
- WPF drive calibration is separate JSON keyed by the drive AccurateRip signature.
- The EAC CTDB plugin retains its own registry-backed settings.

WPF and classic protect a nonempty proxy password with the platform's Windows data
protection API before committing settings. A platform that cannot protect a nonempty
secret fails the save instead of silently dropping it.

### General and WPF lifecycle

Engine settings include single-instance/update/language behavior, decoding-thread choice,
eject behavior, metadata limits, tagging, and output layout. WPF adds:

- prevent sleep during rip, default on;
- lock tray during rip, default off;
- stop on unrecoverable read, default off;
- adaptive read speed, default on;
- deep recovery, default on;
- selected output base directory, format, and secure depth;
- per-format lossless/lossy overrides;
- external encoder approval receipts;
- naming template and cleanup rules.

### Metadata and network

The engine retains CTDB, AccurateRip, MusicBrainz-through-CTDB, gnudb/freedb, metadata
search, cover search, and proxy settings. Proxy fields are `UseProxyMode`, server, port,
user, and protected password. CTDB server TLS behavior remains an external service
boundary and is not proven by codec tests.

### AccurateRip and CTDB

Current configuration includes confidence thresholds, offset fixing, verified-output
policy, CTDB submission/ask behavior, detailed logging, AccurateRip log/tag controls, and
CTDB tag controls. These settings belong to the engine and are shared where a product
invokes the matching workflow.

### Naming and output layout

The engine retains per-track/single-file formats, filename cleansing, gap style, CUE/M3U
creation, log embedding/extraction, TOC creation, tagging, HTOA, and album-art controls.
WPF adds a separate naming scheme and applies it through `OutputLayout`; it does not store
WPF-only tokens back into the engine's `trackFilenameFormat`.

### HDCD

The engine retains detect/decode, 20/24-bit output, wait-before-decode, LossyWAV 16-bit
handling, and extra-sample truncation settings. WPF defaults to detection on and decode
off.

## Drive accuracy: implemented and remaining

The 2026-07-10 inventory said cache defeat and read-speed control did not exist. That is no
longer true.

Implemented:

- a read-only drive cache probe;
- measured flush-size cache defeat for secure re-reads;
- supported/minimum speed probing;
- adaptive speed requests applied at safe read-window boundaries;
- deep recovery with progress-aware retrying and slow-to-floor behavior;
- same-drive Test & Copy with a third read when needed;
- persisted drive calibration with confirmed/estimated/unconfirmed confidence.

Still not implemented:

- lead-in overread probing;
- lead-out overread probing;
- dual-drive cross-verification.

`DriveCalibrationService` currently writes both overread flags as false with an explicit
follow-up comment. The data fields existing in `DriveCalibration` do not make the hardware
probe real.

Classic per-drive settings still include read offset, C2 mode, and read command. WPF uses
its own app setting for Burst/Secure/Paranoid depth and its calibration store for cache and
speed behavior.

## Current WPF information architecture

The application registers these pages at startup:

1. Rip
2. Verify
3. Convert
4. Queue
5. Report
6. Drive
7. Settings
8. Naming
9. Advanced
10. Explore

This list proves the surfaces exist and are composed into the app. It does not by itself
prove every control has been exercised on real hardware.

The earlier proposal to build one settings service, reuse encoder/decoder view models, and
share a shell over `CUESheet` is now implemented in the WPF source. The remaining
capability work is narrower:

- run the complete WPF artifact and hardware smoke matrix on supported Windows hosts;
- run the full classic Win32/x64 release matrix on hosted Visual Studio;
- implement and validate lead-in/lead-out overread before exposing those flags as
  calibrated capability;
- decide whether dual-drive comparison belongs in this product;
- close the FLACCL exact-length verification defect on real OpenCL hardware;
- add behavioral coverage for TTA and MP3, and a WMA Lossless run on a capable host.

## Historical corrections

The 2026-07-10 audit remains useful for the original settings inventory, but these claims
are superseded:

- Rip, Verify, and Convert do not share an identical encoder set or input surface.
- WPF is implemented; its information architecture is no longer only a proposal.
- Cache defeat and adaptive read-speed control are implemented.
- Overread and dual-drive comparison remain future work.
- TTA is a classic-only C++/CLI plugin.
- FlaCuda is deleted.
- FLACCL is classic-only and its verify path is not ready to default on.
- The managed FFmpeg wrapper is not shipped by either primary product.
- Ogg, Opus, Musepack, TAK, and AAC output are conditional external-executable paths,
  not bundled in-process codecs.
