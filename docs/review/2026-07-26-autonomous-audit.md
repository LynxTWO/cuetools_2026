# Autonomous Codebase Audit - 2026-07-26

## Verdict

All verified findings that could be fixed and exercised safely on this machine were
remediated. The modern WPF product has a green local Release gate, a validated clean
artifact, a hash-bound plugin set, whole-album transactional publication, protected
settings, and explicit lossless-output verification contracts.

That is not the same as declaring the entire repository release-ready. The local
classic WinForms/C++/CLI solution now builds in all three requested configurations:
AnyCPU completed with 53 succeeded and 0 failed; x64 and Win32 each completed with
2 succeeded, 0 failed, and 59 intentionally skipped configuration entries. TTA
compiled and linked for both native configurations. A targeted Installer Projects
build also passed 8 projects with 0 failures and produced a 929,792-byte MSI. This
machine reached those results through the Visual Studio 18 resolver with the VS2022
v143 toolset, which is useful local evidence but not identical to the pinned hosted
VS2022 image. Final frozen-output receipts and the hosted repeat remain release gates.
Real optical-drive reads, a full FLAC rip, same-drive Test & Copy, staged CTDB repair,
WMA Lossless
verification, Icecast 2.5 source/metadata streaming, and OpenCL/FLACCL verification
have now run locally. Hosted CI, signing, external CTDB TLS, selected failure-injection
cases, and some external-command integrations remain named evidence gaps rather than
inferred passes.

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
| Test & Copy truth | Same-drive agreement requires matching nonzero full-track CRC32 as well as AR CRC; cross-drive AR comparison remains separate. Test and Copy CRC roles persist per disc and appear on the Rip track grid; a confirming third read is never mislabeled as Copy. A live H: run completed two independent full reads and published 11 verified FLAC files. Current source then passed 25 Paranoid cache-defeat windows twice; the final 2-minute-53-second run used the real offset and consumed the end-of-disc path, beyond the former 19-second SCSI failure. | `VerifyHistory`, `TestAndCopyResolver`, `RipViewModel`, WPF tests and live H: integration |
| Drive calibration and edge reads | Missing or stale versioned calibration runs before the first Rip, Verify, or Test & Copy. Secure/Paranoid reads require a proven independent reread strategy. Positive cache evidence is monotonic: later smaller or apparently uncached timing results retain the largest proven flush. Flushes complete or fail explicitly. Lead-in/out probes use the exact one-sector command shape and the full offset-sized range; only proven edges replace zero-padding in the reader. | `DriveCalibrationService`, `CDDriveReader`, calibration-policy tests and live H: Paranoid probe |
| Output-assurance truth | AccurateRip, CTDB, and Test & Copy describe read evidence; they do not imply that the encoded file was decoded and compared. Rip results, `rip.verify`, history, reports, and UI now carry lossless/output-verification state and detail explicitly, warning when a reachable lossless encoder has no output oracle. Assurance is granted only to exact bundled settings types (or the exact FLACCL assembly/type identity) and explicit command verifier contracts; a user-plugin subclass that inherits a convenient verify property is rejected as unknown. | `OutputVerificationAssurance`, `VerifyRecord`, `RipReport`, report/history tests |
| CTDB repair | Generates destinations in an owned same-volume stage, requires the repair branch to apply, independently verifies staged output, and atomically publishes a unique repaired sibling. Completed lossless rips offer repair immediately for recoverable track-set or image output. Repair preserves source basenames, human/custom tags, artwork, and exact CDTOC identity while deliberately removing stale AccurateRip/CTDB payload-proof tags. Ambiguous and escaping paths fail closed. A live opt-in run repaired a deliberately damaged image and left source hashes unchanged. | [`RepairTransaction.cs`](../../CUETools.Wpf/Services/RepairTransaction.cs), `VerifyRepairTransactionTests`, `RepairPreservationIntegrationTests`, live repair smoke |
| Rip/convert publication | Cross-process reservation plus owned same-volume album staging; a populated album becomes visible with one directory rename. Collision, traversal, reparse, and cancellation fail closed. Existing sentinels are never reclaimed by path; they consume only that candidate and a numbered sibling is selected. The rename is the explicit commit point, so later marker or reservation cleanup cannot reclassify a committed album as failed. | [`AlbumOutputTransaction.cs`](../../CUETools.Wpf/Services/AlbumOutputTransaction.cs), album/convert/output tests |
| Misleading WPF settings | Five controls with no WPF runtime consumer were removed. Shared classic configuration and its live consumers remain. | WPF views/view models and dead-switch/runtime tests |
| External encoders | Absolute executable resolution, monotonic deadline, bounded stream/process cleanup, tree kill where available, required output, and staged publication. Lossless commands require an explicit decode contract and PCM digest/sample-count agreement. Managed imports carry a runtime-only approved size/hash; the exact executable is rehashed and held under a deny-write/delete lease through launch and self-verification. An absent-at-start destination is create-only; explicit overwrite mode replaces the path present at publication. | [`AudioEncoder.cs`](../../CUETools.Codecs/CommandLine/AudioEncoder.cs), [`LosslessOutputVerifier.cs`](../../CUETools.Codecs/CommandLine/LosslessOutputVerifier.cs), codec tests |
| WMA Lossless | Finalized staged output is independently decoded and compared sample-for-sample before publication. A real net8 Windows Media encode/finalize/independent-decode/PCM verification passed; lossy WMA is correctly exempt from bit equality. | [`WmaLosslessVerification.cs`](../../CUETools.Codecs.WMA/WmaLosslessVerification.cs), WMA tests and live round trip |
| Native codec wrappers | libFLAC honors finalization/verify failure; libwavpack honors pack/update/final-flush failures; WavPack and MAC perform whole-output decode/compare before publication. FLAC/WavPack/MAC writers have terminal close state, repeated close is harmless, and write-after-close is rejected. libFLAC, WavPack, HDCD, and LAME now roll back partially initialized native handles, borrowed metadata, callback roots, and owned streams. | codec writers/readers and native-lifetime tests |
| Legacy framework compatibility | The secure-ripper slip-correlation API no longer exposes C# tuple metadata unavailable to declared net20 consumers. A named `SlipCorrelationResult` and out parameters preserve the values without requiring `System.ValueTuple`/`TupleElementNamesAttribute`; callers and tests use the same contract. The adaptive-speed ladder no longer requires net35's `SortedSet<T>`: it uses `List<T>` with identical ordering because constant rungs are strictly ascending and the 97% cutoff keeps every retained rung below the appended real maximum. | `SlipCorrelator`, `AdaptiveReadSpeedController`, `SCSIDrive`, ripper tests |
| ALAC publication | Uses a unique create-new work file; failure cannot delete or replace a prior output, and an absent-at-start competitor wins. A successful user-requested overwrite uses replace-at-publication semantics. | [`ALACWriter.cs`](../../CUETools.Codecs.ALAC/ALACWriter.cs), `ALACOutputTransactionTest` |
| Settings security | Proxy secrets use current-user DPAPI, plaintext migrates only after safe persistence, writes publish atomically, the UI never redisplays the secret, and polymorphic JSON is allowlisted. Unsupported protection fails the whole save. The settings dialog edits a draft, so Cancel cannot mutate the persisted live configuration. A failed Icecast save restores both the live settings object and the prior in-memory DPAPI blob, so "not saved" cannot silently alter active stream configuration. | `ProxyCredentialStore`, `IcecastCredentialStore`, `SecretProtector`, `SettingsWriter`, `SettingsStore`, `KnownSettingsSerializationBinder`, security/settings tests |
| Persistence failure truth | Missing, loaded, and corrupt JSON are distinct states. Corrupt verification/calibration state fails closed instead of being silently replaced; the drive page reports corrupt calibration and always leaves busy state. Detect/calibrate reject re-entry while busy or ripping, and invalidate command state at both start and finish so disabled controls track the real operation. Corrupt recent history is backed up and not published over. Save failure propagates and in-memory history changes only after the durable write. Newer unknown advanced fields are ignored while malformed known fields and unapproved `$type` values still reject. | `GzJson.TryLoad`, `DriveViewModel`, persistence/fuzz/report tests |
| Plugin/executable trust | Packaged plugins are bound by normalized path, size, SHA-256, managed identity, architecture, and deterministic order. Managed and native bytes are rechecked at load time under deny-write/delete sharing; restricted full-path native loads verify the returned module path, retain handles for process lifetime, and have no bare-name fallback. Discovery requires exact `IsAssignableFrom` identity for encoder, decoder, and ripper contracts rather than accepting an interface short-name lookalike and appending a null cast. A type carrying the compression-plugin attribute must likewise implement the real `ICompressionProvider` contract. HDCD discovery requires the exact usable filter shape: `HDCDDotNet`, `IAudioDest`, `IAudioFilter`, `IFormattable`, and the public `(int,int,int,bool)` constructor. Valid and impostor types are tested. This includes packaged LAME and classic RAR/UnRAR. A strict DLL-only enrollment script creates a separate exact manifest under `%AppData%\CUETools2026\plugins`; replacement is explicit and backed up. Imported executables require reapproval after a content change and are lease-bound at launch. These controls establish approved byte identity, not publisher signing. | [`PluginTrustManifest.cs`](../../CUETools.Processor/PluginTrustManifest.cs), `CUEProcessorPlugins`, compression plugin loader, `EncoderCatalog`, `Install-CUEToolsPlugin.ps1`, trust tests |
| Diagnostic privacy | User-selected verify/repair inputs, rip/Test & Copy destinations, and owned staging roots are registered before work. Redaction is case-insensitive, longest-match-first, and never reprocesses replacement text. | `DiagnosticLog`, rip/verify services, `DiagnosticLogTests` |
| Icecast | HTTPS is preferred; cleartext credentials require explicit opt-in; credentials use DPAPI and proactively migrate; rejected connections are disposed. Configured MP3 bitrate and joint-stereo choices now reach the LAME writer rather than silently falling back to its defaults. Unsupported bitrates are rejected before any source connection opens or credentials leave the process. The raw source connection, authentication rejection, metadata update, listener bytes, flush/close, and teardown passed against disposable Icecast 2.5.0. Constructor/connect failures reach the primary UI error path, cleanup failures are independently contained with type-only diagnostics, and the UI clears stale transmit state. | `IcecastEndpointPolicy`, `IcecastCredentialStore`, Icecast/LAME tests and live smoke |
| MOTD | Bounded strict UTF-8 text over HTTPS only; finite timeouts; no remote image decoding, HTTP fallback, or legacy cache reuse. | [`frmCUETools.cs`](../../CUETools/frmCUETools.cs), source-invariant test, live TLS smoke |
| Parser hardening | Managed FLAC and ALAC readers enforce real input bounds. The corpus fuzzer distinguishes expected rejection from unexpected exceptions and checks invariants. Isolated child timeout, process-tree kill, and post-kill reap are all bounded; inability to terminate is a failed check. | codec readers, [`CorpusFuzzer.cs`](../../CUETools.Fuzz/CorpusFuzzer.cs) |
| RAR streaming | Official signed UnRAR 7.23.0 x86/x64 exposes the required ABI and passed real production-provider full-read/backward-seek checks. Converting the oracle into a committed 280-byte RAR5 fixture exposed a race where `Read` could accept the old pass's EOF before rewind was acknowledged; it now waits while rewind is pending. The focused case and 20/20 repeated no-build runs pass. | [`RarStream.cs`](../../CUETools.Compression.Rar/RarStream.cs), [`RarCompressionProviderTest.cs`](../../CUETools/CUETools.TestCodecs/RarCompressionProviderTest.cs), native provenance inventory |
| Exception/warning diagnostics | Modern cross-thread relays preserve the original exception with `ExceptionDispatchInfo`; net20 deliberately preserves type/identity with a documented reset throw site. Warning fingerprints are budgeted. | `ExceptionRelay`, net20 runtime probe, warning gate |
| Release controls | Test discovery/skip floors, warning baselines, native rebuild/inventory, immutable action pins, required/forbidden-file contracts, plugin/native probes, hashes, receipts, and two SBOM forms. Recursive cleanup rejects reparse points throughout the tree; provenance refuses escaping or linked evidence targets and distinguishes real untracked source from generated native intermediates. | [`eng/ci`](../../eng/ci), [`eng/release`](../../eng/release), workflows |

## Codec and plugin truth

### Reachability

The WPF x64 artifact contract requires 36 paths, including:

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
The classic required-file contract contains 97 paths.

### Verification tiers

"Verified lossless" has different mechanisms:

- whole finalized staged-output decode and PCM comparison: WMA Lossless, WavPack,
  Monkey's Audio, and approved external lossless command encoders;
- frame/finalization verification: managed Flake, ALAC, and libFLAC;
- no independent whole-output oracle for raw WAV;
- FLACCL: classic/net47 OpenCL path with per-frame decode/compare. Its exact-length
  verifier now uses BitReader logical remaining-bit bounds and passes the exact encoded
  frame extent directly. The RTX 3060 rerun passed OpenCL modes 0-8, two CPU workers,
  24-bit input, and the exact 4096-sample boundary; verify-on/off output remained
  byte-identical. The app performs a one-time migration to enable verify; the
  standalone CLI remains explicitly opt-in through `--verify`.

No generic lookahead padding was added to ALAC. Its buffer shape differs from FLAC;
copying FLAC's padding contract would corrupt normal ALAC operation.

## Executed evidence

### Release test suites

[`test-suites.json`](../../eng/ci/test-suites.json) is the current selection and
minimum-discovery authority. Aggregate discovered/pass/skip totals are deliberately
refreshed by the final canonical gate after this implementation wave; the earlier
388/381/7 snapshot is historical and is not repeated as current evidence here.

The separate net20 `ExceptionRelay` runtime probe passed. Declared skips remain visible,
not converted into passes. The real WMA runtime path has now also passed as a separate
live net8 integration. Review found that old `TestRipper` exercised a stale copy of
the secure-sector algorithm that ignored the C2 vote plane. Shipping code and the test
now call the same extracted `SecureSectorVote.CorrectSector` helper. The SDK net47 test
generates deterministic 64-pass data over 32 sectors with C2-flagged and unflagged
minority corruption, an insufficient-clean-pass case, and C2-plane reconstruction
without false secure assurance. It is enrolled in the canonical manifest with a
three-test minimum and zero skips; the canonical run passed all 3 tests with 0
failures and 0 skips.

The passing net20 `ExceptionRelay` probe is not a claim that the whole net20 solution
lane is green. A later classic AnyCPU build found the slip-correlation tuple contract
required unavailable tuple metadata; that declared-framework blocker is fixed with a
named result/out-parameter API. The next pass found net35-only `SortedSet<T>` in the
adaptive-speed ladder; its ordered `List<T>` replacement preserves the documented
rung/cutoff semantics. The local AnyCPU, x64, and Win32 solution builds now pass; the
final frozen-output receipt run remains pending.

### Build, native, fuzz, and artifact gates

- Managed Release warning gate: 378 emitted warning lines, 37 normalized
  fingerprints, exactly matching the 37-entry baseline; no new fingerprint.
- Native x64 rebuild: libFLAC 267,776 bytes, WavPack 153,600 bytes, MACLib 193,024
  bytes; 68 warning lines normalized to the 11-entry native baseline; no new
  fingerprint.
- Deterministic fuzz: seed 20260712, 20,000 iterations, seven executable checks
  passed, zero failures, and one explicitly reported unsafe/native SCSI truncated
  boundary skip.
- Clean WPF publish: the required-file contract is now 36 paths; trust entries
  14/14, registrations 19/19, native probes 5/5, and the forbidden-root check all
  passed in the recorded clean-artifact run. Final artifact/file-hash totals are
  refreshed by the final release gate.
- Release-safety and plugin-enrollment harnesses passed under both PowerShell 7 and
  Windows PowerShell 5.1, including refusal of extra files, reparse points, implicit
  replacement, incorrect hashes, and merged publication.
- Provenance generation produced the build receipt, contract snapshot, native
  inventory, SHA-256 file records, notices, and SBOM inputs. Recovered evidence now
  traces the retained LAME, UnRAR, and TTA bytes/history as described below.
- SBOM: CycloneDX dependency output and the Microsoft SPDX file inventory were both
  generated. The latter is a file inventory, not evidence that the product has zero
  dependencies.
- Workflow syntax: all four YAML files parsed successfully and official
  `actionlint` v1.7.12 passed all four locally. Hosted execution remains a separate
  environment check.

The validated tree is `bin/Release/CUETools2026-win-x64`. An older ignored
`publish/CUETools2026-win-x64` directory contains 518 files and no plugin manifest;
it is not release evidence and was left untouched because its provenance/retention
policy was not part of this remediation. Future cleanup should quarantine or
regenerate it through the artifact-GC pass rather than deleting it by resemblance.

### Additional framework checks

The changed codec libraries were built across their declared target frameworks:
ALAC and its console encoder, WMA, Icecast, libFLAC, libwavpack, HDCD, libmp3lame,
MACLib, and the shared codec library. The focused native-wrapper lifetime suite
passed 10/10 and the focused ALAC publication suite passed 6/6. The native x64 gate
also passes for libFLAC, WavPack, and Monkey's Audio without new warnings. After the
two declared-net20 source fixes, the redirected-output classic AnyCPU solution build
completed with 53 succeeded and 0 failed. The x64 and Win32 solution configurations
each completed with 2 succeeded, 0 failed, and 59 skipped configuration entries.
TTA compiled and linked separately for both configurations, and both outputs are
valid CLR PE files. Installer Projects 3.0.0, with its required
`DisableOutOfProcBuild` integration, passed a targeted build at 8 succeeded and
0 failed and produced a 929,792-byte MSI. The successful local route combines the
Visual Studio 18 resolver with the VS2022 v143 toolset; it does not substitute for
the pinned hosted VS2022 workflow. Final frozen-output validation, direct CUEPlayer
compilation evidence, receipts, hashes, and SBOM comparison remain pending.

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

### 1. Release toolchain and hosted evidence

- Finish the frozen-output classic pass, direct CUEPlayer compilation check, and
  receipt/hash/SBOM validation against the 97-path
  [`classic-win.manifest.json`](../../eng/release/classic-win.manifest.json).
- Run all pinned GitHub workflows on their intended VS2022 image and compare their
  artifacts and receipts with the passing local AnyCPU/x64/Win32/MSI evidence.
- Retain the passing real WMA Lossless integration and local actionlint evidence in
  the release lane rather than relying only on availability-gated tests.
- Preserve and compare classic hashes/SBOMs from the frozen artifact.

### 2. Hardware and service integrations

- Optical-drive success paths are observed: H: completed an 11-track read-only
  verification (AR 107/424, CTDB 114/544) and full 11-track FLAC rip with zero read
  errors; K: completed a 12-track read-only verification (AR 257/707, CTDB
  1345/1464) with zero read errors; both drives answered simultaneous SCSI
  inquiry/TOC. H: also passed a clean same-drive Test & Copy integration in 842
  service seconds: calibration confirmed a 786,432-byte cache flush; independent
  413-second verify and 410-second encode reads produced 11 FLAC files; both reads
  reported AR 107/424 and CTDB 114/544 with zero reread/failed windows. Runtime result
  and deserialized `rip.verify` both reported `Format=flac`,
  `OutputVerificationKnown=true`, `LosslessOutput=true`, and
  `OutputVerificationPerformed=true`, with decoded-and-compared detail. The first
  attempt is retained as diagnostic evidence: an overlapping dependency build
  coincided with transient H: SCSI ASC/ASCQ 08/0A at 27% of Copy; the isolated rerun
  crossed that point and passed. The later behavior-preserving extraction of the same
  shipping vote into `SecureSectorVote` means a final-source no-build H: rerun is still
  pending. Remaining hardware work includes deliberate cancellation,
  disagreement/failure injection, concurrent publication, and preserved incomplete-stage
  retry.
- CTDB staged repair is observed on a deliberately damaged known image, including
  independent post-repair verification and unchanged source hashes. The completed-rip
  route is also observed on K: with a real damaged 24-track CD: three recovery windows
  exhausted rereads, final FLAC proof passed, the Rip page immediately offered repair
  for six sectors, and repair published a separately verified `album - repaired`
  sibling. FFmpeg `-xerror` decoded all 24 repaired FLACs; the original 29 top-level
  files retained aggregate SHA-256
  `56B8701EEF43A3A368DE5E65801D503EC24E807EFCABB68A301B39921F9C212B`.
  Server TLS remains an external operator boundary.
- Icecast 2.5.0 source/auth/metadata/listener/flush/teardown is observed. Certificate
  rejection, a real HTTPS endpoint, user.config migration, and the supported Mono
  matrix remain separate checks.
- AccurateRip, gnudb/freedb, MusicBrainz links, and MOTD: retain bounded live smoke
  checks because these services can change independently of the repository.

### 3. Remaining codec integration work

- Preserve the passing FLACCL RTX 3060 matrix in a repeatable hardware lane; add a
  second OpenCL implementation/device when available rather than inferring universal
  driver compatibility from one GPU.
- Run real external FLAC and TAK command pairs. The independent decoder contract is
  tested with fakes, and an ffmpeg/ALAC integration has run, but those executables
  were not locally available in the final gate.
- Add real whole-output verification where feasible for remaining lossless wrappers,
  especially TTA. Its x64/Win32 C++/CLI builds now pass, but C4103 flags possibly
  leaked `#pragma pack(1)` state and C4244 flags `_finalSampleCount` narrowing. Review
  that ABI boundary and change it only with a TTA round-trip/corpus oracle. WAV is
  structurally simpler but still lacks a separate oracle.
- Define stronger overwrite conflict semantics and resolve the close-to-publish
  hostile replacement window plus crash-orphan policy for shared codec work files.
  An absent-at-start destination is create-only, but successful overwrite mode
  intentionally replaces the path present at publication. Work names are
  unpredictable and owned, yet a same-user hostile peer that observes a running
  encoder can still target that path after the child releases it.

### 4. Legacy and supply-chain debt

- Keep `TestRipper`'s passing SDK net47 production-helper fixture and its
  three-test/zero-skip contract in CI.
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
- Preserve the recovered origin evidence for HDCD, RareWares libmp3lame, RARLAB
  UnRAR, and TTA in the release inventory. UnRAR is now official signed 7.23.0
  for x86/x64, with exact hashes, required exports, and real production
  `RarStream` full-read/backward-seek evidence in both process architectures;
  its committed regression also fixed backward-seek replay against stale EOF and
  passed 20/20 repeats. The prior 6.11 import record remains historical evidence.
  The retained LAME DLLs are byte-identical to the SHA-256-recorded RareWares
  archives. Evidence that cannot be recovered has an explicit retain decision,
  not an open-ended search: the public Christopher Key archives expose the
  reference command-line decoder rather than this DLL's source/recipe; the
  RareWares archives contain only DLL/LIB/EXP outputs; and no present action can
  create a TTA checksum captured in 2009. TTA's current official archive hash
  and the import's exact local delta are recorded. HDCD and LAME may be replaced
  only through the behavior/ABI/corpus gates named in the native inventory.
- Plan and validate the remaining codec drift one integration at a time. WavPack
  5.9.0 is complete with clean Win32/x64 builds and a focused real round trip.
  LAME 3.100 to the newly released 4.0 remains separate. Monkey's Audio 13.20
  is complete with a hash-bound SDK, adapted `IAPEIO` wrapper, both-architecture
  native builds, and real 16/24-bit verified round trips. The unshipped FFmpeg
  workflow/wrapper is 7.1.1 while upstream stable is
  8.1.2. The current local changes inside taglib-sharp must be preserved and
  reconciled, not overwritten.
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
- review serialized defaults as migrations, not just constructors: prove fresh-install,
  upgrade, user-opt-out, and older-settings behavior separately;
- treat plugin/public API compatibility and persistence shape as explicit consumers,
  including callers compiled against historical members;
- distinguish `Connect()` success from live write/flush/close/service proof, and trace
  exceptional paths back into UI state so a failed worker cannot leave a stale
  "connected" control;
- propagate assurance facts through every consumer (runtime result, sidecar, history,
  report, and UI) rather than inferring encoded-file verification from AccurateRip,
  CTDB, or repeated-read evidence;
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

The modern WPF x64 tree is suitable for a hosted release-candidate run. WMA,
CTDB-repair, Icecast, FLACCL, and optical/Test & Copy mechanism evidence are green
locally; the short final-source H: rerun after `SecureSectorVote` extraction remains
pending.
It should not be called a final signed release until the hosted workflow, final clean
artifact gate, and publisher signing/attestation policy are green.

The classic solution and targeted MSI now have local Visual Studio evidence across
AnyCPU, x64, and Win32, including both TTA architectures. The distribution is not yet
certified: its frozen 97-path artifact/receipt checks and the pinned hosted VS2022
lane still need to agree with that local evidence.
