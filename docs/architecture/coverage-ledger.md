# Coverage Ledger

Current-state refresh: 2026-07-27. This ledger records review depth, not a claim
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
| S2 Ripping and SCSI | `CUETools.Ripper*/`, `Bwg.*/` | high | commented, tested | legacy-unclear / hardware boundary | verified: C2 accumulation/voting invariants reviewed; modern ripper suite 22/22 passed; payload medium errors enter the existing untrusted-sector recovery path while tested device/transport failures remain fatal; transition retry, rejected-batch decomposition, medium-parent pinpoint recovery, rejected-parent pinpoint recovery, and cache-defeat retry/decomposition are separate bounded classifier routes for observed `IllegalRequest 24/00` states; exact parent and child sense/context is preserved and rejected payload bytes are never consumed; SCSI builds pass for net8/net47/net20; H: and K: completed full-disc reads and simultaneous inquiry/TOC; H: completed a full rip and Test & Copy plus a final-source Paranoid cache-defeat window; a later K: first-Test run recovered damaged windows for 1,227 seconds before all three normal 16-sector cache regions rejected with exact 24/00 | run the new 8/4/2/1-sector cache-defeat fallback at K:'s damaged window plus full Test & Copy; broader drive, firmware, C2, cancellation, disagreement, and media-error matrices remain |
| S3 Processor engine | `CUETools.Processor/` | high | reviewed, commented, tested | owned-risky | verified: path cleansing, DPAPI proxy persistence, atomic settings publication, manifest-bound plugin loading, and local database publication paths reviewed; processor suite 7 passed / 1 expected skipped | `CUESheet.cs` remains a large orchestration surface; classic album output is per-file rather than album-transactional |
| S4 Archive handling | `CUETools.Compression*/` and bundled archive libraries | high | reviewed, tested | owned-risky / third-party mirror | verified: RAR input uses `Unrar.Test()` plus an in-memory callback and does not extract attacker-selected paths to disk; modern targets use SharpZipLib 1.4.2; official signed UnRAR 7.23 x86/x64 exposes the required ABI; a committed RAR5 production-provider test exposed and fixed backward-seek replay against stale EOF, then passed 20/20 repeats; prior 6.11 import evidence is retained | broad malformed-archive coverage remains incomplete |
| S5 Parity and repair | `CUETools.Parity/`, `CUETools.CDRepair/`, `CUETools.AccurateRip/CDRepair.cs` | high | commented, tested | owned-risky | verified: live repair CRC gate reviewed; a deliberately damaged known image passed staged CTDB repair, independent post-verification, and unchanged-source checks; passed and accepted Test & Copy results now carry contained repair-source identity into the same post-rip transaction | physical damaged-media Test & Copy repair and adversarial/external server behavior remain outside local evidence |
| S6 Managed codecs | managed paths under `CUETools.Codecs*/` | high | reviewed, commented, tested | owned-risky | verified: shared `BitReader` is now bounded; selected path-based staged publication and lossless verification contracts were reviewed; a real net8 WMA Lossless encode/finalize/independent-decode/PCM comparison passed | one Windows Media installation is not a full host matrix; exhaustive per-codec malformed-input work remains |
| S7 Native and mixed-mode codec wrappers | `CUETools.Codecs.lib*/`, `CUETools.Codecs.MACLib/`, `.ffmpeg/`, `.lame_enc/`, `CUETools.Codecs.TTA/`, `ttalib-1.1/` | high | reviewed, commented, tested | owned-risky / cross-repo-boundary | verified: libFLAC/WavPack/HDCD/LAME partial-init ownership and callback lifetimes, selected staged output ownership, 10 focused lifetime tests, and release native probes | remaining wrappers, TTA provenance, and the full live native matrix are incomplete |
| S8 GPU codecs and parity | `CUETools.Codecs.FLACCL/`, `CUETools.FLACCL.cmd/`, `CUETools.CLParity/` | medium | mapped, tested | legacy-unclear | verified: FLACCL/CLParity are optional current paths; historical FlaCuda projects were deleted; corrected FLACCL verification passed on an RTX 3060 across modes 0-8, two CPU workers, 24-bit input, and the exact-frame boundary | cross-vendor/device/driver OpenCL behavior remains unknown |
| S9 Classic GUI applications | `CUETools/`, `CUERipper/`, `CUEPlayer/`, `CUETools.eac3ui/`, controls | medium | reviewed, commented, tested | owned-risky / legacy-unclear | verified: MOTD is bounded HTTPS text with no image cache; source handlers surface proxy/Icecast save failures; CUEPlayer/eac3ui entrypoints are inventoried | real CUEPlayer settings persistence/migration plus full interactive GUI, accessibility, and localization behavior are not covered |
| S10 CLI tools | converter, ARCUE, codec, ripper, eac3to, and CTDB command projects | low | reviewed | owned-clear | verified: representative entrypoints are thin adapters over shared libraries and do not bypass the reviewed publication contracts | command-line compatibility matrix is not exhaustive |
| S11 DSP and audio output | DSP, CoreAudio, DirectSound, Icecast | medium | reviewed, commented, tested | legacy-unclear / external-system | verified: Icecast validates endpoint components, defaults to HTTPS, requires explicit insecure-HTTP opt-in, DPAPI-protects CUEPlayer credentials, and disposes rejected responses; disposable Icecast 2.5.0 passed source/auth rejection, metadata, listener-byte, flush/close, and teardown smoke | Icecast HTTPS certificate and Mono request-hook behavior plus broad device-output matrices remain unknown |
| S12 EAC plugin and installer | `CUETools.CTDB.EACPlugin*/` | high | mapped, commented, tested | owned-risky / approval-gated / external-host | verified: net20 host boundary documented; exception relay has a .NET 2.0 runtime probe; the targeted Installer Projects build passed 8/0 and produced a 929,792-byte MSI | EAC COM-host/runtime behavior remains external; hosted installer parity remains |
| S13 Build, CI, and release | `.github/workflows/`, `eng/ci/`, `eng/release/`, solution/project files | high | reviewed, tested | owned-risky / external-control-plane | verified: suite count/skip gates, warning gates, fuzz smoke, artifact contracts, plugin manifests, native probes, provenance, and SBOM steps exist; local classic AnyCPU is 53/0, x64 and Win32 are 2/0 with 59 skipped configuration entries each, TTA builds both, and Installer Projects is 8/0 with an MSI | first current hosted run and frozen 97-path classic artifact receipts remain open |
| S14 Automated tests | legacy MSTest projects, `CUETools.Ripper.Tests/`, `CUETools.Wpf.Tests/` | medium | tested | owned-clear | verified: canonical discovery/skip floors exist; TestRipper is SDK net47, calls the production secure-vote helper, and passed its 3-test/zero-skip contract; the prior 388/381/7 aggregate is retained only as historical evidence | final canonical aggregate, hosted execution, availability-gated cases, and declared skips remain bounded gaps |
| S15 CUETools 2026 WPF runtime | `CUETools.Wpf/`, `CUETools.Wpf.Tests/`, `CUETools.Fuzz/` | high | mapped, reviewed, tested | owned-risky / Windows-hardware boundary | verified: 367/367 WPF tests pass; Rip layout contracts require viewport-bound grids, bounded rails, wrapped actions, vertical rail scrolling, and tooltip-backed trimming; Deep recovery is a durable default-on expert setting rather than a per-rip-looking control; process-per-drive windows use cross-process letter and physical-device leases, independent Stop/status state, non-sensitive launch arguments, unique logs, and serialized primary-only settings publication; the active selector launched an isolated K: job while H: completed Test & Copy without retargeting H:'s state; same-thread nesting, competing-thread denial, other-process denial/release, launch parsing, XAML exposure, and settings-writer serialization pass deterministic tests; a failed confirming read now holds a completed Copy instead of deleting it; portable human-facing album sidecars preserve legacy repair discovery while machine markers stay stable; composition, settings/logging, album/repair transactions, external-encoder approval, load-time native trust, artifact contract, real WMA, H:/K: optical reads, H: full rip/Test & Copy, and staged CTDB repair evidence also pass their bounded checks | prove same-drive denial, independent Stop, and crash release in the published build; capture final published output names and the source at 1784, 1200, and 1024 pixels; deliberate hardware/filesystem crash cases and signed/hosted release evidence remain open |
| ThirdParty submodules | `ThirdParty/{flac,taglib-sharp,openclnet,WavPack,WindowsMediaLib}` | medium | excluded from line review; pins inventoried | cross-repo-boundary / vendored | verified: pinned revisions and local patch files are recorded | upstream CVE and local-diff maintenance remain owner work |
| ThirdParty binaries | vendored managed/native DLLs and SDK assets | high | excluded from source review; artifact membership audited | binary / asset-heavy | verified: current release contracts enumerate expected shipped files/hashes; import evidence traces retained LAME, UnRAR, and TTA history | HDCD's exact origin/build, residual TTA checksum history, other mirrored assets, and publisher signing remain incomplete |
| Generated code | serializers, `*.Designer.cs`, typed DataSet output | low | excluded | generated | verified by project/file inspection | regenerate from authoritative inputs when changed |
| `CUERipper.WPF/` | historical WPF stub | low | deferred | owned-clear / unreachable from current product path | verified: distinct from `CUETools.Wpf` | revisit removal after release stabilization |

## Test evidence and exclusions

The following local aggregate is the historical pre-final snapshot. Current totals
are refreshed only by the final canonical gate:

| Suite | Discovered | Passed | Skipped | Reach |
| --- | ---: | ---: | ---: | --- |
| codecs | 109 | 107 | 2 | shared codec behavior and native-wrapper lifetimes; one platform codec test and one historical test are availability/intent skips |
| parity | 22 | 18 | 4 | Reed-Solomon and parity behavior; four deliberate long/speed skips |
| processor | 8 | 7 | 1 | processor fixtures now run; CTDB response test remains tied to a missing external fixture |
| modern ripper | 8 | 8 | 0 | `CUETools.Ripper.Tests`, not physical-drive integration |
| WPF | 241 | 241 | 0 | modern services, trust, transactions, diagnostics, and source/contract tests |

Historical total: 388 discovered, 381 passed, 0 failed, 7 expected skipped.

The net20 exception relay probe is an additional compiled runtime gate and is not
part of that historical MSTest total. `CUETools/TestRipper/TestRipper.csproj` is
now SDK net47, uses deterministic in-memory data against the shipping
`SecureSectorVote` helper, and passed its enrolled 3-test/zero-skip contract.

## Honest coverage boundary

The repository is mapped at slice level, and the named security/publication
changes have implementation and automated-test evidence. That does not establish
full behavioral coverage. In particular:

- hosted CI/release execution is not yet observed on the current source state;
- frozen 97-path classic receipts and hosted parity remain despite passing local
  AnyCPU/x64/Win32/TTA/MSI evidence;
- the final-source H: repeat and broader optical-drive, WMA-host, Icecast
  HTTPS/certificate/Mono, and cross-vendor OpenCL matrices remain;
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
- 2026-07-26: refreshed the ledger with live WMA, Icecast 2.5.0, RTX 3060,
  H:/K: optical/Test & Copy, CTDB repair, TestRipper 3/3, and local classic/MSI
  receipts; the older aggregate is explicitly historical pending the final gate.
- 2026-07-27: recorded the 18/18 damaged-media/control-transition/nested-payload policy suite, all
  three SCSI target builds, the 352/352 WPF suite, and the responsive Rip layout
  contract and 1200-pixel loaded-disc evidence. The damaged K: rerun and final
  presentation matrix remain bounded external checks.
- 2026-07-27: split the rejected-parent pinpoint ancestry from the medium-parent
  route, recorded the 20/20 ripper and 358/358 WPF suites, and added deterministic
  process-per-drive authority, settings serialization, and collision-safe logging
  evidence. Real simultaneous H:/K: jobs and the damaged K: rerun remain pending.
- 2026-07-27: recorded the held confirmation-failure transaction, active-selector
  secondary-drive launch, Test & Copy CTDB repair handoff, 21/21 ripper tests,
  359/359 WPF tests, and the final-source H: cache-defeat window. K: currently has
  no loaded media, so its exact rerun remains pending.
- 2026-07-27: recorded simultaneous H:/K: jobs, K:'s 1,227-second late
  cache-defeat 24/00 evidence, the bounded chunk-decomposition response, portable
  album sidecars with legacy discovery, 22/22 ripper tests, and 367/367 WPF tests.
