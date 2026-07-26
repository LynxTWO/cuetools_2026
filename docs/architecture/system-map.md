# System Map

Current-state refresh: 2026-07-26. This is an evidence-bounded map of the
first-party repository. `verified` means the implementation, project file, test
manifest, or local test result was inspected. `inferred` and `unknown` are used
where runtime or external-system evidence is incomplete.

## 1. System summary

This repository contains two product generations:

- The classic CUETools 2.2.6 Windows suite: CUETools, CUERipper, and CUEPlayer
  WinForms applications targeting .NET Framework 4.7, plus command-line tools
  and an EAC-hosted plugin targeting .NET Framework 2.0.
- CUETools 2026: the `CUETools.Wpf` x64 Windows desktop application targeting
  `net8.0-windows`, version 2026.1.0. It composes the shared Processor, Ripper,
  SCSI, codec, and native-plugin layers through dependency injection.

There is no first-party hosted service or server in this repository. Runtime
state is local: settings, logs, history, reports, calibration data, CUE sheets,
audio files, and rip/verification data. The desktop products call external
verification, metadata, artwork, streaming, and update-message endpoints.

## 2. Runtime units and reachability

| Runtime unit | Reachable behavior | Framework / architecture | Evidence and boundary |
| --- | --- | --- | --- |
| `CUETools/` | Classic conversion, verification, repair, and per-file output through `CUETools.Processor` | .NET Framework 4.7 WinForms | verified: `CUETools/CUETools.csproj`, `CUETools/frmCUETools.cs` |
| `CUERipper/` | Classic optical-disc ripping and metadata lookup through the SCSI/ripper stack | .NET Framework 4.7 WinForms | verified: `CUERipper/CUERipper.csproj`, `CUERipper/frmCUERipper.cs` |
| `CUEPlayer/` | Playback and Icecast streaming | .NET Framework 4.7 WinForms | verified: `CUEPlayer/CUEPlayer.csproj`, `CUEPlayer/Icecast.cs` |
| `CUETools.Wpf/` | Modern ripping, Test & Copy, conversion, verification/repair, reports, history, album art, drive calibration, and external-encoder approval | `net8.0-windows`, x64 WPF | verified: `CUETools.Wpf/CUETools.Wpf.csproj`, `CUETools.Wpf/App.xaml.cs`, `CUETools.Wpf/Services/` |
| `CUETools.CTDB.EACPlugin/` | CTDB verification/submission code loaded inside Exact Audio Copy | .NET Framework 2.0 | verified: `CUETools.CTDB.EACPlugin/CUETools.CTDB.EACPlugin.csproj`, `Plugin.cs`; host behavior is external |
| CLI projects | Scriptable conversion, verification, ripping, and codec entrypoints over shared libraries | mixed .NET Framework and SDK-style projects | verified: solution and project files |
| `CUETools.eac3ui/` | Blu-ray/eac3to-oriented UI over shared processing code | .NET Framework 4.7 | verified: project and main UI/process call paths |
| `CUERipper.WPF/` | Historical one-window stub, not the CUETools 2026 application | .NET Framework 4.7 | verified: project contents; deferred dead-weight decision |
| Historical FlaCuda projects | CUDA.NET-based codec projects formerly present in the repository | deleted; no current runtime reachability | verified: deleted in commit `4e1b02d` |

Shared libraries have product-specific target reach:

- `CUETools.Codecs`: `net20;net47;netstandard2.0`
- `CUETools.Codecs.Icecast`: `net20;net47;netstandard2.0`
- `CUETools.Codecs.WMA`: `net20;net47;net8.0-windows`
- `CUETools.Processor`: `net47;netstandard2.0`

Do not infer that a test or hardening change in one product generation exercises
the other. In particular, classic CUETools publishes output per file, while the
modern WPF app adds album-level and repair-level transactions.

## 3. Local state and secrets

| Store | Product reach | Current behavior |
| --- | --- | --- |
| Classic profile settings | CUETools and CUERipper | `CUETools.Processor/Settings/SettingsReader.cs` and `SettingsWriter.cs`; writes use a same-directory temporary file followed by replace/move publication |
| Classic proxy credential | CUETools and CUERipper | `ProxyCredentialStore` protects nonempty passwords with Windows DPAPI, CurrentUser scope, under `ProxyPasswordProtected`; plaintext serialization is cleared and unsupported platforms fail closed |
| Modern settings | CUETools 2026 | `%AppData%\CUETools2026\settings.txt`; `SettingsStore` protects `WpfProxyPasswordProtected`, migrates legacy plaintext, and rejects corrupt or wrong-user blobs |
| CUEPlayer Icecast credential | CUEPlayer | `IcecastCredentialStore` implements a bounded DPAPI CurrentUser blob and source-level ordering that attempts protected persistence before clearing legacy plaintext; real `ApplicationSettingsBase.Save()` persistence/migration has not been exercised |
| Modern diagnostic log | CUETools 2026 | one local file per run under `%AppData%\CUETools2026\logs`; no automatic upload path was found |
| Modern history and encoder catalog | CUETools 2026 | `%AppData%\CUETools2026\history.json` and `%AppData%\CUETools2026\encoders`; imported encoders require an explicit user selection and approval record |
| Audio, CUE, and report output | all desktop products | user-selected local storage; untrusted file content crosses parser and native-code boundaries |

DPAPI is a user-context confidentiality boundary, not a portable-secret format.
The applications deliberately do not fall back to plaintext when CurrentUser
protection is unavailable. Save failures at the classic GUI boundaries are
visible and do not silently discard a newly supplied credential.

## 4. External systems

| System | Reachable product path | Transport and current state |
| --- | --- | --- |
| AccurateRip | classic and modern verification/ripping through shared libraries | HTTPS-only hardcoded endpoints in `CUETools.AccurateRip/AccurateRip.cs` |
| CTDB | verification, repair, metadata, and submission | plain HTTP remains in `CUETools.CTDB/CUEToolsDB.cs`; the server did not offer usable TLS when checked, so closure requires external coordination |
| gnudb | freedb-compatible metadata fallback | plain HTTP text protocol in `Freedb/FreedbHelper.cs` |
| cue.tools MOTD | classic CUETools startup message | exact HTTPS endpoint `https://cue.tools/motd/motd.txt`; bounded strict UTF-8 text with finite timeouts; the former remote-image decode/cache path is gone |
| Icecast | CUEPlayer streaming source and metadata updates | HTTPS by default through `IcecastEndpointPolicy`; HTTP requires an explicit persisted `AllowInsecureHttp` choice and UI warning |
| Apple artwork search | CUETools 2026 album-art flow | HTTPS in `CUETools.Wpf/Services/AlbumArtService.cs` |
| MusicBrainz | browser links and tag field names | the legacy client source/project was deleted; direct lookup was retired. Metadata comes through CTDB proxy/freedb paths, while browser links to musicbrainz.org remain |
| External encoder sites | CUETools 2026 setup flow | the app opens a browser; it does not silently download or execute bytes. A user-picked executable is copied into the managed encoder directory, bound to approval metadata, and rehashed under a retained deny-write/delete lease at launch |
| GitHub Actions | CI and release control plane | hosted runner and installed-tool behavior are out of repo; workflow definitions are reviewed, but a first hosted run remains required |

## 5. Trust boundaries

| Boundary | Controls now present | Residual limit |
| --- | --- | --- |
| Network bytes to verification/metadata parsers | AccurateRip and MOTD use HTTPS; MOTD input is bounded text; Icecast validates one endpoint authority and defaults to TLS | CTDB and gnudb remain unauthenticated HTTP; live Icecast TLS/auth behavior is not locally observed |
| Audio/archive bytes to managed, `unsafe`, and native parsers | bounded `BitReader`; codec tests and fuzz smoke; RAR input is read through an in-memory callback rather than extracted to attacker-selected disk paths | old vendored parser provenance and exhaustive malformed-input coverage remain incomplete |
| Metadata to Windows paths | invalid characters, reserved device names, and trailing dot/space cases are cleansed and covered by tests | arbitrary path-length and filesystem-specific behavior is not exhaustively proven |
| Plugin directory to application process | packaged plugins require `CUETools.PluginManifest.v1` entries with normalized relative path, size, SHA-256, assembly identity, and architecture; managed and native bytes are rehashed at load. Native modules use verified full paths with no bare-name fallback and retained handles | this is an integrity allowlist, not publisher signing. A principal able to replace both manifest and directory can approve new bytes |
| Local-development plugin path | loose `CUETools.*.dll` enumeration is disabled unless `CUETOOLS_ALLOW_UNMANIFESTED_PLUGINS=1` is explicitly set | enabling the switch intentionally restores an unmanifested local-development trust boundary |
| Application to optical drive | SCSI command construction and device access in `Bwg.Scsi` and `CUETools.Ripper.SCSI` | physical-drive, firmware, C2, and cache behavior requires hardware evidence |
| EAC host to plugin | .NET 2.0 plugin boundary and inputs are documented | EAC process behavior, installer environment, and COM integration are external |
| CI source to release bytes | test-count/skip gates, artifact contracts, plugin manifests, native probes, provenance generation, and SBOM scripts | the full classic artifact still requires a suitable Visual Studio/devenv environment and hosted execution evidence |

## 6. Transaction and publication boundaries

Settings writes stage beside the destination and publish with replace/move
semantics. This removes the prior partial-file window, but it is not evidence of
power-loss durability or parent-directory synchronization.

The hardened path-based ALAC, WMA, WavPack, MAC, libFLAC, and applicable
external command-line writer paths stage completed output and then:

- use `File.Replace` when the destination existed at transaction start;
- use create-only `File.Move` when it did not, so a competing publisher is not
  overwritten;
- remove staging data on failure.

WMA Lossless additionally decodes the completed staged file and verifies PCM
format, sample count, and SHA-256 before publication. The real Windows codec
round trip is availability-gated and was skipped locally.

CUETools 2026 uses two higher-level boundaries:

- `AlbumOutputTransaction` reserves the destination across processes, stages in
  an owned same-volume sibling, rejects escape/reparse paths, writes a completion
  marker, and publishes the complete album directory by rename. This covers rip,
  Test & Copy, and conversion flows.
- `RepairTransaction` stages and validates repaired output, then publishes a
  unique `- repaired` sibling without mutating the source album.

These controls establish publication behavior under the tested failure and
contention cases. They do not by themselves prove crash consistency for every
filesystem, storage controller, or power-loss point.

This is not a universal codec guarantee. For example, Flake still writes the
requested path directly, and WAV/TTA do not have the same whole-output
verification oracle.

## 7. Build, test, and release evidence

Local TRX results captured 2026-07-26 discovered 388 tests: 381 passed, 0
failed, and 7 were expected skips.

| Suite | Discovered | Passed | Skipped |
| --- | ---: | ---: | ---: |
| codecs | 109 | 107 | 2 |
| parity | 22 | 18 | 4 |
| processor | 8 | 7 | 1 |
| modern ripper | 8 | 8 | 0 |
| WPF | 241 | 241 | 0 |

`eng/ci/test-suites.json` records the discovery floors and skip ceilings, and
`eng/ci/Invoke-TestSuites.ps1` fails on zero discovery, failures, count
regressions, or excess skips. The excluded legacy
`CUETools/TestRipper/TestRipper.csproj` still depends on hardcoded captures at
`Y:\Temp\dbg\960`; it is not the eight-test modern ripper suite.

The .NET 2.0 lane has an additional runtime probe outside the 388 MSTest total.
`ExceptionRelay` intentionally preserves the original exception type and object
identity with `throw exception;` on .NET 2.0, accepting a visible stack-origin
reset because `ExceptionDispatchInfo` is unavailable there. Modern targets use
`ExceptionDispatchInfo` to preserve the producer stack. The CI probe compiles
and runs this contract under .NET 2.0.

Workflow and release scripts now define legacy/modern test lanes, warning gates,
fuzz smoke, classic and WPF artifact contracts, plugin trust manifests, native
probes, provenance, and SBOM output. This is verified statically and locally
where noted; it is not a claim that the hosted workflow or full classic package
has completed on the current source state.

## 8. Remaining evidence gaps

- First successful hosted CI/release execution on the current source state.
- Full classic Visual Studio/devenv build and artifact validation on a machine
  with the required legacy tooling.
- Physical optical-drive Test & Copy and repair smoke with known media.
- Real WMA codec, Icecast TLS/auth, Mono/private `HttpWebRequest`, OpenCL/FLACCL,
  and supported hardware/runtime matrices.
- Real `ApplicationSettingsBase.Save()` persistence and legacy Icecast
  credential migration in CUEPlayer.
- CTDB server-side TLS and gnudb transport modernization.
- Complete provenance decisions for HDCD, LAME, UnRAR, TTA, and other vendored
  binaries; release signing and NuGet lockfiles.

See `docs/architecture/coverage-ledger.md` for review depth and
`docs/unknowns/` for the bounded open questions.
