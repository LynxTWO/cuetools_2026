# Coverage Ledger

Current-state refresh: 2026-07-31. This ledger records review depth, not a claim
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
| S2 Ripping and SCSI | `CUETools.Ripper*/`, `Bwg.*/` | high | commented, tested | legacy-unclear / hardware boundary | verified: C2 accumulation/voting invariants reviewed; modern ripper suite 32/32 passed; payload medium errors enter the existing untrusted-sector recovery path while device and transport failures keep separate identities; transition retry, rejected-batch decomposition, medium-parent pinpoint recovery, rejected-parent pinpoint recovery, cache-defeat retry/decomposition, and the exact H: model/firmware normal-read 08/0A communication retry are separate bounded classifier routes; exact parent and child sense/context is preserved and rejected payload bytes are never consumed; unassigned ASC 08 qualifier 0A retains its known communication family and raw identity; SCSI builds pass for net8/net47/net20; H: and K: completed full-disc reads and simultaneous inquiry/TOC; H: crossed all four retained 08/0A addresses and completed a source-bound 846-second Test & Copy with 11 verified files, AR 107/424, CTDB 114/544, and zero failed windows while K: completed its concurrent job; K: also completed the bounded cache fallback, a 2,275-second Test & Copy, and an independently verified six-sector CTDB repair | retain positive R105 branch counters if 08/0A recurs; broader drive, firmware, C2, cancellation, disagreement, and media-error matrices remain; the 2026-08-01 adversarial addendum records a verified DeepRecovery failed-sector sentinel defect, a mid-pass StopOnUnrecoverable stop, an unchecked SCSI residual, and orchestration branches without deterministic in-repo activation tests. Remediated 2026-08-01/02 (R106-R124) and exercised live on two damaged discs and both matrix drives: give-up is engine state, deep recovery is bounded by pass count and wall clock, low-speed and flush reads carry an extended timeout (19,500 activations carried three complete salvage reads), the drive's own 3E/02 surrender marks sectors instead of failing jobs, command autodetect sweeps three disc regions, and a stall detector plus heartbeats make any slow grind legible. Ripper suite 59/59 |
| S3 Processor engine | `CUETools.Processor/` | high | reviewed, commented, tested | owned-risky | verified: path cleansing, DPAPI proxy persistence, atomic settings publication, manifest-bound plugin loading, and local database publication paths reviewed; processor suite 7 passed / 1 expected skipped | `CUESheet.cs` remains a large orchestration surface; classic album output is per-file rather than album-transactional |
| S4 Archive handling | `CUETools.Compression*/` and bundled archive libraries | high | reviewed, tested | owned-risky / third-party mirror | verified: RAR input uses `Unrar.Test()` plus an in-memory callback and does not extract attacker-selected paths to disk; modern targets use SharpZipLib 1.4.2; official signed UnRAR 7.23 x86/x64 exposes the required ABI; a committed RAR5 production-provider test exposed and fixed backward-seek replay against stale EOF, then passed 20/20 repeats; prior 6.11 import evidence is retained | broad malformed-archive coverage remains incomplete |
| S5 Parity and repair | `CUETools.Parity/`, `CUETools.CDRepair/`, `CUETools.AccurateRip/CDRepair.cs` | high | commented, tested | owned-risky | verified: live repair CRC gate reviewed; a deliberately damaged known image passed staged CTDB repair, independent post-verification, and unchanged-source checks; passed and accepted Test & Copy results now carry contained repair-source identity into the same post-rip transaction | physical damaged-media Test & Copy repair and adversarial/external server behavior remain outside local evidence; the 2026-08-01 adversarial addendum records first-server-ordered CTDB variant selection at `RepairTransaction.cs:68` and a drifted dead `CUETools.CDRepair/` duplicate lacking the miscorrection CRC gate |
| S6 Managed codecs | managed paths under `CUETools.Codecs*/` | high | reviewed, commented, tested | owned-risky | verified: shared `BitReader` is now bounded; selected path-based staged publication and lossless verification contracts were reviewed; a real net8 WMA Lossless encode/finalize/independent-decode/PCM comparison passed | one Windows Media installation is not a full host matrix; exhaustive per-codec malformed-input work remains |
| S7 Native and mixed-mode codec wrappers | `CUETools.Codecs.lib*/`, `CUETools.Codecs.MACLib/`, `.ffmpeg/`, `.lame_enc/`, `CUETools.Codecs.TTA/`, `ttalib-1.1/` | high | reviewed, commented, tested | owned-risky / cross-repo-boundary | verified: libFLAC/WavPack/HDCD/LAME partial-init ownership and callback lifetimes, selected staged output ownership, 10 focused lifetime tests, release native probes, source-built TTA archive/import comparison, and explicit legacy HDCD/LAME retain gates. Root-loaded WPF wrappers now resolve only a conflict-rejecting manifest-approved native path; the clean production apphost finalized real FLAC, WavPack, Monkey's Audio, and LAME outputs, with lossless decode-and-compare enabled, plus HDCD initialization. Monkey's Audio finalizer contains failed type initialization. Official libhdcd v1.4 built x86/x64 but failed the six-vector behavioral replacement gate while its non-HDCD control matched exactly. The unshipped FFmpeg wrapper binds AutoGen 8.1.0 to checked FFmpeg 8 native majors, contains callback failures, owns partial state transactionally, and passed real 8.1.2 x64/x86 16/24-bit deterministic AIFF probes | FFmpeg's full format matrix and remaining wrapper/live-native combinations are incomplete |
| S8 GPU codecs and parity | `CUETools.Codecs.FLACCL/`, `CUETools.FLACCL.cmd/` | medium | mapped, tested | owned-risky / hardware boundary | verified: FLACCL is the optional current path; its SDK-style net47 plugin/command pair preserves the qualified 32-bit host and passed corrected verification on an RTX 3060 across modes 0-8, two CPU workers, 24-bit input, and the exact-frame boundary; the disabled, unconsumed, unshipped, non-building CLParity experiment was retired under R89 | cross-vendor/device/driver FLACCL behavior remains unknown |
| S9 Classic GUI applications | `CUETools/`, `CUERipper/`, `CUEPlayer/`, `CUETools.eac3ui/`, controls | medium | reviewed, commented, tested | owned-risky / legacy-unclear | verified: MOTD is bounded HTTPS text with no image cache; source handlers surface proxy/Icecast save failures; BluTools, CUERipper/ProgressODoom, CUEPlayer, and CUETools preserve their managed/PE/config/resource contracts and old/new live main-form behavior through staged SDK conversion; the classic GUI project-format boundary is closed and duplicate resource names are gated; CUEPlayer remains solution-only and uncollected | real CUEPlayer settings persistence/migration plus full interactive GUI, accessibility, and localization behavior are not covered |
| S10 CLI tools | converter, ARCUE, codec, ripper, eac3to, and CTDB command projects | low | reviewed | owned-clear | verified: representative entrypoints are thin adapters over shared libraries and do not bypass the reviewed publication contracts | command-line compatibility matrix is not exhaustive |
| S11 DSP and audio output | DSP, CoreAudio, DirectSound, Icecast | medium | reviewed, commented, tested | legacy-unclear / external-system | verified: Icecast validates endpoint components, defaults to HTTPS, requires explicit insecure-HTTP opt-in, DPAPI-protects CUEPlayer credentials, and disposes rejected responses; disposable Icecast 2.5.0 passed source/auth rejection, metadata, listener-byte, flush/close, and teardown smoke | Icecast HTTPS certificate and Mono request-hook behavior plus broad device-output matrices remain unknown |
| S12 EAC plugin and installer | `CUETools.CTDB.EACPlugin*/` | high | mapped, commented, tested | owned-risky / approval-gated / external-host | verified: net20 host boundary documented; exception relay has a .NET 2.0 runtime probe; the targeted Installer Projects build passed 8/0 and produced a 929,792-byte MSI | EAC COM-host/runtime behavior remains external; hosted installer parity remains |
| S13 Build, CI, and release | `.github/workflows/`, `eng/ci/`, `eng/release/`, solution/project files | high | reviewed, tested | owned-risky / external-control-plane | verified: suite count/skip gates, warning gates, fuzz smoke, artifact contracts, plugin manifests, native probes, provenance, and SBOM steps exist; external encoder preparation binds three executables and every required source/build input, including the four-archive deterministic Opus/libopus 1.6.1 build; local classic AnyCPU is 53/0, x64 and Win32 are 2/0 with 59 skipped configuration entries each, TTA builds both, and Installer Projects is 8/0 with an MSI. Source-bound hosted classic/WPF CI and an unsigned release evidence run pass with zero annotations. The downloaded release passed classic 97-file and WPF 557-file hash/SPDX closures, populated CycloneDX graphs, clean provenance with five clean submodules, and the exact 423-member native SDK expansion. A 117-file Authenticode/RFC 3161 policy fails closed for tag releases, regenerates plugin manifests, and places provenance/SBOM generation after final signed bytes | refresh hosted evidence after control-plane changes; public-trust signing identity provisioning is external |
| S14 Automated tests | legacy MSTest projects, `CUETools.Ripper.Tests/`, `CUETools.Wpf.Tests/` | medium | tested | owned-clear | verified: canonical discovery/skip floors exist. The 2026-07-31 local canonical gate discovered 647 tests, passed 641, failed zero, and retained six declared skips: classic 156/162 and modern 485/485. TestRipper is SDK net47 and calls the production secure-vote helper; the prior 388/381/7 aggregate is retained only as historical evidence | availability-gated cases, hardware integrations, and declared skips remain bounded gaps |
| S15 CUETools 2026 WPF runtime | `CUETools.Wpf/`, `CUETools.Wpf.Tests/`, `CUETools.Fuzz/` | high | mapped, reviewed, tested | owned-risky / Windows-hardware boundary | verified: 750 WPF tests pass plus one gated on-screen capture that stays Inconclusive without its output folder; Rip layout contracts require viewport-bound grids, bounded rails, wrapped actions, vertical rail scrolling, and tooltip-backed trimming; the three-tier clipping policy (rail never clips, page content reflows to 860, held layout scrolls below) is pinned by `PageScrollPolicyTests`, `RailColumnWidthTests`, and `QueueColumnLayoutTests`, and the Stryker mutation lane gates pull requests since 2026-08-26; Deep recovery is a durable default-on expert setting rather than a per-rip-looking control; process-per-drive windows use cross-process letter and physical-device leases, independent Stop/status state, non-sensitive launch arguments, unique logs, and serialized primary-only settings publication. Rip, Convert, and Queue share a grouped codec picker that names format, extension, implementation, origin, readiness, history, best use, and distribution terms; unavailable implementations remain visible but cannot be selected. Queued jobs carry an exact stable implementation id. Encoded optical jobs preflight codec health before drive ownership and freeze one implementation for the complete operation. Damaged agreement is distinguished from clean verification; repair publication seals source/output SHA-256 proofs, named reports, `repair.verify`, and a final completion marker; the live repair matched AccurateRip and CTDB with source hashes unchanged. Artwork retains source-specific release identity, uses bounded proxy-aware CTDB/Cover Art Archive discovery with MusicBrainz disc-ID/fuzzy-TOC and release-group fallback, ranks release identity before quality, exposes a sortable Front/All chooser with attribution links, freezes selected bytes per job, and prevents a hidden processor fallback. One-read local JPEG/PNG/BMP import applies encoded/pixel bounds and RIOT JPEG conversion. Non-front art is browser-only. TheAudioDB is an off-by-default, source-labeled fallback with a purpose-separated DPAPI user API key, release-group/text validation, rate gate, bounded 429 retry, and provider host policy. Apple artwork has no runtime reach. Parser/rank/import/secret/limit/redirect/layout tests pass, live CAA, MusicBrainz, and TheAudioDB endpoints returned HTTP 200, dark/light and constrained-window captures pass, automatic and local-override embedded art are byte-proven, the WPF/fuzz warning gate remains empty, and the clean self-contained x64 apphost passes five production-layout native probes | the selector windows are captured at 100 to 200 percent DPI (2026-08-26) and under Windows high contrast (2026-08-27); the app palette ignores the system scheme and the codec picker's selection loses contrast in the dark theme, which is decision D14; TheAudioDB must remain off by default pending its distribution tier and accepted attribution. Existing hardware, crash, and production-signing matrices also remain open. The 2026-08-01 adversarial addendum is closed: R109 made `RipReport.Verified` exclude damaged results so report, history, and certificate all say consistent, and R108 parks a held Copy keyed to its disc on Stop-during-confirm and tray events instead of deleting it (both fixed 2026-08-01; this column lagged until 2026-08-26) |
| ThirdParty submodules | `ThirdParty/{flac,taglib-sharp,openclnet,WavPack,WindowsMediaLib}` | medium | excluded from line review; pins inventoried | cross-repo-boundary / vendored | verified: pinned revisions and local patch files are recorded | upstream CVE and local-diff maintenance remain owner work |
| ThirdParty binaries | vendored managed/native DLLs and SDK assets | high | excluded from source review; artifact membership audited | binary / asset-heavy | verified: current release contracts enumerate expected shipped files/hashes; import evidence traces retained LAME, UnRAR, HDCD, and TTA history; irrecoverable HDCD/LAME build details and the absent 2009 TTA checksum are disclosed with retain/replacement gates; the Musepack and current-libopus binaries are byte-reproducible from packaged source/patch/build materials, while Musepack's ambiguous tag writer is excluded. Publisher signing explicitly excludes hash-pinned upstream and Microsoft runtime files | other mirrored-asset provenance and the external public-trust signing identity remain incomplete |
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

Current hosted total at commit
`33c8eea3b8d085d5bb2473a7f6451dce2ba4e294`: 630 discovered, 623
passed, zero failed, and seven declared skips across the classic and modern
workflow selections.

Current local canonical total on 2026-07-31: 647 discovered, 641 passed,
zero failed, and six declared skips. The modern WPF and ripper suites passed
485/485; the four classic suites passed 156/162 with their six bounded skips.

The net20 exception relay probe is an additional compiled runtime gate and is not
part of that historical MSTest total. `CUETools/TestRipper/TestRipper.csproj` is
now SDK net47, uses deterministic in-memory data against the shipping
`SecureSectorVote` helper, and passed its enrolled 3-test/zero-skip contract.

## Honest coverage boundary

The repository is mapped at slice level, and the named security/publication
changes have implementation and automated-test evidence. That does not establish
full behavioral coverage. In particular:

- hosted evidence must be refreshed after material runner-image, action,
  Visual Studio, or SDK changes;
- broader optical-drive, WMA-host, Icecast HTTPS/certificate/Mono, and
  cross-vendor OpenCL matrices remain;
- tests of rename/replace behavior do not prove power-loss durability on every
  filesystem;
- third-party binary provenance and public-trust signing identity provisioning
  are not complete; repository signing policy and unsigned-tag refusal are.

## Changelog

- 2026-07-02: initial S1-S14 ledger, critical comments, logging audit, and
  remediation backlog created.
- 2026-07-26: reconciled the ledger with current products and gates; added the
  reachable CUETools 2026 WPF slice; replaced stale plaintext-proxy,
  plain-HTTP-MOTD/Icecast, unsigned-plugin, missing-fixture, and no-test-CI
  statements; recorded the 388/381/7 local test result and bounded external
  evidence gaps.
- 2026-07-30: recorded source-bound hosted classic/WPF/FFmpeg/release success,
  zero final annotations, the 630-test hosted aggregate, and independent
  downloaded-artifact provenance/SBOM/hash validation.
- 2026-07-31: recorded the production-apphost native binding and real encode
  probes, grouped implementation-aware codec picker, pre-drive codec gate, exact
  queued implementation identity, 453/453 WPF result, 637/643 canonical local
  aggregate, clean self-contained WPF publication, and zero warning budget.
- 2026-07-31: added the exact H: 08/0A normal-read communication retry, raw
  unassigned-qualifier diagnostics, 32/32 ripper result, 641/647 canonical local
  aggregate, zero warning budget, release-safety pass, validated R105 artifact,
  four live address probes, and a full concurrent H:/K: Test & Copy result.
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
