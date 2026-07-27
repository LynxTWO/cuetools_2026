# Safe-Fix Plan

**Current authority, 2026-07-26:** the wave below has been executed through its
locally available gates. Individual batch notes preserve intermediate counts and
plans, but the final status/counts are in `2026-07-26-autonomous-audit.md` and
the final canonical gate rather than this historical plan. Optical-drive reads, an
H: full FLAC rip and same-drive Test & Copy, CTDB repair, WMA Lossless, FLACCL/OpenCL,
Icecast 2.5.0, and local actionlint now have direct evidence. The local classic
matrix is green: AnyCPU 58/0/11, and x64 and Win32 9/0/60,
TTA in both architectures, and Installer Projects 8/0 with a 929,792-byte MSI.
The local frozen classic receipt and exact 97-file collection pass; hosted parity
remains pending. Signing, CTDB TLS, and named residual
hardware/service failure cases remain pending; H: Test & Copy also needs a final-source
repeat after the behavior-preserving `SecureSectorVote` extraction.
no intermediate "ready" label should be read as the current state.

Bounded remediation batches under pass 11 Step 2. One section per batch. Update statuses as batches land.

## Batch 1: test modernization and CI gating (2026-07-02)

**Backlog source:** coverage ledger S13/S14 next-pass entries; closed unknown "Do the MSTest suites pass on current code?" in `docs/unknowns/coverage-pass.md`.

**Goal:** the green suites (TestParity, TestCodecs) build without full Visual Studio and gate every push.

**Why safe now:** suite results are known green (verified 2026-07-02); changes touch test projects, the solution file, and CI only - no shipped code. Not approval-gated (S13 is flagged approval-gated for release-path changes; adding a test gate does not alter the release artifact path, and rollback is removing the steps).

### Exact changes

1. `CUETools/CUETools.TestParity/CUETools.TestParity.csproj` - rewrite as SDK-style, net47, MSTest v2 packages (MSTest 3.6.1 + Microsoft.NET.Test.Sdk 17.11.1), `GenerateAssemblyInfo=false` (keeps `Properties\AssemblyInfo.cs`), exclude orphaned `CDRepairTest.cs` and `CDRepairEncodeTest.cs` (present on disk, absent from the old compile list).
2. `CUETools/CUETools.TestCodecs/CUETools.TestCodecs.csproj` - same, plus: `PlatformTarget=x64` (native libFLAC is x64), flatten `Data\*` to the output root (tests open bare filenames), copy `ThirdParty\x64\libFLAC_dynamic.dll` into output when it exists, drop the unused `CSScriptLibrary` reference, exclude orphaned `FileGroupInfoTest.cs` (TestProcessor owns the live copy).
3. `CUETools/CUETools.TestCodecs/FlacWriterTest.cs` - remove the two `[DeploymentItem]` attribute lines pointing at `../ThirdParty*/x64/libFLAC_dynamic.dll`; they only resolved under the original author's VS test settings, and their presence switches MSTest v2 into deployment mode, which breaks every bare-filename data access. The csproj copy above replaces them.
4. `CUETools/CUETools.TestProcessor/CUETools.TestProcessor.csproj` - same SDK-style conversion (build-only; its tests stay red until fixtures exist, so CI does not run it).
5. `CUETools.sln` - flip the project-type GUID for the three converted projects from `{FAE04EC0-...}` (legacy C#) to `{9A19103F-...}` (SDK-style) so `devenv` loads them correctly.
6. `.github/workflows/CI-windows.yml` and `release-windows.yml` - add two steps after the existing builds: `dotnet test` on TestParity and TestCodecs (Release). The Release|x64 devenv build already produces `ThirdParty\x64\libFLAC_dynamic.dll` via `$(SolutionDir)`-relative OutDir, so the codec tests get their native dependency.

**Out of scope (deliberately):** `TestRipper` stays legacy MSTest v1 (hardware-dependent; migrate with slice S2). TestProcessor fixtures (tracked unknown). MSTest migration of `CUETools.TestHelpers` is unnecessary (plain library).

### Behavior that must remain unchanged

- Shipped binaries and `collect_files.bat` output: untouched (tests are not collected).
- Existing devenv solution builds: converted projects must still build inside the sln.
- Test semantics: same test set runs; the only test-source diff is deleting two dead `[DeploymentItem]` attributes.

### Verification

- Local: `dotnet test` on TestParity and TestCodecs must reproduce the known-green baseline (18 passed / 4 skipped; 34 passed / 1 skipped) before commit.
- CI: first push after the change must show the new test steps green.

### Rollback

Revert the batch commits; no data or release artifacts affected.

**Status:** landed 2026-07-02. Local verification matched the known-green baseline exactly: TestParity 18 passed / 4 skipped, TestCodecs 34 passed / 1 skipped, TestProcessor builds. CI verification pending first push (watch the two new test steps).

## Wave 2: 2026-07-26 audit remediation

**Approval:** on 2026-07-26 the user authorized autonomous implementation of all findings
from the 2026-07-25 audit and allowed green worktree changes to be committed. This includes
the normally protected repair, concurrency, credential, and release-control areas.

The wave is split into independently reviewable batches. A later batch does not inherit a
green status from an earlier one.

### Batch 2A: Test & Copy checksum truth

**Backlog source:** R19.

**Exact files:** `CUETools.Wpf/Accuracy/VerifyHistory.cs`,
`CUETools.Wpf/Accuracy/TestAndCopyResolver.cs`,
`CUETools.Wpf.Tests/TestAndCopyResolverTests.cs`, and the resolver fuzz test if needed.

**Why safe now:** the full CRC is already recorded. The change narrows the Test & Copy pass
condition to the bit-identity contract the UI already promises. Cross-drive verify history
keeps its offset-safe AccurateRip comparison.

**Behavior that must remain unchanged:** cross-drive history matches on ARv2/ARv1; a valid
same-drive Test & Copy with equal full CRC still passes; staged-read selection remains
whole-read rather than per-track assembly.

**Checks:** targeted resolver tests, then all `CUETools.Wpf.Tests`; inspect exact discovered,
passed, failed, and skipped counts.

**Rollback:** revert this batch. No persisted format changes.

**Observability:** the existing Test & Copy log remains the user-visible receipt. No new raw
metadata is logged.

**Status:** fixed 2026-07-26. Focused verification passed 25/25. The full WPF suite passed
127/127 with 0 failed and 0 skipped in both Debug and Release.

### Batch 2B: WPF repair transaction

**Backlog source:** R20.

**Exact files:** `CUETools.Wpf/Services/VerifyService.cs`, the smallest required Processor
repair seam, repair-specific tests, and user-facing repair copy.

**Why safe now:** the current WPF path fails before writing. The approved change must stage,
verify, back up, and replace; it must not enable an unguarded in-place rewrite.

**Behavior that must remain unchanged:** plain Verify remains read-only except configured
verification logs; nonrecoverable inputs remain untouched; classic WinForms repair behavior
is not changed by a WPF-only seam.

**Checks:** no-entry, recoverable, staged-success, verification-failure, replacement-failure,
rollback, cancellation, and source-unchanged tests; full WPF and Processor suites.

**Rollback:** restore the WPF repair service and remove only staging created by the new
transaction. Backups are never deleted during rollback.

**Observability:** log phase names and exception types only. Do not log album paths or audio
contents.

**Status:** complete on 2026-07-26. The WPF repair path now stages on the source
volume, requires an applied CTDB repair, independently decodes the candidate files,
and atomically publishes a sibling while leaving the source unchanged. Focused
transaction and post-rip routing tests pass, and a prior live K: damaged-disc run
proved the completed-rip route.

### Batch 2C: remove false WPF safety controls

**Backlog source:** R21.

**Exact files:** WPF Settings/Advanced views and view models, `DeadSwitchTests`, and affected
settings tests.

**Why safe now:** removing controls that perform no action prevents false promises. Shared
configuration fields and classic WinForms consumers remain intact.

**Behavior that must remain unchanged:** existing settings files continue to load; classic
applications keep their CTDB configuration; no new network call is introduced.

**Checks:** dead-switch analyzer, settings round trip, WPF build and full tests, source search
for classic CTDB consumers.

**Rollback:** restore the WPF rows and allowlist entries.

**Observability:** none.

**Status:** complete on 2026-07-26. Removed `NoUnverifiedOutput`, `FixOffset`,
`FixOffsetToNearest`, `CtdbSubmit`, and `CtdbAsk` from the modern WPF surface while retaining
their shared configuration compatibility. Focused tests pass 6/6; the full WPF suite passes
129/129 in both Debug and Release with zero skips.

## Post-restart assurance batch (2026-07-26)

**Approval:** the user explicitly authorized release-tooling, verification,
concurrency, optical-drive, native, installer, and external-integration work. This
batch remains bounded to the six findings from the final adversarial review.

### Final-output proof survives publication

**Exact files:** `CUETools.Codecs/LosslessPcmVerification.cs`,
`CUETools.Processor/CUESheet.cs`, `CUETools.Wpf/Services/RipService.cs`, and focused
assurance/integration tests under `CUETools.Wpf.Tests/`.

**Safety and unchanged behavior:** keep the existing PCM comparison and UI wording.
Add an immutable proof for the exact metadata-complete output set. Test & Copy and
held-output publication must revalidate that proof while copying under a source
lease. A transform that cannot carry the proof must clear the assurance claim.

**Checks:** clean and mutated multi-file transfers, missing/duplicate/escaped/extra
proof paths, held acceptance, writer race, all CUE output styles, HTOA, TagLib
rewrites, and decoder/finalization failures. Then run the focused and full WPF gates
plus the final H: optical pass.

**Rollback:** revert the proof model and publication integration together. Do not
leave the Boolean claim without its evidence.

**Observability:** content fingerprints remain in memory. Do not log or persist them.

**Status:** complete locally on 2026-07-26. Immutable exact-output proofs and the
destination-bound handoff cover metadata-complete multi-file publication, held
acceptance, writer races, crash recovery, and failure quarantine. The focused
publication/proof suite passes 33/33; the final hardware repeat remains a release
gate.

### Classic release ownership, recovery, and fresh inputs

**Exact files:** `eng/release/Collect-ClassicArtifacts.ps1`,
`eng/release/Test-ClassicArtifactCollection.ps1`, and narrowly scoped build-receipt
or recovery helpers/tests under `eng/release/`.

**Safety and unchanged behavior:** preserve exact 97-file collection and validation.
Add a cross-session lease, token-bound ownership/tree receipts, foreign-destination
refusal, journal-before-backup recovery, and exact-token cleanup. Collection must
also reject compiled inputs that do not match a current source/worktree,
configuration, platform, and toolchain receipt.

**Checks:** owned replacement, foreign preservation, lease contention, mismatched
tokens, injected failures at each move/rollback boundary, restart recovery,
same-version stale binaries, dirty-source receipts, and modified/missing inputs.
Run the release-safety umbrella and a fresh classic matrix before collection.

**Rollback:** retain any backup and journal whose recovery state cannot be proven.
Never delete a destination or stage by name resemblance.

**Observability:** receipts contain repository/toolchain identity and file hashes,
not user media or local secrets.

**Status:** complete locally on 2026-07-27. The exact release orchestrator passes
restore, Any CPU, x64, Win32, warning evaluation, receipt completion, 95-input
collection, notices, and transactional publication. A failed build exposed a
source-change recovery gap; an explicit stale-intent path now preserves the prior
intent bytes and is covered by the 86-check orchestrator harness.

### Bounded optical telemetry

**Exact files:** `CUETools.Wpf/Services/LevelMeteringRipper.cs`,
`CUETools.Wpf/Services/RipService.cs`, `CUETools.Wpf/ViewModels/RipViewModel.cs`,
`CUETools.Wpf/Controls/CodecScope.cs`, and focused mailbox tests.

**Safety and unchanged behavior:** move RMS/sample visualization into a preallocated
bounded SPSC mailbox created before the read starts. A slow UI drops presentation
windows only. It must not block or alter optical reads, encoded PCM, checksums, or
progress.

**Checks:** zero producer-thread allocation after warm-up, stalled-consumer bounds,
slot lifetime, order/scaling, slot reuse, and queue-full behavior. Then run the WPF
gate and live optical integration.

**Rollback:** restore the callback path as one unit. No persisted data changes.

**Observability:** only bounded RMS/sample presentation data crosses the mailbox.

**Status:** complete locally on 2026-07-26. The producer uses a preallocated bounded
SPSC mailbox, drops presentation-only windows under pressure, and clears stale
scope state on reset. Focused tests cover allocation, byte decoding, slot lifetime,
ordering, reuse, concurrency, and stalled/full consumers. The final hardware repeat
remains a release gate.

### FLACCL verification capability uses exact runtime identity

**Exact files:** `CUETools.Processor/CUEProcessorPlugins.cs`,
`CUETools.Wpf/Services/OutputVerificationAssurance.cs`, and
`CUETools.Wpf.Tests/OutputVerificationTypeTrustTests.cs`.

**Safety and unchanged behavior:** manifest-approved packaged FLACCL keeps its
verification switch. Local-development and user-plugin types do not borrow the
claim. The package loader registers the exact runtime `Type`; matching assembly and
type names alone remain untrusted.

**Checks:** two runtime types with identical assembly/type names, only one carrying
the capability; packaged FLACCL acceptance; subclass/unrelated rejection; full
plugin and WPF trust gates.

**Rollback:** revert the capability registry and evaluator together.

**Observability:** the capability is runtime-only and contains no path or hash.

**Status:** complete locally on 2026-07-26. Manifest-approved registration enrolls
the exact runtime settings type. Identically named types from another assembly or
load context, subclasses, unrelated types, and malformed verification properties
are rejected by focused tests and the canonical WPF gate.

### Vendor patches build from an ignored source stage

**Exact files:** `eng/ci/Prepare-VendorSources.ps1`,
`eng/ci/Build-NativeDependencies.ps1`, its focused preparation tests,
`eng/release/New-ClassicBuildReceipt.ps1` and receipt tests, `CUETools.sln`,
the five direct TagLibSharp/WindowsMediaLib project consumers, `README.md`,
the three Windows workflows, and the S13/R51 review records.

**Safety and unchanged behavior:** preserve the four pinned gitlink commits, the
four checked patch bytes, project GUIDs, managed APIs, native output paths, warning
budgets, and artifact membership. Materialize the exact patched source closure under
ignored `obj/vendor-sources/current` and make every patched-source consumer use that
closure. Preparation must refuse dirty or mismatched submodules and must leave their
before/after status identical.

**Checks:** clean and repeated preparation, pinned-commit and patch-hash binding,
tampered-stage replacement, dirty-submodule refusal, patch applicability, managed
WPF and classic builds, both native architectures, classic receipt mutation
rejection, workflow static checks, and recursive clean-submodule assertions.

**Rollback:** restore direct submodule project paths and in-place patch commands.
The generated stage is ignored and may be removed only after its ownership manifest
and containment checks succeed. Checked patch files and gitlinks remain unchanged.

**Observability:** stage receipts contain repository-relative paths, commit hashes,
patch hashes, file counts, and aggregate source hashes. They contain no user media,
credentials, or machine-specific absolute paths.

**Status:** complete on 2026-07-27. The identity-bound stage contains 1,549 source
files and is reused idempotently. The staging harness passes 15 checks, native
preparation passes 21, all real consumer scans are clean, and five initialized
submodules remain unchanged after managed, native, and classic builds.

### CTDB repair preserves source names and metadata

**Exact files:** `CUETools.Wpf/Services/VerifyService.cs`,
`CUETools.Wpf/Services/RepairTransaction.cs`, focused WPF repair tests, the opt-in
live CTDB probe, and the R52 review record.

**Safety and unchanged behavior:** keep the source set read-only and retain the
owned sibling-stage/atomic-publication transaction. Change only the private repair
configuration: use source basenames for repaired FLAC outputs and force standard
tags, representable custom tags, and embedded artwork to be copied. Keep verify-time
tag/log writes, sidecar extraction, M3U creation, and arbitrary user filename
templates disabled. Continue decoding the final files after TagLib saves and before
publication.

**Checks:** isolated-config mutation tests; real managed-FLAC track and image
transcodes with punctuation-bearing names, basic/custom tags, and artwork; existing
escape, rollback, collision, and source-immutability tests; then the opt-in live
damaged CTDB repair probe.

**Rollback:** restore the repair-only filename and copy flags. No source file or
already-published repair directory is modified by rollback.

**Observability:** progress and result messages expose only the already-redacted
repair directory. Tests compare metadata in temporary fixtures without logging tag
contents or user paths.

**Status:** complete locally on 2026-07-27. Real managed-FLAC track-set and
disc-image tests preserve punctuation-bearing basenames, source-authoritative
standard tags, custom Xiph fields, exact CDTOC values, and embedded artwork while
proving the source bytes unchanged. Stale AccurateRip/CTDB proof tags are
deliberately absent from repaired payloads. Focused repair tests pass 14/14. The
opt-in live K: damaged-disc check remains external evidence, not an inferred pass.

### First-use cache calibration, overread, and named CRC evidence

**Exact files:** `CUETools.Ripper.SCSI/SCSIDrive.cs`,
`CUETools.Wpf/Accuracy/DriveCalibrationService.cs`,
`CUETools.Wpf/Services/RipService.cs`, Rip/Drive view models and views, persistence
models, and focused calibration/history/Test & Copy tests.

**Safety and unchanged behavior:** calibrate before the first read that depends on
the drive record. Require a confirmed independent-reread strategy for Secure and
Paranoid. Retain the largest proven flush across timing noise and require every
flush read to complete. Probe lead-in/out with the same read command and only the
offset-sized one-sector range; retain zero-padding when the drive rejects an edge.
Store Test and Copy as named CRC roles without changing the existing agreement
oracle or labeling the third confirmation as Copy.

**Checks:** cache-policy tests for smaller and apparently uncached later probes;
history persistence across roles/restarts/drives; third-read labeling; net8 and
net47 SCSI builds; full WPF tests; isolated live H: 25-window Paranoid
cache-defeat run; final damaged K: Test & Copy and lead-out pass after device reset.

**Rollback:** revert the calibration schema version and reader edge-range changes
together. A partial rollback could trust new flags without consuming them or consume
unprobed flags.

**Observability:** calibration logs the measured strategy, size, offset edge
capabilities, timing, and scrubbed exception/SCSI evidence. CRC evidence contains
audio checksums and disc identity, not user paths or tags.

**Status:** implemented and deterministic-test verified on 2026-07-27. H: passed
25 Paranoid cache-defeat windows twice; the final-source run used the real read
offset, consumed the end-of-disc path, and passed in 2 minutes 53 seconds. The WPF
suite passes 347/347, the ripper suites pass net8 8/8 and net47 17/17, and the SCSI
project builds for net8, net47, and net20 under its required full-MSBuild host. K:
remains an explicit hardware evidence gap until Windows releases its wedged device
handle.
