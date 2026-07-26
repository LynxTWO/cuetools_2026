# Autonomous Codebase Audit - 2026-07-26

## Verdict

All verified findings that could be fixed and exercised safely on this machine were
remediated. The modern WPF product has a green local Release gate, a validated clean
artifact, a hash-bound plugin set, whole-album transactional publication, protected
settings, and explicit lossless-output verification contracts.

That is not the same as declaring the entire repository release-ready. The broad
classic WinForms/C++/CLI distribution still needs its full Visual Studio hosted
build and artifact validation. Real optical-drive, CTDB repair, Windows Media,
Icecast, OpenCL/FLACCL, and some external-command integrations were unavailable
locally. They remain named evidence gaps below rather than inferred passes.

The earlier codec handoff was directionally useful, with two corrections preserved
as current facts:

- there are thirteen unreferenced codec projects in the historical inventory, and
  TTA is the different one: a C++/CLI wrapper over vendored `ttalib`, not C#;
- codec reachability is product-specific. The classic package carries a broad plugin
  set; WPF ships a curated, manifest-bound subset. "In the repo" does not imply "in
  both products."

## Audit scope and method

The audit used the repository-local
[`anti-dark-code`](../../.claude/skills/anti-dark-code/SKILL.md) workflow and the
skill-creator validation rules. It traced:

- classic WinForms, WPF, command-line, player, EAC/COM, plugin, fuzz, CI, and release
  entry points;
- optical read, Test & Copy, encode, verify, repair, settings, network, plugin-load,
  process-launch, file-publication, and artifact-production boundaries;
- managed and native codec registration, runtime dependencies, verification claims,
  and package reachability;
- first-party tests, warning budgets, fuzz invariants, workflow syntax, release
  contracts, native inventory, hashes, and SBOM output.

The bounded first-party source census found 623 candidate C# files and 68 C# project
files after excluding `ThirdParty`, `bin`, `obj`, designer/generated files, and the
mirrored MusicBrainz tree. Counts in this document are matching lines within their
stated scan, not a claim that each match is a distinct defect.

Historical documents were treated as dated evidence. Current code, project files,
release manifests, and executable test results win whenever an older record
contradicts them.

## Implemented remediation

| Area | Current control | Evidence |
| --- | --- | --- |
| Test & Copy truth | Same-drive agreement requires matching nonzero full-track CRC32 as well as AR CRC; cross-drive AR comparison remains separate. | `VerifyHistory`, `TestAndCopyResolver`, WPF tests |
| CTDB repair | Generates destinations in an owned same-volume stage, requires the repair branch to apply, independently verifies staged output, and atomically publishes a unique repaired sibling. The source is not overwritten. | [`RepairTransaction.cs`](../../CUETools.Wpf/Services/RepairTransaction.cs), `VerifyRepairTransactionTests` |
| Rip/convert publication | Cross-process reservation plus owned same-volume album staging; a populated album becomes visible with one directory rename. Collision, traversal, reparse, and cancellation fail closed. Existing sentinels are never reclaimed by path; they consume only that candidate and a numbered sibling is selected. The rename is the explicit commit point, so later marker or reservation cleanup cannot reclassify a committed album as failed. | [`AlbumOutputTransaction.cs`](../../CUETools.Wpf/Services/AlbumOutputTransaction.cs), album/convert/output tests |
| Misleading WPF settings | Five controls with no WPF runtime consumer were removed. Shared classic configuration and its live consumers remain. | WPF views/view models and dead-switch/runtime tests |
| External encoders | Absolute executable resolution, monotonic deadline, bounded stream/process cleanup, tree kill where available, required output, and staged publication. Lossless commands require an explicit decode contract and PCM digest/sample-count agreement. Managed imports carry a runtime-only approved size/hash; the exact executable is rehashed and held under a deny-write/delete lease through launch and self-verification. An absent-at-start destination is create-only; explicit overwrite mode replaces the path present at publication. | [`AudioEncoder.cs`](../../CUETools.Codecs/CommandLine/AudioEncoder.cs), [`LosslessOutputVerifier.cs`](../../CUETools.Codecs/CommandLine/LosslessOutputVerifier.cs), codec tests |
| WMA Lossless | Finalized staged output is independently decoded and compared sample-for-sample before publication. Lossy WMA is correctly exempt from bit equality. | [`WmaLosslessVerification.cs`](../../CUETools.Codecs.WMA/WmaLosslessVerification.cs), WMA tests |
| Native codec wrappers | libFLAC honors finalization/verify failure; libwavpack honors pack/update/final-flush failures; WavPack and MAC perform whole-output decode/compare before publication. FLAC/WavPack/MAC writers have terminal close state, repeated close is harmless, and write-after-close is rejected. libFLAC, WavPack, HDCD, and LAME now roll back partially initialized native handles, borrowed metadata, callback roots, and owned streams. | codec writers/readers and native-lifetime tests |
| ALAC publication | Uses a unique create-new work file; failure cannot delete or replace a prior output, and an absent-at-start competitor wins. A successful user-requested overwrite uses replace-at-publication semantics. | [`ALACWriter.cs`](../../CUETools.Codecs.ALAC/ALACWriter.cs), `ALACOutputTransactionTest` |
| Settings security | Proxy secrets use current-user DPAPI, plaintext migrates only after safe persistence, writes publish atomically, the UI never redisplays the secret, and polymorphic JSON is allowlisted. Unsupported protection fails the whole save. | `ProxyCredentialStore`, `SecretProtector`, `SettingsWriter`, `SettingsStore`, `KnownSettingsSerializationBinder`, security tests |
| Plugin/executable trust | Packaged plugins are bound by normalized path, size, SHA-256, managed identity, architecture, and deterministic order. Managed and native bytes are rechecked at load time under deny-write/delete sharing; restricted full-path native loads verify the returned module path, retain handles for process lifetime, and have no bare-name fallback. This includes packaged LAME and classic RAR/UnRAR. Imported executables require reapproval after a content change and are lease-bound at launch. | [`PluginTrustManifest.cs`](../../CUETools.Processor/PluginTrustManifest.cs), `EncoderCatalog`, trust tests |
| Diagnostic privacy | User-selected verify/repair inputs, rip/Test & Copy destinations, and owned staging roots are registered before work. Redaction is case-insensitive, longest-match-first, and never reprocesses replacement text. | `DiagnosticLog`, rip/verify services, `DiagnosticLogTests` |
| Icecast | HTTPS is preferred; cleartext credentials require explicit opt-in; credentials use DPAPI and proactively migrate; rejected connections are disposed. Constructor/connect failures reach the primary UI error path, cleanup failures are independently contained with type-only diagnostics, and the background writer is cleared in `finally`. | `IcecastEndpointPolicy`, `IcecastCredentialStore`, Icecast tests |
| MOTD | Bounded strict UTF-8 text over HTTPS only; finite timeouts; no remote image decoding, HTTP fallback, or legacy cache reuse. | [`frmCUETools.cs`](../../CUETools/frmCUETools.cs), source-invariant test, live TLS smoke |
| Parser hardening | Managed FLAC and ALAC readers enforce real input bounds. The corpus fuzzer distinguishes expected rejection from unexpected exceptions and checks invariants. Isolated child timeout, process-tree kill, and post-kill reap are all bounded; inability to terminate is a failed check. | codec readers, [`CorpusFuzzer.cs`](../../CUETools.Fuzz/CorpusFuzzer.cs) |
| Exception/warning diagnostics | Modern cross-thread relays preserve the original exception with `ExceptionDispatchInfo`; net20 deliberately preserves type/identity with a documented reset throw site. Warning fingerprints are budgeted. | `ExceptionRelay`, net20 runtime probe, warning gate |
| Release controls | Test discovery/skip floors, warning baselines, native rebuild/inventory, immutable action pins, required/forbidden-file contracts, plugin/native probes, hashes, receipts, and two SBOM forms. Recursive cleanup rejects reparse points throughout the tree; provenance refuses escaping or linked evidence targets and distinguishes real untracked source from generated native intermediates. | [`eng/ci`](../../eng/ci), [`eng/release`](../../eng/release), workflows |

## Codec and plugin truth

### Reachability

The WPF x64 artifact contract requires:

- 9 managed plugin DLLs;
- 5 native x64 dependencies;
- 14 exact trust-manifest/runtime entries in total;
- 19 encoder/decoder/HDCD registrations;
- 5 native initialization/version probes.

Those values come from
[`wpf-win-x64.manifest.json`](../../eng/release/wpf-win-x64.manifest.json) and were
validated against a clean publish. They are package facts, not a count of every
codec project in the repository.

The classic artifact remains broader: it includes FLACCL/OpenCL, architecture-
specific TTA C++/CLI, compression, SCSI, and other legacy plugins. The managed
FFmpeg wrapper was removed from the classic collection manifest because its native
FFmpeg bindings are not in that package. The standalone pinned FFmpeg build workflow
still exists, but its output is not silently advertised as part of either product.

### Verification tiers

"Verified lossless" has different mechanisms:

- whole finalized staged-output decode and PCM comparison: WMA Lossless, WavPack,
  Monkey's Audio, and approved external lossless command encoders;
- frame/finalization verification: managed Flake, ALAC, and libFLAC;
- no independent whole-output oracle in the current wrapper: TTA and raw WAV;
- FLACCL: classic/net47 OpenCL path, default verification remains off and the known
  exact-length trailing-buffer issue remains open until it can be built and exercised
  on an OpenCL host.

No generic lookahead padding was added to ALAC. Its buffer shape differs from FLAC;
copying FLAC's padding contract would corrupt normal ALAC operation.

## Executed evidence

### Release test suites

[`test-suites.json`](../../eng/ci/test-suites.json) is the current selection and
minimum-discovery authority.

| Suite | Discovered | Passed | Expected skipped |
| --- | ---: | ---: | ---: |
| Parity net47 | 22 | 18 | 4 |
| Codecs net47 | 109 | 107 | 2 |
| Processor net47 | 8 | 7 | 1 |
| Ripper net8.0-windows | 8 | 8 | 0 |
| WPF net8.0-windows | 241 | 241 | 0 |
| **Total** | **388** | **381** | **7** |

The separate net20 `ExceptionRelay` runtime probe also passed. The seven skips are
declared environment/fixture boundaries; none is reported as a pass. In particular,
the real WMA runtime case can skip on an unavailable local codec, and the old
`TestRipper` project is explicitly excluded because it initializes from 64 hardcoded
`Y:\Temp\dbg\960` hardware captures through the retired QualityTools adapter.

### Build, native, fuzz, and artifact gates

- Managed Release warning gate: 378 emitted warning lines, 37 normalized
  fingerprints, exactly matching the 37-entry baseline; no new fingerprint.
- Native x64 rebuild: libFLAC 267,776 bytes, WavPack 153,600 bytes, MACLib 193,024
  bytes; 68 warning lines normalized to the 11-entry native baseline; no new
  fingerprint.
- Deterministic fuzz: seed 20260712, 20,000 iterations, seven executable checks
  passed, zero failures, and one explicitly reported unsafe/native SCSI truncated
  boundary skip.
- Clean WPF publish: required-file contract passed for 34 files; trust entries
  14/14; registrations 19/19; native probes 5/5; one forbidden root fallback was
  absent; final directory contained 539 files.
- Release-safety harness: 32/32 checks passed under PowerShell 7 and Windows
  PowerShell 5.1. The additive forbidden-file contract harness passed 8/8 in each
  engine with a zero-warning validator build.
- Provenance: build receipt, contract snapshot, native inventory, and SHA-256 record
  for all 539 files were generated.
- SBOM: CycloneDX emitted 38 dependency/library components plus the application
  root. The Microsoft artifact scan detected zero dependency packages but emitted
  one product package and 539 file records; that SPDX output is therefore a file
  inventory, not evidence of zero dependencies.
- Workflow syntax: all four YAML files parsed successfully. `actionlint` was not
  installed locally, so semantic action validation awaits the hosted lane.

The validated tree is `bin/Release/CUETools2026-win-x64`. An older ignored
`publish/CUETools2026-win-x64` directory contains 518 files and no plugin manifest;
it is not release evidence and was left untouched because its provenance/retention
policy was not part of this remediation. Future cleanup should quarantine or
regenerate it through the artifact-GC pass rather than deleting it by resemblance.

### Additional framework checks

The changed codec libraries were built across their declared target frameworks:
ALAC and its console encoder, WMA, Icecast, libFLAC, libwavpack, HDCD, libmp3lame,
MACLib, and the shared codec library. The focused native-wrapper lifetime suite
passed 10/10 and the focused ALAC publication suite passed 6/6. Full classic
GUI builds did not run locally: the available toolchain lacks the complete .NET 4.7
targeting/Visual Studio SDK resolver and later hits stale legacy NuGet/runtime assets.
That is a toolchain boundary, not a claimed source-code pass.

## Static risk-surface census

The candidate census above excludes designer/generated sources. The navigational
risk scan below intentionally covers all 667 first-party C# files outside
`ThirdParty`, including tests and generated interop; the HTTP row excludes test
projects. These counts provide navigation priorities, not automatic findings:

| Pattern | Matching lines | Interpretation |
| --- | ---: | --- |
| `DllImport` literal | 171 | 166 active declarations plus 5 commented examples; expected native codec, optical-drive, UI, and platform interop still require architecture/package probes. |
| file/directory copy, move, replace, or delete operations | 101 | Publication/cleanup-heavy product; ownership, containment, and rollback tests are load-bearing. |
| `unsafe` | 385 | Concentrated in codec/DSP/SCSI paths; fuzz and golden PCM checks remain essential. |
| process start call sites | 22 | External codecs, shell links, fuzz helpers, and tests; command execution is now bounded in the codec path. |
| dynamic assembly load | 7 | Production plugin loads plus release/test validators; packaged WPF loads are manifest-bound. |
| `TypeNameHandling.Auto` | 1 | Retained only with `KnownSettingsSerializationBinder`; an unrestricted-deserializer scan found zero matches. |
| `NotImplementedException` | 3 | Legacy DirectSound/CoreAudio edge operations; not WPF release blockers, but should be exercised before those playback backends are expanded. |
| C# string literals beginning `http://` | 37 | Dominated by generated XML namespace identifiers; remaining live CTDB/freedb/classic-link uses are separately tracked. |

The broad `throw variable;` pattern is intentionally not summarized as "23 bad
rethrows." It includes newly constructed exceptions, test fakes, comments, the
deliberate net20 identity tradeoff, and real legacy sites. Context classification is
required before editing. The audited Processor/WMA caught-exception stack-loss paths
were fixed; legacy Freedb/SCSI construction style remains cleanup debt.

## Remaining work, in priority order

### 1. Hosted release evidence

- Run all pinned GitHub workflows.
- Build Release classic AnyCPU/x64/Win32 with full Visual Studio/devenv and validate
  [`classic-win.manifest.json`](../../eng/release/classic-win.manifest.json).
- Require a release lane where real WMA Lossless encode/decode cannot skip.
- Run actionlint and retain the existing YAML parse check.
- Generate and compare classic hashes/SBOMs; the classic artifact did not exist
  locally.

### 2. Hardware and service integrations

- Optical drive: real rip, cancellation, Test & Copy disagreement, two concurrent
  publishers, and retry of a preserved incomplete stage.
- CTDB: a known recoverable image/disc through staged repair and independent
  post-repair verification. Server TLS is an external operator boundary.
- Icecast: HTTPS source/auth/metadata, certificate rejection, explicit HTTP opt-in,
  rejected-auth disposal, user.config migration, and the supported Mono matrix.
- AccurateRip, gnudb/freedb, MusicBrainz links, and MOTD: retain bounded live smoke
  checks because these services can change independently of the repository.

### 3. Codec integrations unavailable locally

- Build and exercise FLACCL on OpenCL hardware before fixing its exact-length
  verification buffer. The shared GPU pipeline can have only two bytes of trailing
  slack on a one-compute-unit device, so this should not be patched by analogy.
- Run real external FLAC and TAK command pairs. The independent decoder contract is
  tested with fakes, and an ffmpeg/ALAC integration has run, but those executables
  were not locally available in the final gate.
- Add real whole-output verification where feasible for remaining lossless wrappers,
  especially TTA. WAV is structurally simpler but still lacks a separate oracle.
- Define stronger overwrite conflict semantics and resolve the close-to-publish
  hostile replacement window plus crash-orphan policy for shared codec work files.
  An absent-at-start destination is create-only, but successful overwrite mode
  intentionally replaces the path present at publication. Work names are
  unpredictable and owned, yet a same-user hostile peer that observes a running
  encoder can still target that path after the child releases it.

### 4. Legacy and supply-chain debt

- Replace `TestRipper`'s 64 machine-specific captures with a deterministic in-memory
  majority/C2 fixture and move it off the retired test adapter.
- Decide whether classic multi-track output should gain album-wide transactionality;
  its codec files are atomic individually today.
- Roll out NuGet lock files only after compatibility testing across net20/net47/net8;
  do not break the legacy restore graph mechanically.
- Add signing/attestation. Current hashes prove identity after publication, not
  publisher identity.
- Run release generation in a trusted, non-concurrently-mutated workspace. The
  PowerShell 5.1/.NET path checks fail closed on observed reparse points, but path
  inspection and later filesystem mutation cannot form one atomic anti-TOCTOU
  operation. Git-ignored submodule build output is deliberately not enumerated by
  source provenance; the receipt states that policy.
- Recover immutable upstream/build provenance for vendored HDCD, libmp3lame, UnRAR,
  and TTA source history.
- Plan and validate the known codec drift one integration at a time: WavPack 5.8.1
  to 5.9.0, Monkey's Audio 10.86 to 13.20, and LAME 3.100 to the newly released
  4.0. The unshipped FFmpeg workflow/wrapper is 7.1.1 while upstream stable is
  8.1.2. The current local changes inside WavPack/taglib-sharp submodules must be
  preserved and reconciled, not overwritten.
- Continue targeted review of the three playback `NotImplementedException` sites and
  legacy TODOs; neither count alone authorizes behavior changes.

## Anti-dark-code skill improvements

The repository-local skill was updated rather than changing the user's global skill
installation. The audit exposed several reusable failure modes, now encoded in the
local workflow:

- distinguish filename searches from content searches (`rg --files | rg PATTERN`
  cannot prove source content);
- define reported counts as matching lines unless unique semantics are actually
  classified;
- require one immediate follow-up pass for compound audits so contradictions found
  late are reconciled rather than deferred;
- refresh stale dated claims while preserving historical evidence;
- record product-specific reachability as reachable, packaged-only, historical,
  externally blocked, or unobserved instead of collapsing it to project references;
- bound history review and checkpoint single-result/API failures;
- use tool-neutral orchestration language and explicit path-conflict rules;
- garbage-collect stale artifacts and contradictory generated evidence;
- declare transaction commit points so post-commit cleanup cannot turn success into
  a duplicate retry;
- bound both process-tree termination and the post-kill reap in timeout harnesses;
- scan recursive-cleanup descendants and recheck every receipt leaf immediately
  before writing;
- distinguish PowerShell in-session success state from native child exit codes;
- keep confidence and coverage-status values canonical while putting scope
  qualifications in evidence fields;
- keep architecture, security, unknowns, and review records in their intended
  locations.

The skill passes the skill-creator `quick_validate.py` validator after these changes.

## Release decision

The modern WPF x64 tree is suitable for a hosted release-candidate run. It should not
be called a final release until the hosted workflow, real WMA lane, and at least one
hardware/service smoke matrix above are green.

The classic distribution is not locally certified. Its source changes have targeted
library and source-invariant coverage, but the complete classic GUI/C++/CLI artifact
still requires the full Visual Studio lane.
