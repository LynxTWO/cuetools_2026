# System Map

Current-state refresh: 2026-07-28. This is an evidence-bounded map of the
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
| `CUERipper/` | Classic optical-disc ripping and metadata lookup through the SCSI/ripper stack | SDK-style .NET Framework 4.7 WinForms | verified: project/main UI paths plus old/new managed, PE, config, localization, decoded-image, and live main-form equivalence under R91 |
| `CUEPlayer/` | Playback and Icecast streaming; solution-buildable but not collected into either primary release package | SDK-style .NET Framework 4.7 WinForms | verified: project/Icecast paths plus old/new managed, PE, config, decoded-image, and live main-form equivalence under R92 |
| `CUETools.Wpf/` | Modern ripping, Test & Copy, conversion, verification/repair, reports, history, album art, drive calibration, and external-encoder approval | `net8.0-windows`, x64 WPF | verified: `CUETools.Wpf/CUETools.Wpf.csproj`, `CUETools.Wpf/App.xaml.cs`, `CUETools.Wpf/Services/` |
| `CUETools.CTDB.EACPlugin/` | CTDB verification/submission code loaded inside Exact Audio Copy | .NET Framework 2.0 | verified: `CUETools.CTDB.EACPlugin/CUETools.CTDB.EACPlugin.csproj`, `Plugin.cs`; host behavior is external |
| CLI projects | Scriptable conversion, verification, ripping, and codec entrypoints over shared libraries | mixed .NET Framework and SDK-style projects | verified: solution and project files |
| `CUETools.eac3ui/` | Blu-ray/eac3to-oriented BluTools UI over shared processing code | SDK-style .NET Framework 4.7 WPF | verified: project and main UI/process call paths; old/new API, PE, config, image-resource, and live startup equivalence under R90 |
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
| Icecast | CUEPlayer streaming source and metadata updates | HTTPS by default through `IcecastEndpointPolicy`; HTTP requires an explicit persisted `AllowInsecureHttp` choice and UI warning. A disposable Icecast 2.5.0 instance passed source/auth rejection, metadata, listener-byte, flush/close, and teardown smoke locally; HTTPS certificate and Mono behavior remain unobserved |
| Apple artwork search | no current runtime reach | removed from CUETools 2026 artwork discovery because its Search API terms document album art as store-promotional content rather than art for file embedding |
| MusicBrainz and Cover Art Archive | CUETools 2026 release-bound artwork discovery plus metadata browser links | selected CTDB provider identity is retained; an exact release MBID goes directly to Cover Art Archive. Missing IDs use a one-request-per-second MusicBrainz disc-ID/fuzzy-TOC lookup, then exact-release or clearly labeled release-group art |
| TheAudioDB | optional CUETools 2026 artwork fallback | off by default; accepts a user-supplied API key only, protects it with purpose-separated current-user DPAPI, labels the source, applies a process-wide request gate plus bounded 429 retry, and ranks MusicBrainz release-group matches above exact artist/album text. The future default still depends on the applicable distribution tier and accepted attribution |
| External encoder sites | CUETools 2026 setup flow | the app opens a browser; it does not silently download or execute bytes. A user-picked executable is copied into the managed encoder directory, bound to approval metadata, and rehashed under a retained deny-write/delete lease at launch |
| GitHub Actions | CI and release control plane | hosted runner and installed-tool behavior are out of repo; workflow definitions are reviewed, but a first hosted run remains required |

## 5. Trust boundaries

| Boundary | Controls now present | Residual limit |
| --- | --- | --- |
| Network and local bytes to verification/metadata/artwork parsers | AccurateRip and MOTD use HTTPS; MOTD input is bounded text; Icecast validates one endpoint authority and defaults to TLS. WPF network artwork uses the configured proxy, bounded responses and redirects, provider host policy, public-host checks for metadata URLs, JPEG/PNG header and pixel limits, cancellation, and a bounded in-memory manifest cache. Local JPEG/PNG/BMP import accepts one regular file, reads it once under encoded/pixel limits, and freezes a RIOT-resized JPEG snapshot | CTDB and gnudb metadata transport remains unauthenticated HTTP; artwork UI and embedded-output proof on a real selected disc remains; Icecast HTTPS certificate/interoperability and Mono behavior are not locally observed |
| Audio/archive bytes to managed, `unsafe`, and native parsers | bounded `BitReader`; codec tests and fuzz smoke; RAR input is read through an in-memory callback rather than extracted to attacker-selected disk paths. Signed UnRAR 7.23 and a committed production-provider RAR5 fixture cover full read/backward seek; the fixture exposed and fixed a rewind/stale-EOF race | exhaustive malformed-input coverage remains incomplete |
| Metadata to Windows paths | invalid characters, reserved device names, and trailing dot/space cases are cleansed and covered by tests | arbitrary path-length and filesystem-specific behavior is not exhaustively proven |
| Plugin directory to application process | packaged plugins require `CUETools.PluginManifest.v1` entries with normalized relative path, size, SHA-256, assembly identity, and architecture; managed and native bytes are rehashed at load. Native modules use verified full paths with no bare-name fallback and retained handles | this is an integrity allowlist, not publisher signing. A principal able to replace both manifest and directory can approve new bytes |
| Local-development plugin path | loose `CUETools.*.dll` enumeration is disabled unless `CUETOOLS_ALLOW_UNMANIFESTED_PLUGINS=1` is explicitly set | enabling the switch intentionally restores an unmanifested local-development trust boundary |
| Application to optical drive | SCSI command construction and device access in `Bwg.Scsi` and `CUETools.Ripper.SCSI`; H: and K: completed real full-disc reads and simultaneous inquiry/TOC, and H: completed a full rip plus two-read Test & Copy | two drives do not establish every firmware, C2, cache, cancellation, disagreement, or damaged-media behavior; the final-source H: repeat remains pending |
| EAC host to plugin | .NET 2.0 plugin boundary and inputs are documented | EAC process behavior, installer environment, and COM integration are external |
| CI source to release bytes | test-count/skip gates, artifact contracts, plugin manifests, native probes, provenance generation, and SBOM scripts. Local classic AnyCPU/x64/Win32/TTA builds and a targeted MSI build pass | frozen 97-path classic receipts and execution on the pinned hosted VS2022 image remain |

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

The earlier local TRX snapshot captured 2026-07-26 discovered 388 tests: 381
passed, 0 failed, and 7 were expected skips. It is historical evidence, not the
current aggregate after the final implementation wave.

| Suite | Discovered | Passed | Skipped |
| --- | ---: | ---: | ---: |
| codecs | 109 | 107 | 2 |
| parity | 22 | 18 | 4 |
| processor | 8 | 7 | 1 |
| modern ripper | 8 | 8 | 0 |
| WPF | 241 | 241 | 0 |

`eng/ci/test-suites.json` records the discovery floors and skip ceilings, and
`eng/ci/Invoke-TestSuites.ps1` fails on zero discovery, failures, count
regressions, or excess skips. `CUETools/TestRipper/TestRipper.csproj` no longer
depends on private `Y:\Temp` captures or a stale copied vote algorithm. Its SDK
net47 tests call the production `SecureSectorVote` helper, are enrolled with a
three-test/zero-skip floor, and passed 3/3 locally.

The .NET 2.0 lane has an additional runtime probe outside the 388 MSTest total.
`ExceptionRelay` intentionally preserves the original exception type and object
identity with `throw exception;` on .NET 2.0, accepting a visible stack-origin
reset because `ExceptionDispatchInfo` is unavailable there. Modern targets use
`ExceptionDispatchInfo` to preserve the producer stack. The CI probe compiles
and runs this contract under .NET 2.0.

Workflow and release scripts now define legacy/modern test lanes, warning gates,
fuzz smoke, classic and WPF artifact contracts, plugin trust manifests, native
probes, provenance, and SBOM output. Local classic evidence includes AnyCPU
53/0, x64 and Win32 at 2/0 with 59 skipped configuration entries each, TTA
compiled/linked for both, and an Installer Projects 8/0 pass that produced a
929,792-byte MSI. This is not a claim that frozen 97-path receipts or the hosted
workflow have completed on the current source state.

## 8. Remaining evidence gaps

- First successful hosted CI/release execution on the current source state.
- Frozen classic 97-path artifact/receipt validation and hosted-image parity for
  the passing local AnyCPU/x64/Win32/TTA/MSI matrix.
- Final-source H: Test & Copy repeat plus deliberate optical
  cancellation/disagreement/damaged-media cases. The two-drive read, full rip,
  Test & Copy mechanism, and staged known-image CTDB repair paths have run.
- Broader runtime matrices: WMA beyond the passing local net8 round trip,
  Icecast HTTPS certificate and Mono/private `HttpWebRequest`, and OpenCL beyond
  the passing RTX 3060 modes 0-8 evidence.
- Real `ApplicationSettingsBase.Save()` persistence and legacy Icecast
  credential migration in CUEPlayer.
- CTDB server-side TLS and gnudb transport modernization.
- Continue the source-built HDCD and LAME replacement projects only through
  their recorded behavior/ABI/corpus gates. Their unrecoverable historical build
  details and the absent contemporaneous TTA checksum are disclosed retain
  decisions, not claims that more local searching can close them. UnRAR 7.23
  origin/signature/ABI/runtime evidence and first-party NuGet lock coverage are
  closed. Release signing and other mirrored-asset provenance remain.

See `docs/architecture/coverage-ledger.md` for review depth and
`docs/unknowns/` for the bounded open questions.
