# Coverage Ledger

Current-state refresh: 2026-07-26. This ledger records review depth, not a claim
that every line or runtime combination has been exercised. Slice definitions
live in `docs/architecture/repo-slices.md`.

Status meanings used here:

- `mapped`: entrypoints, dependencies, and trust boundaries identified.
- `reviewed`: relevant implementation read against the stated concern.
- `commented`: load-bearing invariants also documented in source.
- `tested`: current automated evidence exists for the bounded behavior.
- `deferred` or `excluded`: intentionally outside the current code-level pass.

Evidence is labeled `verified`, `inferred`, or `unknown`.

## Ledger

| Slice | Scope | Risk | Status | Classification | Current evidence | Remaining evidence / next check |
| --- | --- | --- | --- | --- | --- | --- |
| S1 Network verification and metadata | `CUETools.AccurateRip/`, `CUETools.CTDB*/`, `Freedb/` | high | reviewed, commented | owned-risky / external-system | verified: AccurateRip is HTTPS; CTDB request/repair/submit and gnudb paths are mapped; the legacy MusicBrainz client was deleted and direct lookup retired | CTDB and gnudb still use HTTP; external server changes and live interoperability remain open |
| S2 Ripping and SCSI | `CUETools.Ripper*/`, `Bwg.*/` | high | commented, tested | legacy-unclear / hardware boundary | verified: C2 accumulation/voting invariants reviewed; modern ripper suite 8/8 passed; earlier Windows drive smoke reached the SCSI stack | physical-drive, cache, firmware, C2, and media-error matrices are unknown |
| S3 Processor engine | `CUETools.Processor/` | high | reviewed, commented, tested | owned-risky | verified: path cleansing, DPAPI proxy persistence, atomic settings publication, manifest-bound plugin loading, and local database publication paths reviewed; processor suite 7 passed / 1 expected skipped | `CUESheet.cs` remains a large orchestration surface; classic album output is per-file rather than album-transactional |
| S4 Archive handling | `CUETools.Compression*/` and bundled archive libraries | high | reviewed | owned-risky / third-party mirror | verified: RAR input uses `Unrar.Test()` plus an in-memory callback and does not extract attacker-selected paths to disk in the reachable input flow | old SharpZipLib/UnRAR provenance and broad malformed-archive coverage remain incomplete |
| S5 Parity and repair | `CUETools.Parity/`, `CUETools.CDRepair/`, `CUETools.AccurateRip/CDRepair.cs` | high | commented, tested | owned-risky | verified: live repair CRC gate reviewed; parity suite discovered 22, passed 18, skipped 4 expected long/speed tests | physical repair smoke and adversarial server behavior remain outside local evidence |
| S6 Managed codecs | managed paths under `CUETools.Codecs*/` | high | reviewed, commented, tested | owned-risky | verified: shared `BitReader` is now bounded; selected path-based staged publication and lossless verification contracts were reviewed; codec suite discovered 109, passed 107, skipped 2 | real WMA availability test skipped; exhaustive per-codec malformed-input work remains |
| S7 Native and mixed-mode codec wrappers | `CUETools.Codecs.lib*/`, `CUETools.Codecs.MACLib/`, `.ffmpeg/`, `.lame_enc/`, `CUETools.Codecs.TTA/`, `ttalib-1.1/` | high | reviewed, commented, tested | owned-risky / cross-repo-boundary | verified: libFLAC/WavPack/HDCD/LAME partial-init ownership and callback lifetimes, selected staged output ownership, 10 focused lifetime tests, and release native probes | remaining wrappers, TTA provenance, and the full live native matrix are incomplete |
| S8 GPU codecs and parity | `CUETools.Codecs.FLACCL/`, `CUETools.FLACCL.cmd/`, `CUETools.CLParity/` | medium | mapped | legacy-unclear | verified: FLACCL/CLParity are optional current paths; historical FlaCuda projects were deleted in commit `4e1b02d` | OpenCL device/runtime coverage is unknown |
| S9 Classic GUI applications | `CUETools/`, `CUERipper/`, `CUEPlayer/`, `CUETools.eac3ui/`, controls | medium | reviewed, commented, tested | owned-risky / legacy-unclear | verified: MOTD is bounded HTTPS text with no image cache; source handlers surface proxy/Icecast save failures; CUEPlayer/eac3ui entrypoints are inventoried | real CUEPlayer settings persistence/migration plus full interactive GUI, accessibility, and localization behavior are not covered |
| S10 CLI tools | converter, ARCUE, codec, ripper, eac3to, and CTDB command projects | low | reviewed | owned-clear | verified: representative entrypoints are thin adapters over shared libraries and do not bypass the reviewed publication contracts | command-line compatibility matrix is not exhaustive |
| S11 DSP and audio output | DSP, CoreAudio, DirectSound, Icecast | medium | reviewed, commented | legacy-unclear / external-system | verified: Icecast validates endpoint components, defaults to HTTPS, requires explicit insecure-HTTP opt-in, DPAPI-protects CUEPlayer credentials, and disposes rejected responses | real Icecast TLS/auth, Mono request-hook behavior, and broad device-output matrices are unknown |
| S12 EAC plugin and installer | `CUETools.CTDB.EACPlugin*/` | high | mapped, commented, tested | owned-risky / approval-gated / external-host | verified: net20 host boundary documented; exception relay type/identity contract has a separate .NET 2.0 runtime probe | EAC COM-host and vdproj installer runs require external tooling and host evidence |
| S13 Build, CI, and release | `.github/workflows/`, `eng/ci/`, `eng/release/`, solution/project files | high | reviewed, tested | owned-risky / external-control-plane | verified: suite count/skip gates, warning gates, fuzz smoke, artifact contracts, plugin manifests, native probes, provenance, and SBOM steps exist | first current hosted run and full classic devenv artifact validation remain open |
| S14 Automated tests | legacy MSTest projects, `CUETools.Ripper.Tests/`, `CUETools.Wpf.Tests/` | medium | tested | owned-clear | verified local TRX, 2026-07-26: 388 discovered, 381 passed, 0 failed, 7 expected skipped | availability-gated and deliberately excluded cases are not coverage; see test note below |
| S15 CUETools 2026 WPF runtime | `CUETools.Wpf/`, `CUETools.Wpf.Tests/`, `CUETools.Fuzz/` | high | mapped, reviewed, tested | owned-risky / Windows-hardware boundary | verified: composition, settings/logging, album/repair transactions, external-encoder approval, load-time native trust, artifact contract, and 241/241 WPF tests | end-to-end optical-drive, WMA, filesystem crash, and signed-release evidence remain open |
| ThirdParty submodules | `ThirdParty/{flac,taglib-sharp,openclnet,WavPack,WindowsMediaLib}` | medium | excluded from line review; pins inventoried | cross-repo-boundary / vendored | verified: pinned revisions and local patch files are recorded | upstream CVE and local-diff maintenance remain owner work |
| ThirdParty binaries | vendored managed/native DLLs and SDK assets | high | excluded from source review; artifact membership audited | binary / asset-heavy | verified: current release contracts enumerate expected shipped files and hashes | provenance gaps remain for HDCD, LAME, UnRAR, TTA, and other binaries |
| Generated code | serializers, `*.Designer.cs`, typed DataSet output | low | excluded | generated | verified by project/file inspection | regenerate from authoritative inputs when changed |
| `CUERipper.WPF/` | historical WPF stub | low | deferred | owned-clear / unreachable from current product path | verified: distinct from `CUETools.Wpf` | revisit removal after release stabilization |

## Test evidence and exclusions

The local aggregate is:

| Suite | Discovered | Passed | Skipped | Reach |
| --- | ---: | ---: | ---: | --- |
| codecs | 109 | 107 | 2 | shared codec behavior and native-wrapper lifetimes; one platform codec test and one historical test are availability/intent skips |
| parity | 22 | 18 | 4 | Reed-Solomon and parity behavior; four deliberate long/speed skips |
| processor | 8 | 7 | 1 | processor fixtures now run; CTDB response test remains tied to a missing external fixture |
| modern ripper | 8 | 8 | 0 | `CUETools.Ripper.Tests`, not physical-drive integration |
| WPF | 241 | 241 | 0 | modern services, trust, transactions, diagnostics, and source/contract tests |

Total: 388 discovered, 381 passed, 0 failed, 7 expected skipped.

The net20 exception relay probe is an additional compiled runtime gate and is not
part of the 388 MSTest total. The old
`CUETools/TestRipper/TestRipper.csproj` remains explicitly excluded because its
only test loads 64 captures from hardcoded `Y:\Temp\dbg\960` paths through a
retired VS2010 test adapter.

## Honest coverage boundary

The repository is mapped at slice level, and the named security/publication
changes have implementation and automated-test evidence. That does not establish
full behavioral coverage. In particular:

- hosted CI/release execution is not yet observed on the current source state;
- the full classic release needs a suitable Visual Studio/devenv environment;
- optical-drive, WMA, Icecast, Mono, and OpenCL paths require external hardware
  or services;
- tests of rename/replace behavior do not prove power-loss durability on every
  filesystem;
- third-party binary provenance and signing are not complete.

## Changelog

- 2026-07-02: initial S1-S14 ledger, critical comments, logging audit, and
  remediation backlog created.
- 2026-07-26: reconciled the ledger with current products and gates; added the
  reachable CUETools 2026 WPF slice; replaced stale plaintext-proxy,
  plain-HTTP-MOTD/Icecast, unsigned-plugin, missing-fixture, and no-test-CI
  statements; recorded the 388/381/7 local test result and bounded external
  evidence gaps.
