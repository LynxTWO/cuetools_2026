# Repo Slices

Current-state refresh: 2026-07-26. These slices partition first-party code by
runtime reachability and trust boundary. They are planning units, not claims of
line-by-line review. The matching evidence/status table is
`docs/architecture/coverage-ledger.md`.

## Product-to-slice reachability

| Product | Directly relevant slices |
| --- | --- |
| Classic CUETools 2.2.6 | S1, S3-S11, S13-S14 |
| Classic CUERipper 2.2.6 | S1-S3, S5-S11, S13-S14 |
| Classic CUEPlayer 2.2.6 | S3, S6-S7, S9, S11, S13-S14 |
| CUETools 2026 WPF | S1-S8, S11, S13-S15 |
| EAC plugin | S1, S3, S5, S12-S14 |
| Command-line tools | their shared-library slices plus S10 and S13-S14 |

The same library can reach multiple targets. A result for `net8.0-windows` does
not automatically cover .NET Framework 4.7 or .NET Framework 2.0.

## Slice entries

### S1: Network verification and metadata clients

- **Scope:** `CUETools.AccurateRip/`, `CUETools.CTDB/`,
  `CUETools.CTDB.Types/`, `Freedb/`.
- **Why it matters:** network bytes influence verification, repair, submission,
  and metadata decisions.
- **Reachability:** classic and modern products call AccurateRip, CTDB, and
  gnudb through shared libraries. The legacy MusicBrainz client was deleted;
  product code retains browser links and tag names.
- **Verified controls:** AccurateRip is HTTPS; request/response invariants and
  the repair CRC gate are documented.
- **Residual boundary:** CTDB and gnudb remain HTTP; server/runtime behavior is
  external.
- **Risk / status:** high; reviewed and commented.
- **Next evidence:** server-side TLS/interoperability and adversarial-response
  integration.

### S2: Ripping and SCSI stack

- **Scope:** `CUETools.Ripper/`, `CUETools.Ripper.SCSI/`,
  `CUETools.Ripper.Console/`, `CUETools.Ripper.Tests/`, `Bwg.Scsi/`,
  `Bwg.Hardware/`, `Bwg.Logging/`.
- **Why it matters:** raw device access, drive quirks, C2 handling, cache
  behavior, and voting determine rip correctness.
- **Reachability:** CUERipper, ConsoleRipper, and CUETools 2026 ripping/Test &
  Copy flows.
- **Verified controls:** C2 accumulation/voting invariants were reviewed; the
  modern ripper suite passed 8/8 locally. H: and K: completed full-disc reads
  with zero read errors and simultaneous inquiry/TOC, H: completed a full
  11-track FLAC rip, and H: passed a two-read Test & Copy with matching
  full-track/AR/CTDB evidence and decoded-and-compared output assurance.
- **Residual boundary:** no test suite can establish behavior for every drive,
  firmware, media defect, or SCSI transport.
- **Risk / status:** high; commented and partially tested.
- **Next evidence:** final-source H: Test & Copy repeat plus deliberate
  cancellation, disagreement, and recoverable/unrecoverable read errors.

### S3: Processor engine, settings, and plugin discovery

- **Scope:** `CUETools.Processor/`.
- **Why it matters:** `CUESheet` coordinates conversion/verification;
  `CUEConfig` shapes paths and network settings; plugin discovery crosses a
  code-loading boundary.
- **Reachability:** every classic conversion/verification GUI and CLI, the EAC
  path through shared libraries, and CUETools 2026 through `netstandard2.0`.
- **Verified controls:** Windows-name cleansing, DPAPI CurrentUser proxy storage,
  rejected-secret state, same-directory settings staging, allowlisted
  polymorphic settings types, and `CUETools.PluginManifest.v1` verification.
  Managed/native bytes are rechecked at load; native returned-module paths are
  verified and bare-name fallback is forbidden.
- **Residual boundary:** the plugin manifest is an integrity allowlist rather
  than publisher signing. Unmanifested enumeration is intentionally reachable
  only with `CUETOOLS_ALLOW_UNMANIFESTED_PLUGINS=1`.
- **Risk / status:** high; reviewed, commented, and service-seam tested.
- **Next evidence:** continue decomposition/coverage around large `CUESheet`
  orchestration paths.

### S4: Archive handling

- **Scope:** `CUETools.Compression/`, `CUETools.Compression.Rar/`,
  `CUETools.Compression.Zip/`, and their bundled parser binaries.
- **Why it matters:** old managed/native parsers consume untrusted archives.
- **Reachability:** classic and shared processor flows that open audio from
  RAR/Zip containers.
- **Verified controls:** the RAR path calls `Unrar.Test()` and consumes
  `DataAvailable` into memory; it does not extract archive-controlled names to
  disk in the reviewed input flow. Official signed UnRAR 7.23.0 x86/x64 DLLs
  expose the required ABI and passed the production provider/stream round trip
  against RARLAB's real archive under both process architectures. A committed
  RAR5 fixture exposed a backward-seek/stale-EOF race; `Read` now waits while
  rewind is pending, and the regression passed 20/20 repeated runs.
- **Residual boundary:** broad malformed-input coverage remains incomplete.
- **Risk / status:** high; reviewed and integration-tested.
- **Next evidence:** expand fuzz/integration cases for both archive formats.

### S5: Parity and repair math

- **Scope:** `CUETools.Parity/`, `CUETools.CDRepair/`,
  `CUETools.AccurateRip/CDRepair.cs`.
- **Why it matters:** a false success can publish damaged audio.
- **Reachability:** CTDB verify/repair paths in classic and modern products.
- **Verified controls:** the reachable repair flow applies corrections to the
  current-rip CRC and rejects a nonzero residual. The parity suite discovered
  22 tests, passed 18, and deliberately skipped four long/speed tests.
- **Residual boundary:** CRC32 is a corruption check, not a server signature.
  A deliberately damaged known image passed staged repair and independent
  post-verification without changing the source; physical damaged-media and
  server-authentication/TLS behavior remain external.
- **Risk / status:** high; commented and tested at the math boundary.
- **Next evidence:** known-disc damaged-media repair and CTDB server TLS/auth.

### S6: Codec core and managed codecs

- **Scope:** `CUETools.Codecs/` and managed codec projects.
- **Why it matters:** decoders parse untrusted streams, including `unsafe`
  paths; writers define lossless publication guarantees.
- **Reachability:** all conversion products and relevant player/ripper paths.
- **Verified controls:** the shared `BitReader` now bounds input; hardened
  path-based ALAC/WMA/WavPack/MAC/libFLAC and applicable external-writer paths
  stage output before replace/create-only publication; external lossless
  encoders need an independent decoder verification contract, and managed
  imports are hash/size lease-bound at launch. Flake still
  writes the requested path directly, and WAV/TTA do not have the same
  whole-output oracle. A real Windows net8 WMA Lossless encode/finalize/
  independent-decode/PCM comparison passed.
- **Residual boundary:** one Windows Media installation does not cover every
  host/runtime, and per-codec malformed-input coverage is not exhaustive.
- **Risk / status:** high; reviewed, commented, and tested.
- **Next evidence:** repeat WMA across supported hosts and continue fuzz corpus
  growth.

### S7: Native and mixed-mode codec wrappers

- **Scope:** `CUETools.Codecs.libFLAC/`, `CUETools.Codecs.libwavpack/`,
  `CUETools.Codecs.libmp3lame/`, `CUETools.Codecs.lame_enc/`,
  `CUETools.Codecs.MACLib/`, `CUETools.Codecs.ffmpeg/`,
  `CUETools.Codecs.TTA/`, `ttalib-1.1/`.
- **Why it matters:** buffer ownership and callback lifetime cross managed/native
  memory boundaries.
- **Reachability:** packaged codec plugins selected by classic and modern
  products.
- **Verified controls:** libFLAC, WavPack, HDCD, and LAME partial-init ownership,
  callback roots, borrowed metadata, and close-before-write behavior were reviewed
  and covered by 10 focused tests; selected writers use staged publication;
  manifest-approved native modules are rehashed and full-path loaded before managed
  plugin registration; artifact contracts probe expected native files and
  architectures.
- **Residual boundary:** not every wrapper/runtime combination has a live test,
  and several binary provenance records remain incomplete.
- **Risk / status:** high; reviewed, commented, and partially tested.
- **Next evidence:** wrapper-by-wrapper ownership matrix and supported-host
  integration runs.

### S8: GPU codecs and parity

- **Scope:** `CUETools.Codecs.FLACCL/`, `CUETools.FLACCL.cmd/`. The retired
  `CUETools.CLParity/` experiment remains available in Git history through the
  R89 parent.
- **Why it matters:** OpenCL/device-specific behavior is difficult to reproduce;
  the CUDA trees can be confused with shipped functionality.
- **Reachability:** FLACCL is an optional current classic path. `CLParity` was
  retired under R89 after proving its encoder registration was commented out,
  no first-party consumer referenced it, it was absent from release collection,
  and its project pointed at a missing `OpenCLNet.dll`. Historical FlaCuda
  projects were deleted in commit `4e1b02d`.
- **Verified controls:** solution/release membership is mapped. The FLACCL
  plugin and command host are SDK-style net47 projects with explicit
  32-bit-preferred host behavior. Corrected per-frame verification passed on an
  RTX 3060 across OpenCL modes 0-8, two CPU workers, 24-bit input, and the exact
  4096-sample boundary.
- **Residual boundary:** one NVIDIA device/driver does not establish every
  OpenCL implementation, device, or performance profile.
- **Risk / status:** medium; FLACCL mapped and locally exercised, dead GPU
  ancestors retired.
- **Next evidence:** repeat the FLACCL correctness matrix on another OpenCL
  vendor.

### S9: Classic GUI applications

- **Scope:** `CUETools/`, `CUERipper/`, `CUEPlayer/`, `CUETools.eac3ui/`,
  `CUEControls/`, `ProgressODoom/`.
- **Why it matters:** GUI code owns user consent, error visibility, and the final
  handoff to network, settings, and file operations.
- **Reachability:** classic desktop products only. `CUETools.Wpf` is S15.
- **Verified controls:** the classic MOTD is exact-host bounded HTTPS text with
  no remote image decode/cache; CUETools/CUERipper surface DPAPI save failures;
  CUEPlayer source handlers warn before insecure Icecast, do not redisplay a
  stored password, and report save failures. BluTools is the first SDK-style
  classic-GUI pilot: its API, fields, methods, PE flags, generated config, and
  all 19 embedded image payloads match the baseline, and both old/new builds
  construct a live WPF window. Real `ApplicationSettingsBase`
  persistence/migration has not been exercised. CUEPlayer's entrypoint remains
  inventoried rather than runtime-smoked.
- **Residual boundary:** interactive GUI, accessibility, and localization flows
  are not comprehensively automated.
- **Risk / status:** medium; reviewed and commented.
- **Next evidence:** smoke the failure messages and persisted consent on a clean
  Windows user profile.

### S10: Command-line tools

- **Scope:** converter, ARCUE, Flake, ALACEnc, LossyWAV, ChaptersToCue,
  ConsoleRipper, eac3to, CTDB.Converter, and related executable projects.
- **Why it matters:** they are the scriptable public surface.
- **Reachability:** direct executable entrypoints over shared libraries.
- **Verified controls:** representative argument paths delegate into shared
  validation/publication behavior.
- **Residual boundary:** the full legacy command-line compatibility matrix was
  not exercised.
- **Risk / status:** low; reviewed lightly.
- **Next evidence:** targeted compatibility tests when an entrypoint changes.

### S11: DSP and audio output

- **Scope:** DSP, CoreAudio, DirectSound, and
  `CUETools.Codecs.Icecast/`.
- **Why it matters:** output-device correctness and an outbound credentialed
  streaming path meet here.
- **Reachability:** CUEPlayer and shared audio-output code.
- **Verified controls:** Icecast endpoint parsing binds source and metadata to
  one authority, defaults to HTTPS, requires explicit insecure-HTTP opt-in, and
  disposes rejected 4xx responses. CUEPlayer stores its secret with DPAPI. A
  disposable Icecast 2.5.0 instance passed source/auth rejection, metadata,
  listener-byte, flush/close, and teardown smoke.
- **Residual boundary:** HTTPS certificate/interoperability and the Mono/private
  `HttpWebRequest` hook are unobserved.
- **Risk / status:** medium; reviewed and commented.
- **Next evidence:** Icecast HTTPS/certificate and Mono cases plus
  supported-output-device smoke.

### S12: EAC plugin and installer

- **Scope:** `CUETools.CTDB.EACPlugin/`,
  `CUETools.CTDB.EACPlugin.Installer/`.
- **Why it matters:** code runs with a third-party host's privileges and retains
  a .NET Framework 2.0 compatibility contract.
- **Reachability:** Exact Audio Copy loads the plugin through COM; the installer
  is a vdproj project.
- **Verified controls:** the host boundary is documented. A separate net20
  runtime probe verifies exception type/object identity. The implementation
  intentionally accepts a visible stack-origin reset on net20 because
  `ExceptionDispatchInfo` is unavailable; modern targets preserve the producer
  stack.
- **Residual boundary:** EAC COM behavior and the installer require the real host
  and Visual Studio Installer Projects tooling.
- **Risk / status:** high; mapped, commented, and probe-tested.
- **Next evidence:** EAC-hosted smoke and installer artifact validation.

### S13: Build, CI, and release control plane

- **Scope:** `.github/workflows/`, `eng/ci/`, `eng/release/`,
  `collect_files*.bat`, `ThirdParty/*.patch`, solution and project files.
- **Why it matters:** this path decides which source, tests, plugins, native
  bytes, manifests, and evidence become a release.
- **Reachability:** every shipped product.
- **Verified controls:** legacy/modern suite manifests with discovery and skip
  gates, net20 probe, warning gates, fuzz smoke, classic/WPF artifact contracts,
  plugin manifests, native probes, provenance, and SBOM scripts.
- **Residual boundary:** static/local verification is not a completed hosted
  workflow. Local classic AnyCPU is 53/0; x64 and Win32 are each 2/0 with
  59 skipped configuration entries; TTA builds both; Installer Projects is 8/0
  and produced a 929,792-byte MSI. Frozen 97-path receipts and parity on the
  pinned hosted VS2022 image remain.
- **Risk / status:** high; reviewed and locally tested.
- **Next evidence:** first current hosted CI/release run and signed artifact
  decision.

### S14: Automated tests

- **Scope:** `CUETools/CUETools.TestCodecs/`,
  `CUETools/CUETools.TestParity/`, `CUETools/CUETools.TestProcessor/`,
  `CUETools.Ripper.Tests/`, `CUETools.Wpf.Tests/`, and
  `CUETools/TestRipper/`.
- **Why it matters:** suite counts and skip budgets are the regression contract
  for shared and modern code.
- **Reachability:** codecs, parity, Processor, modern ripper services, and WPF
  services/contracts.
- **Verified controls:** CI records minimum discovery and maximum skip counts.
  The prior 388/381/7 aggregate is historical pending the final canonical total.
  TestRipper is now SDK net47, calls the shipping `SecureSectorVote` helper,
  has no private-capture dependency, and passed its enrolled 3-test/zero-skip
  contract.
- **Residual boundary:** declared skips and availability-gated behavior remain
  visible gaps; focused fixtures do not replace hosted or hardware integration.
- **Risk / status:** medium; tested with explicit exclusions.
- **Next evidence:** retain TestRipper in the canonical selection and run
  availability-gated cases on suitable hosts.

### S15: CUETools 2026 WPF runtime

- **Scope:** `CUETools.Wpf/`, `CUETools.Wpf.Tests/`, `CUETools.Fuzz/`.
- **Why it matters:** this is a live x64 `net8.0-windows` product, not the
  historical `CUERipper.WPF` stub. It owns modern composition, transactions,
  local diagnostics, encoder approval, history, reports, artwork, and drive
  workflows.
- **Reachability:** CUETools 2026 only, with shared Processor/Ripper/codec
  dependencies.
- **Verified controls:** DPAPI settings migration, local redacting diagnostics,
  album and repair transactions, plugin trust, external-encoder approvals,
  artifact contract, and 241/241 local WPF tests.
- **Residual boundary:** optical-drive, WMA, crash/power-loss filesystem,
  real-service, and signed-release evidence remain outside local automation.
- **Risk / status:** high; mapped, reviewed, and tested at service seams.
- **Next evidence:** end-to-end known-media smoke and first validated release
  artifact.

## Exclusions

- Pinned ThirdParty submodules are cross-repository boundaries. Their revisions,
  patches, and artifact membership are tracked, but their source is not claimed
  as first-party reviewed.
- Vendored binaries and SDK assets are inventoried/hashed where shipped, not
  source-audited. Provenance gaps stay open.
- Generated serializers, designers, and typed DataSet output are excluded from
  inline comment work.
- `CUERipper.WPF/` remains an explicit deferred/dead-weight decision and should
  not be confused with the live `CUETools.Wpf` runtime. Historical FlaCuda
  projects are deleted, not excluded current scope.

## Prioritized remaining passes

1. Hosted CI/release and frozen classic artifact receipts (S13).
2. Final-source optical repeat and deliberate drive/repair failure cases (S2,
   S5, S15).
3. Cross-host WMA, Icecast HTTPS/certificate/Mono, and cross-vendor OpenCL
   matrices (S6, S8, S11).
4. Third-party binary provenance, signing, and dependency locking (S4, S7,
   S13).
5. Decide the deferred `CUERipper.WPF` stub's future after owner review (S9,
   S14).
