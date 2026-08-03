# Safe-Fix Plan

**Current authority, 2026-07-30:** the wave below has been executed through its
local and selected hosted gates. Individual batch notes preserve intermediate
counts and plans, but the final status/counts are in the live release evidence,
remediation backlog, and final canonical gate rather than this historical plan.
Optical-drive reads, an
H: full FLAC rip and same-drive Test & Copy, CTDB repair, WMA Lossless, FLACCL/OpenCL,
Icecast 2.5.0, and local actionlint now have direct evidence. The local classic
matrix is green: AnyCPU 58/0/11, and x64 and Win32 9/0/60,
TTA in both architectures, and Installer Projects 8/0 with a 929,792-byte MSI.
The local and hosted classic receipts and exact 97-file collection pass. Hosted
classic/WPF/FFmpeg/release evidence is source-bound and annotation-clean.
Public-trust signing identity provisioning, CTDB TLS, and named residual
hardware/service failure cases remain pending.
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

**Status:** landed 2026-07-02. Local verification matched the then-known-green
baseline exactly: TestParity 18 passed / 4 skipped, TestCodecs 34 passed / 1
skipped, TestProcessor builds. Superseding hosted run `30518472651` passes the
expanded classic selection with zero failures and seven declared skips.

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
project builds for net8, net47, and net20 under its required full-MSBuild host.
After an elevated device restart and tray cycle, K: reopened and began a real
Paranoid Test & Copy. That run exposed the distinct payload medium-error gap tracked
below instead of a calibration failure.

## Damaged-disc completion and narrow-window access batch (2026-07-27)

### Represent payload medium errors as unreadable sectors

**Exact files:** `CUETools.Ripper.SCSI/SCSIDrive.cs`, a focused pure failure-policy
helper under `CUETools.Ripper.SCSI/`, `CUETools.Ripper.Tests/`, and the R55 review
record.

**Safety and unchanged behavior:** preserve fatal handling for transport failures,
device removal, not-ready, unit-attention, illegal commands, and hardware errors.
When a READ CD payload command reports a medium error, retry its batch one sector
at a time. A sector that still reports a medium error contributes flagged evidence
to the existing secure vote and failed-sector map. It may continue only under the
existing retry and Stop-on-unrecoverable policy. Do not turn a failed command into
trusted audio.

**Checks:** pure classification tests for medium, hardware, not-ready, ioctl, and
legacy `64/00` behavior; net8/net47/net20 SCSI builds; modern ripper tests; full WPF
tests; and a repeat on the damaged K: disc with Stop-on-unrecoverable off.

**Rollback:** revert the classification helper and `FetchSectors` integration
together. The old behavior aborts the whole read on any payload medium error.

**Observability:** retain the existing bounded recovery and failed-window logs.
Do not log sector payload bytes or user metadata.

**Status:** implemented and deterministic-test verified on 2026-07-27. The failure
policy classifies only device-reported medium errors as damaged-media evidence,
preserves fatal handling for every tested non-media class, and retains the legacy
`64/00` split behavior. The modern ripper suite passes 12/12, the full WPF suite
passes 352/352, and the SCSI project builds for net8, net47, and net20 under its
required full-MSBuild host. A repeat on the damaged K: disc remains the hardware
check that must prove `NO SEEK COMPLETE` becomes failed-sector evidence rather than
aborting the transaction.

### Keep the Rip page reachable at narrow widths

**Exact files:** `CUETools.Wpf/Views/RipView.xaml`, a focused layout-contract test
under `CUETools.Wpf.Tests/`, and the R56 review record.

**Safety and unchanged behavior:** preserve commands, bindings, settings, evidence,
and wide-screen visual order. Replace fixed side-rail allocation with bounded
proportional widths, let action controls wrap, let side rails scroll vertically,
trim long headings with their full text in a tooltip, and provide a horizontal
fail-safe only below the supported work-area minimum. Keep Deep recovery default-on
and durable, but move its expert opt-out to Settings instead of presenting it as a
per-rip action.

**Checks:** XAML contract assertions for the scroll and wrap safeguards; full WPF
tests; build and publish; then inspect the Rip page at 1784, 1200, and 1024 logical
pixels with a loaded disc and with a completed/failed job.

**Rollback:** revert the Rip-page XAML as one unit. No persisted setting or output
format changes.

**Observability:** none. This is presentation-only and does not add telemetry.

**Status:** implemented and deterministic-test verified on 2026-07-27. The XAML
contract binds each inner work grid to its viewport so ScrollViewer measurement
cannot make proportional columns expand without bound. It also requires bounded
proportional rails, vertical rail scrolling, wrapped actions, and tooltip-backed
text trimming. The full WPF suite passes 352/352. A 1200-pixel loaded-disc capture
proved the primary controls and CRC columns remain reachable after the viewport
fix. Final-source 1784-, 1200-, and 1024-pixel captures remain the presentation
check after publication.

### Settle accepted drive-control transitions before payload reads

**Exact files:** `CUETools.Ripper.SCSI/SCSIDrive.cs`,
`CUETools.Ripper.SCSI/PayloadReadFailurePolicy.cs`,
`CUETools.Ripper.Tests/PayloadReadFailurePolicyTests.cs`,
`CUETools.Wpf/Services/RipService.cs`, and the R57 review record.

**Safety and unchanged behavior:** keep SET CD SPEED serialized on the read thread
at a fresh-window boundary. After the drive accepts the transition, wait a bounded
40 ms before the first payload. Retry once after another bounded 80 ms only when
that first payload reports the observed `DeviceFailed`, `IllegalRequest`, ASC/ASCQ
`24/00` state. Clear the transition latch after the first payload. A repeated
`24/00`, a `24/00` without a pending transition, every other illegal request, and
all device/transport failures remain fatal.

**Checks:** pure transition-policy positives and negatives; modern ripper tests;
net8/net47/net20 SCSI builds; full WPF tests; then repeat K: Test & Copy beyond the
former 18-28 second failure window and retain payload context if it still fails.

**Rollback:** revert the transition latch, settle, classifier, and added exception
context together. Do not retain a broad command retry without its transition gate.

**Observability:** payload SCSI failures add relative sector, transfer count, read
command, applied speed, and pending speed/cache transition flags. These are
device/read facts; no sector bytes or user metadata are logged. A completed phase
also records the count of transition-bound retries.

**Status:** implemented and deterministic-test verified on 2026-07-27. The modern
ripper suite passes 14/14, the full WPF suite passes 352/352, and the SCSI project
builds for net8, net47, and net20 under its required full-MSBuild host. K: later
completed Test and advanced Copy to relative sector 283328 without another
transition-bound failure. The nested pinpoint failure there is tracked under R58.

### Preserve and recover a corroborated nested pinpoint failure

**Exact files:** `CUETools.Ripper.SCSI/SCSIDrive.cs`,
`CUETools.Ripper.SCSI/PayloadReadFailurePolicy.cs`,
`CUETools.Ripper.Tests/PayloadReadFailurePolicyTests.cs`,
`CUETools.Wpf/Services/RipService.cs`, and the R58 review record.

**Safety and unchanged behavior:** retain the exact multi-sector `24/00`
decomposition rule, which accepts only independently successful child payloads.
For the separately observed path, first require a parent multi-sector medium error.
Snapshot every child failure with its exact sector and sense before another command
can overwrite device state. Retry a child `24/00` once after 80 ms. Consume only a
successful retry. A repeated identical `24/00`, or a medium error on retry, marks
only that sector untrusted for the existing vote and CTDB path. A different command,
transport, readiness, removal, or hardware failure remains fatal.

**Checks:** pure batch-policy positives and negatives; modern ripper tests; full
WPF tests; net8/net47/net20 SCSI builds; opt-in reads at relative sector 283328 and
the 283200 window boundary at 4224 kB/s; then repeat K: Test & Copy beyond relative
sector 283328.

**Rollback:** revert the policy method, isolated fallback branch, counter, and log
field together. Do not merge this with the medium-error split, whose trust
semantics deliberately differ.

**Observability:** each child failure carries its exact relative sector, transfer
count of one, parent batch location/count/sense, command, applied speed, and
transition flags without audio bytes. Completed phases record
`payload_batch_fallbacks`, `pinpoint_retries`, and
`corroborated_unreadable_pinpoints`.

**Status:** implemented and bounded-test verified on 2026-07-27. The modern ripper
policy suite passes 18/18, the full WPF suite passes 352/352, and SCSI builds pass
for net8/net47/net20. The bounded K: probes pass at the exact relative sector and
at the real window boundary/speed. The end-to-end rerun remains.

### Remove obsolete legacy resource-build warnings

**Exact files:** `Bwg.Scsi/Bwg.Scsi.csproj`,
`CUETools.Ripper.SCSI/CUETools.Ripper.SCSI.csproj`,
`CUEControls/CUEControls.csproj`,
`CUETools.Codecs.Flake/CUETools.Codecs.Flake.csproj`,
`CUETools.Codecs.WMA/CUETools.Codecs.WMA.csproj`,
`CUETools.Ripper.SCSI/SCSIDrive.cs`,
`eng/ci/warning-baseline.json`, and the R60 review record.

**Safety and unchanged behavior:** disable source-revision suffixes only for the
`net20` target consumed by the legacy Assembly Linker, which accepts numeric
product versions only. Preserve the declared assembly version, current-target
informational versions, and release provenance carried by receipts and hashes.
Remove the `cdtext` field only after proving it has no live writes or reads.
Do not add a source workaround for MSB3088: that message was a stale resource
cache produced by invoking the unsupported `dotnet` build host for `net20`, and a
full-MSBuild rebuild clears it.

**Checks:** scan every `net20` project for source resources, rebuild all five
resource-bearing projects through the installed full Visual Studio MSBuild host
without command-line overrides, and require zero AL1053 warnings. Build the SCSI
project for net47 and net8; run the modern ripper and WPF suites; run the checked
warning gate; and confirm all staged vendor worktrees remain clean.

**Rollback:** restore the five target-local metadata properties, the dead field,
and its baseline fingerprint together. No persisted setting, SCSI command, audio
data, or release artifact format changes.

**Observability:** no runtime telemetry changes. Build receipts continue to carry
the source identity; the old `net20` satellite assembly receives the same numeric
product version as its parent assembly.

**Status:** implemented and verified on 2026-07-27. Of 36 `net20` projects, five
contain source resources and all five rebuild through full Visual Studio MSBuild
with zero AL1053 warnings. The SCSI net47/net8 builds are also clean. The ripper
suite passes 20/20, the WPF suite passes 358/358, and the checked modern build now
emits zero warnings against an empty baseline. All staged vendor worktrees remain
clean.

### Remove obsolete managed-codec warning paths

**Exact files:** `CUETools.Codecs.ALAC/ALACSubframe.cs`,
`CUETools.Codecs.ALAC/RiceContext.cs`,
`CUETools.Codecs.ALAC/ALACWriter.cs`,
`CUETools.Codecs.Flake/AudioEncoder.cs`,
`CUETools.Codecs.libFLAC/libFLAC.cs`,
`CUETools.Compression.Zip/SeekableZipStream.cs`,
`CUEControls/MediaSlider.cs`, and the R61 review record.

**Safety and unchanged behavior:** remove ALAC's `cbits` and `porder` only after
searching all 13 project C# files and finding declarations but no live reads or
writes. Collapse ALAC's unconditional one-iteration window block without changing
the executed statements. Remove Flake's never-assigned `sr_code1` branch while
retaining its existing rejection of sample rates outside the FLAC table. Remove
no public ZIP member: retain the compatibility-only stream password event while
documenting that the owning provider must request the password before opening an
encrypted stream. Do not delete or fake-write libFLAC frame fields: native
libFLAC populates them through the decoder callback, so suppress CS0649 only
around that documented interop packet.
Retain the legacy media slider's public `Dispose()` member and mark its inherited
member hiding explicitly; disposal behavior and binary surface stay unchanged.

**Checks:** build each touched codec for its modern target; run ALAC and FLAC
verify-on-encode tests plus the full WPF suite; run the warning gate; publish and
probe the native plugin contract.

**Rollback:** revert each codec-local cleanup with its matching warning-baseline
fingerprint. The libFLAC interop layout and field visibility must remain unchanged.

**Observability:** none. No logging, output format, encoder setting, or persisted
contract changes.

**Status:** implemented and verified on 2026-07-27. Touched codec projects build
warning-free for their applicable netstandard2.0, net47, and net20 targets.
The public ZIP event and native-owned libFLAC packet remain intact. The full WPF
suite passes 358/358, and the checked modern build emits zero warnings.

### Make the modern WPF null contracts explicit

**Exact files:** `CUETools.Wpf/Accuracy/DriveCalibrationService.cs`,
`CUETools.Wpf/Accuracy/TestAndCopyLog.cs`,
`CUETools.Wpf/Accuracy/TestAndCopyResolver.cs`,
`CUETools.Wpf/Accuracy/VerifyHistory.cs`, `CUETools.Wpf/App.xaml.cs`,
`CUETools.Wpf/Controls/DiscTray.cs`,
`CUETools.Wpf/Converters/BoolToBrushConverter.cs`,
`CUETools.Wpf/Models/DriveDetails.cs`,
`CUETools.Wpf/Services/DriveService.cs`,
`CUETools.Wpf/Services/NamingContextMapper.cs`,
`CUETools.Wpf/Services/NamingEngine.cs`,
`CUETools.Wpf/Services/OutputLayout.cs`,
`CUETools.Wpf/Services/RipService.cs`,
`CUETools.Wpf.Tests/GzJsonTests.cs`, `eng/ci/warning-baseline.json`, and the R62
review record.

**Safety and unchanged behavior:** annotate optional returns and corrupt persisted
values as nullable instead of hiding them. Preserve existing empty-string display
fallbacks. Add explicit TOC and metadata invariant failures at points that already
failed by null dereference. Test & Copy must reject a completed phase that lacks
its required checksum record before building the comparison list. Mark
`IsCurrent` as proving a non-null calibration so the compiler follows the same
gate the runtime already uses.

**Checks:** focused naming, calibration, persistence, history, Test & Copy, layout,
and codec tests; the full WPF suite; a no-incremental WPF and fuzz build with zero
warnings; an empty checked warning baseline; clean publish and artifact contract;
and clean staged vendor worktrees.

**Rollback:** revert nullable annotations and guards by subsystem. Restore the
warning fingerprints only if a warning is deliberately accepted with new evidence.

**Observability:** no new values are logged. New missing-record failures use a
phase-only message and contain no paths, titles, checksums, or sector payloads.

**Status:** implemented and verified on 2026-07-27. The 34 baseline fingerprints
expanded to 51 source locations. The WPF and fuzz no-incremental builds now emit
zero warnings, the checked baseline is empty, and the full WPF suite passes
358/358. Test & Copy now stops at the phase boundary if checksum evidence is
missing.

### Close the separately discovered classic managed warning set

**Exact files:** `ProgressODoom/HSV.cs`,
`ProgressODoom/MetalProgressPainter.cs`,
`CUETools.Codecs.CoreAudio/WasapiOut.cs`,
`CUETools.DSP.Resampler/Internal/rate_t.cs`,
`CUETools.Codecs.LossyWAV/analysis_rec.cs`,
`CUETools.Codecs.FLACCL/FLACCLWriter.cs`,
`CUETools.FLACCL.cmd/Program.cs`, `CUETools.ARCUE/CUETools.ARCUE.csproj`,
`CUETools.eac3to/CUETools.eac3to.csproj`,
`CUETools.Converter/CUETools.Converter.csproj`, `Directory.Build.targets`,
and the R63 review record.

**Safety and unchanged behavior:** add the equality members required by HSV's
existing operators and mark intentional member hiding. Remove only private or
internal fields and locals with no live write or read. Preserve FLACCL's mapped
task layout because OpenCL kernels populate those fields, and suppress CS0649
only around that packet. Keep the advertised `--ignore-chunk-sizes` option and
apply it to file input as documented. Replace unsupported netcoreapp2.0 targets
with net8.0 while retaining net47. Suppress only the pinned OpenCLNet project's
known compatibility warnings: its OpenCL 1.1 fallback calls and unimplemented
DirectX 9 extension.

**Checks:** rebuild the affected projects, exercise FLACCL with an available
OpenCL device, run the codec and WPF suites, and rebuild the full classic solution
through Visual Studio with zero managed warnings.

**Rollback:** revert by subsystem. Do not remove FLACCL mapped task fields or
rewrite OpenCL calls without cross-vendor device evidence.

**Observability:** no new runtime logging. The FLACCL command now honors an
existing option that its help text already promised.

**Status:** implemented and verified on 2026-07-27. Release Any CPU rebuilt 58
projects with zero managed warnings and no failures; Release x64 and Win32 each
rebuilt their nine selected projects with zero managed warnings and no failures.
The native warning baseline remains separate and unchanged. FLACCL verify passed
on the NVIDIA RTX 3060 through OpenCL 3.0 with the repaired option enabled.
The net47 codec suite passes 112/113 with its one pre-existing skip, the WPF suite
passes 358/358, and all three new net8 command-line outputs start with their
dependency closures present.

### Remove the checked native libFLAC warning set

**Exact files:** `ThirdParty/submodule_flac_CUETools.patch`,
`eng/ci/native-warning-baseline.json`, `eng/ci/VendorSourceStaging.ps1`,
`docs/review/remediation-backlog.md`, `docs/review/safe-fix-plan.md`, and the
R64 review record. The vendor patch payload changes `bitwriter.c`, `fixed.c`,
`format.c`, `lpc.c`, `metadata_object.c`, `stream_decoder.c`,
`stream_encoder.c`, `libFLAC++/metadata.cpp`, and `share/getopt/getopt.c` in
the staged libFLAC 1.5.0 source. The `ThirdParty/flac` gitlink stays immutable.

**Safety and unchanged behavior:** take the bit-writer capacity calculation from
current upstream libFLAC, where capacity is compared in words and the old
byte-size multiplication cannot overflow. Use 64-bit shift operands for the two
24-bit metadata limits. Add explicit residual casts only where the encoder first
proves the selected fixed predictor or LPC residual fits `FLAC__int32`; retain
the existing limit-residual rejection paths. Constant and warm-up samples narrow
only under their existing `bps <= 32` branches. Retain a missing-frame gap in
64 bits through the existing five-second and 50-frame caps, then narrow after
the cap proves the value fits. Check 33-bit channel decorrelation results before
placing them in the 32-bit output buffer so malformed frames cannot wrap before
the existing bounds check. Make the apodization default a `FLAC__real` value
without changing its value. Read the UTF-8 vendor ownership manifest explicitly
as UTF-8 so Windows PowerShell 5.1 preserves non-ASCII upstream paths.
Let the Windows CRT own its `getenv` declaration, and include the CRT math
definitions before libFLAC's fallback constants in the C++ metadata target.

**Checks:** rebuild staged libFLAC for Win32 and x64 with the checked native
warning gate; require an empty native baseline; run native-backed FLAC encode,
decode, and verify-on-encode tests; run the upstream libFLAC tests available on
Windows; and finish with the vendor-clean gate. Refresh classic and publish
receipts only after the active rip releases the running application.

**Rollback:** revert the R64 vendor patch hunks and restore the eight native
warning fingerprints together. Do not replace the fixes with project-wide
warning suppression.

**Observability:** malformed 33-bit decorrelation that reconstructs outside the
declared 32-bit sample range now reports the existing out-of-bounds decoder
status. Missing-frame silence remains capped by the existing policy, but large
gaps can no longer wrap below that cap. No audio samples, metadata values, or
logs change for valid input. Vendor staging now accepts the same manifest under
Windows PowerShell 5.1 and PowerShell 7.

**Status:** implemented and locally verified on 2026-07-27. All six native
dependency builds complete with zero warnings against an empty baseline. The
focused native-backed FLAC tests pass 25/25. The clean upstream CMake test build
emits zero warnings, and its libFLAC and libFLAC++ tests pass 2/2. The classic
codec suite passes 112/113 with its established skip, and the WPF suite passes
358/358. Vendor staging passes 15 checks, reproduces the same patched source
under PowerShell 5.1 and 7, and leaves all five submodules clean. The source-bound
classic receipt is now retained by release run `30518479906`; its downloaded
97-file payload and clean-source receipt passed independent validation.

### Keep Visual Studio rulesets out of Core MSBuild

**Exact files:** `Directory.Build.targets`,
`docs/review/remediation-backlog.md`, and `docs/review/safe-fix-plan.md`.

**Safety and unchanged behavior:** clear `AllRules.ruleset` only when Core
MSBuild is running and the project does not contain that file. Core MSBuild
cannot resolve Visual Studio's installed ruleset directory and currently emits
MSB3884 without applying the rules. Full Visual Studio MSBuild retains the
existing ruleset value and analysis behavior.

**Checks:** rebuild `CUETools.TestHelpers` through the net47 codec suite and
require no MSB3884. Rebuild the classic solution through full Visual Studio and
confirm its warning count remains zero.

**Rollback:** remove the Core-only property override. Do not delete the project
ruleset declarations while full Visual Studio still resolves them.

**Observability:** none. This changes build configuration only.

**Status:** implemented and locally verified on 2026-07-27.
`CUETools.TestHelpers` rebuilds through Core MSBuild with zero warnings. The
full Visual Studio receipt check remains paired with the post-rip classic
release run.

### Decompose only the observed cache-defeat command-shape rejection

**Exact files:** `CUETools.Ripper.SCSI/PayloadReadFailurePolicy.cs`,
`CUETools.Ripper.SCSI/SCSIDrive.cs`,
`CUETools.Ripper.Tests/PayloadReadFailurePolicyTests.cs`,
`CUETools.Wpf/Services/RipService.cs`,
`CUETools.Wpf.Tests/LiveOpticalTestCopyIntegrationTests.cs`, steering, and the
R69 review record.

**Safety and unchanged behavior:** retain strict cache independence. Try the
normal measured transfer shape and all unrelated regions first. Only exact
`DeviceFailed/IllegalRequest/24/00` can reduce the chunk through 8/4/2/1
sectors. Preserve the required sector count, audio-program bounds, scratch-only
destination, and complete-or-fail result. Never use a rejected payload.

**Checks:** run the classifier/ripper suite, all SCSI targets, the full WPF
suite, and a K: CQ2/deep-recovery probe at the observed damaged window with the
measured 786,432-byte eviction volume. Repeat full Test & Copy only after the
bounded probe passes.

**Rollback:** remove the chunk ladder and its diagnostic counter together. Do
not replace the strict failure with a warning or unverified continuation.

**Observability:** completion logs count chunk fallbacks. A terminal failure
reports the final chunk shape, exact sector, status, sense, ASC/ASCQ, attempted
regions, transient retry count, and fallback count.

**Status:** implemented and software verified on 2026-07-27. Ripper tests pass
22/22. The 2026-07-29 K: repeat reached 92 percent of Copy, then rejected all
three regions at 16/8/4/2/1 sectors. The receipt exposed a narrower retry-scope
defect: the first rejected command consumed the one retry for the entire eviction,
so the remaining fourteen address/shape combinations received no settle or retry.

### Scope cache-defeat recovery to each exact SCSI command

**Exact files:** `CUETools.Ripper.SCSI/PayloadReadFailurePolicy.cs`,
`CUETools.Ripper.SCSI/SCSIDrive.cs`,
`CUETools.Ripper.Tests/PayloadReadFailurePolicyTests.cs`,
`CUETools.Wpf.Tests/LiveOpticalTestCopyIntegrationTests.cs`, steering, and the
R69 review record.

**Safety and unchanged behavior:** retain strict cache independence, the three
unrelated regions, and the 16/8/4/2/1 ladder. Give each exact LBA/sector-count
command at most one 80 ms retry only for
`DeviceFailed/IllegalRequest/24/00`. Do not share that consumed retry with a
different address or transfer shape. Every other failure remains fatal, rejected
payload is never used, and the required eviction byte count must still complete.
Calculate that count with widened arithmetic so even a corrupted maximum `int`
setting either completes the requested eviction or fails for lack of disc space.
After a completed eviction, retry the first target payload only for the same
exact `24/00` transition signature, not every `SCSIException`. Treat a stored
flush strategy as independent-read proof only when the shared parser accepts a
positive byte count; a malformed `Flush:` value must fail the first-read gate.

**Checks:** add deterministic first/repeat/unrelated-failure, maximum-size
arithmetic, transition-filter, and malformed-calibration policy tests; run the
ripper suite and all SCSI target builds, run the full WPF suite, then repeat the
configured K: damaged-window probe and full Test & Copy.

**Rollback:** revert the per-command retry scope, checked-width calculation,
shared calibration gate, transition filter, and their diagnostics together. Do
not weaken the complete-or-fail eviction rule.

**Observability:** retain the aggregate retry and chunk-fallback counters. A
terminal failure also reports the read opcode, main-channel/C2 mode, applied
speed, current target window, and the one-retry-per-command policy.

**Status:** in progress 2026-07-29. The source-bound K: Test pass reached the
damaged zone and then failed after 1,123 seconds. All fifteen address/shape
commands received their command-local retry (`transient-retries=15`), proving
the scope correction is active, but the firmware rejected every retry with
exact `24/00`. After the application released the device, Windows reported no
media and a new raw SCSI handle could not open K:. This corroborates the user's
independent observation that the ASUS drive becomes dormant after a failed
read.

### Wake a dormant drive once without weakening cache independence

**Exact files:** `CUETools.Ripper.SCSI/PayloadReadFailurePolicy.cs`,
`CUETools.Ripper.SCSI/SCSIDrive.cs`,
`CUETools.Ripper.Tests/PayloadReadFailurePolicyTests.cs`,
`CUETools.Wpf/Services/RipService.cs`,
`CUETools.Wpf.Tests/LiveOpticalTestCopyIntegrationTests.cs`, steering, and the
R69 review record.

**Safety and unchanged behavior:** only after every usable unrelated region and
every 16/8/4/2/1 shape has failed after its one retry with exact
`DeviceFailed/IllegalRequest/24/00`, issue one non-immediate `START UNIT` on the
already-open device handle. Do not load or eject the tray. Require both command
success and `TEST UNIT READY`, then repeat the full eviction from the measured
shape. The successful reread still requires every requested scratch sector; no
rejected bytes enter the vote or output. A second exhaustion or any different
status/sense remains fatal.

**Checks:** pure one-wake/exhaustion policy positives and negatives; ripper and
WPF suites; all SCSI targets; warning and artifact gates; a loaded K: bounded
window; then a source-bound full Paranoid Test & Copy through the prior
`current-window=278400-280800` failure.

**Rollback:** revert the wake policy, drive command, counter, and diagnostics
together. Retain the per-command retry scope, checked-width calculation, shared
calibration gate, and strict complete-or-fail eviction.

**Observability:** terminal and completion records count wake attempts
separately from command retries and chunk fallbacks. The wake helper preserves
command status and readiness without logging disc identity or payload.

**Status:** in progress 2026-07-29. A second source-bound Test & Copy completed
Test and reached the prior Copy boundary. The exact ladder exhausted, `START
UNIT` succeeded, and the immediate `TEST UNIT READY` returned
`DeviceFailed/IllegalRequest/24/00`. The application failed closed with
`wake-attempts=1`. The wake command is accepted, but readiness has its own
observed control-transition window.

### Settle and retry the exact post-wake readiness transition once

**Exact files:** the same R69 wake slice.

**Safety and unchanged behavior:** after a successful non-immediate `START
UNIT`, settle for 250 ms before querying readiness. Retry `TEST UNIT READY` at
most once after another 250 ms only for
`DeviceFailed/IllegalRequest/24/00`. Do not retry not-ready, unit-attention,
transport, removal, hardware, medium, or any different failure. Readiness still
does not satisfy cache independence: the entire measured eviction must succeed
afterward, and a repeat readiness rejection remains fatal.

**Checks:** deterministic first/repeat/unrelated readiness-transition tests,
all prior software/build/artifact gates, a loaded K: `START UNIT` plus exact
window, and another uninterrupted source-bound Test & Copy.

**Rollback:** remove the two readiness settles, exact classifier, counter, and
diagnostics together; retain the one-wake and complete-eviction gates.

**Observability:** count readiness-transition retries separately from wake
attempts and cache READ CD retries.

**Status:** in progress 2026-07-29.

The source-bound run disproved the final assumption in this batch. Test reached
92 percent after 946 seconds, `START UNIT` succeeded, and both readiness
attempts returned exact `DeviceFailed/IllegalRequest/24/00`, including the
settled retry. CUETools failed closed with
`wake-readiness-retries=1`. Windows then reported K: with no loaded media.
Another delay-only retry is not supported by this evidence.

### Use the complete eviction as proof after indeterminate wake readiness

**Exact files:** the same R69 wake slice.

**Safety and unchanged behavior:** `TEST UNIT READY` is advisory; it does not
prove either media access or cache independence. After a successful
non-immediate `START UNIT` and exactly two bounded readiness results of
`DeviceFailed/IllegalRequest/24/00`, classify readiness as indeterminate and
attempt the already-bounded complete eviction once. Do not continue after a
not-ready, unit-attention, transport, removal, hardware, medium, or different
failure. Do not consume rejected scratch data. Only every requested eviction
sector succeeding can authorize the secure reread; another exact exhaustion
still fails closed.

**Checks:** deterministic first/second/unrelated readiness classifiers; ripper
and WPF suites; net8, net47, and full-MSBuild net20 SCSI builds; zero-warning
gate; production publish and plugin contract; then another uninterrupted
source-bound K: Test & Copy through the same dormant transition.

**Rollback:** remove the indeterminate-readiness classifier, counter, and
continuation together. Retain the one-wake limit, exact readiness retry,
per-command cache-read budgets, and complete-or-fail eviction.

**Observability:** count indeterminate readiness separately from wake attempts,
readiness retries, cache-command retries, and chunk fallbacks. A terminal
failure retains all five local counts.

**Status:** implemented and end-to-end hardware-verified 2026-07-29 at commit
`5fa2c65`. The uninterrupted K: Test & Copy finished in 2,275 seconds, produced
two consistent reads, verified the final encoded PCM, and crossed the former
92-percent Copy failure boundary. CTDB then repaired six sectors in a
source-preserving sibling. Independent decode comparison found 86 changed
channel samples in 67 stereo frames, exactly matching the repair receipt, and
the repaired result matched AccurateRip at 55/82 and CTDB at 207/234.

This successful run did not enter the dormant-drive branch. Wake attempts,
readiness retries, indeterminate-readiness continuations, command retries, and
chunk fallbacks all remained zero. The observed end-to-end blocker is cleared,
but exact hardware activation of the new wake branch remains an unknown rather
than an inferred pass.

### Give human-facing album sidecars a portable identity

**Exact files:** `CUETools.Wpf/Services/AlbumArtifactNames.cs`,
`RipService.cs`, `ConvertService.cs`, `RepairTransaction.cs`, `OutputGuard.cs`,
their focused tests, steering, the anti-dark-code remediation reference, and
the R70 review record.

**Safety and unchanged behavior:** derive one sanitized, 180-character maximum
artist/album/year/disc stem. Use it for cue, rip, AccurateRip, and Test & Copy
logs. Keep transaction ownership, completion, proof, and `rip.verify` names
stable. Accept legacy `album.cue`, require exactly one top-level cue for repair,
and detect old or new human sidecars by file type for overwrite protection.

**Checks:** run naming, conversion publication, proof transfer, overwrite,
legacy repair, named repair, ambiguous-cue, and full WPF tests. Confirm the next
real output has identifiable sidecars and still exposes CTDB repair.

**Rollback:** restore generic human sidecar names together with literal cue
discovery. Do not rename the machine-stable markers.

**Observability:** no metadata is added to diagnostic logs. Album identity is
present only in the user-selected output folder and its human-facing files.

**Status:** implemented and software verified on 2026-07-27. Focused tests pass
45/45 and the full WPF suite passes 367/367. Live output proof remains.

### Seal CTDB repair evidence and report damaged Test & Copy honestly

**Exact files:** `CUETools.Wpf/Services/RepairEvidence.cs`,
`RepairTransaction.cs`, `VerifyService.cs`, `AlbumArtifactNames.cs`,
`OutputGuard.cs`, `RipService.cs`, `Accuracy/TestAndCopyLog.cs`,
`ViewModels/RipViewModel.cs`, focused WPF tests, the opt-in CTDB test, steering,
and the R71 review record.

**Safety and unchanged behavior:** keep the source-preserving sibling transaction.
Capture SHA-256 proofs before repair, independently decode and query the staged
result, then recheck the source, repaired audio, cue, and evidence immediately
before publication. Write `repair.verify`, the named AccurateRip and repair
reports, and `.cuetools-complete` in that order, with the completion marker last.
Treat agreeing reads with unrecoverable windows as consistent damaged evidence,
not a clean pass.

**Checks:** mutate a source during repair, mutate a verified output before
publication, remove or alter evidence, and require every case to fail closed. Test
clean, damaged-repairable, and damaged-unrecoverable result wording. Run the full
WPF suite and opt-in live damaged-image repair.

**Rollback:** remove the receipt and presentation changes together. Do not weaken
the pre-publication source/output proof checks or publish a partial evidence set.

**Observability:** successful repair diagnostics contain only counts, confidence,
and evidence state. The machine receipt stores portable relative names, lengths,
and SHA-256 values inside the user-selected repaired directory.

**Status:** implemented and live-verified on 2026-07-28. Focused tests pass 45/45
and the WPF suite passes 375/375. The live K: fixture corrected 86 samples in six
sectors, published all required evidence, matched AccurateRip 55/82 and CTDB
207/234, and left all 25 source hashes unchanged.

### Complete the artwork selector with bounded local import and optional TheAudioDB

**Exact files:** `CUETools.Wpf/Services/AppSettings.cs`,
`SecretProtector.cs`, `SettingsStore.cs`, `AlbumArtService.cs`,
`Services/Artwork/ArtworkModels.cs`, `ViewModels/SettingsViewModel.cs`,
`ViewModels/RipViewModel.cs`, `ViewModels/ArtworkCandidateViewModel.cs`,
`Views/SettingsView.xaml*`, `Views/RipView.xaml*`,
`Views/ArtworkBrowserWindow.xaml*`, focused WPF tests, artwork review records,
coverage ledger, and unknowns.

**Safety and unchanged behavior:** preserve CTDB/Cover Art Archive release-first
ranking, the app proxy, image/network limits, and immutable per-job JPEG bytes.
Read a dropped file once and retain bytes, not its path. Accept JPEG, PNG, and BMP
by magic; convert PNG/BMP to JPEG and apply the configured side limit with the
existing Mitchell resize path. A local override belongs only to the current
release generation. TheAudioDB remains off by default, accepts an API key rather
than account credentials, stores it with purpose-separated current-user DPAPI,
never logs it or a keyed URL, and ranks below exact-release Cover Art Archive.
Non-front provider art is browser-only.

**Checks:** run importer format, byte, dimension, pixel, malformed input, override
lifetime, immutable snapshot, protected-key round-trip/corruption/clear/redaction,
TheAudioDB parser/error/rate/host/cancellation, ranking, selector layout, theme,
and keyboard tests. Run the full WPF suite, warning gate, publish, provider probes,
and independently inspect real automatic and local-override embedded output.

**Rollback:** disable TheAudioDB and remove local import UI together with their
settings. Keep the existing CTDB/Cover Art Archive selector and hidden-fallback
removal. Never roll back to plaintext secrets or retained local paths.

**Observability:** record only provider, status class, candidate count, match
tier, dimensions, and encoded byte count. Never record API keys, keyed URLs,
music identity, local image paths, or response bodies.

**Status:** implemented, software-verified, and partially live-verified through
2026-07-29 under R72/R73. Live provider probes return HTTP 200. The first normal
dark browser capture exposed a default-white DataGrid body; the repaired browser
passed dark/light 1040x700 captures at 96 DPI. A real image rip embedded the
selected cover byte-for-byte. High-contrast and 150/200 percent DPI browser
captures remain.

### Gate encoded jobs on a stable artwork snapshot

**Exact files:** `CUETools.Wpf/ViewModels/RipViewModel.cs`, the focused
presentation-policy test, and the R83 review record.

**Safety and unchanged behavior:** when release-bound art is loading, disable
Rip and Test & Copy and enforce the same check inside both execution paths.
Leave Verify available because it does not publish audio. Requery encoded-job
commands whenever artwork loading changes. Preserve the existing immutable
byte-array snapshot once a job starts.

**Checks:** prove the encoded-job policy rejects a loading artwork state and
accepts the same ready state. Run the full WPF suite, warning gate, and
self-contained production artifact contract. Repeat a fast real encoded job
started as soon as disc identification finishes and inspect the embedded
picture.

**Rollback:** remove the command and execution gates together. Do not move the
cover snapshot into the optical worker or permit a later UI selection to mutate
an active job.

**Observability:** the existing `art discovery complete` and `embed selected
cover` structural events show the ordering without recording music identity or
image content.

**Status:** live-verified 2026-07-29. The focused regression and full WPF
suite pass 431/431, the warning gate is empty, and the production contract
passes 36 required files, 19 plugin registrations, and five native probes. The
live transition trace kept Verify available while encoded jobs were blocked,
then enabled Rip and Test & Copy after the cover stabilized. An immediate Burst
rip produced 10 FLAC files. Each file contained one embedded picture whose
100,222 bytes and SHA-256 matched `folder.jpg`; final output PCM verification
also passed after metadata.

### Align archival defaults, output layouts, read evidence, and themes

**Exact files:** WPF app/settings persistence, advanced and Rip views,
`EncoderCatalog`, `VerifyHistory`, `RipService`, naming/release ranking,
`ThemeService`, shared theme resources, theme-aware drawing controls, focused
tests, coverage records, and R74-R79.

**Safety and unchanged behavior:** apply the new defaults once to existing
profiles, then preserve later user choices. Keep the EAC-style rip log separate
from the detailed CTDB data in the AccurateRip report. Accept only the documented
qaac and oggenc executable aliases under the existing hash-bound import receipt.
Count completed Test, Copy, and Test & Copy jobs by their actual role instead of
counting carried-forward display fields. Keep track files as the default output,
and expose the engine's already-supported single FLAC with embedded CUE as an
explicit alternative. Do not implement a dual-output transaction until both
artifacts can share one atomic assurance contract. Prefer a single-disc metadata
candidate over a redundant generic multi-disc candidate with the same album and
track identity, while leaving every candidate selectable.

**Checks:** run settings migration/round-trip tests, CTDB/EAC artifact separation,
external encoder alias/import/receipt tests, legacy and current history aggregation,
same-drive and cross-drive CRC presentation, release-ranking fixtures, every
output-style final-decode proof, responsive XAML checks, palette-token coverage,
full WPF tests, warning gates, and self-contained publish.

**Rollback:** revert each persisted setting with its migration flag. Keep
previously stored history fields readable. Removing image output must not change
the engine's existing `CUEStyle` support. Reverting theme visuals must retain
dynamic palette resolution.

**Observability:** log setting migration names, curated executable names, output
layout ids, and numeric evidence counts only. Do not add album identity, output
paths, drive serials, or executable source paths to diagnostics.

**Status:** implementation and the selected live evidence are complete as of
2026-07-29. Interactive 1590x880 and 1180x740 captures passed in light and dark
at the host's actual 96 DPI. The H: metadata reread exposed and then closed a
provider-credit mismatch in the single-disc duplicate check. A Paranoid optical
rip published one FLAC with matching ten-track embedded/external cue sheets,
post-metadata decode proof, an exact selected-cover embedding, and an unambiguous
cue-to-image repair binding.
The closing gates pass: ripper 22/22, WPF 417/417, zero warning fingerprints,
and the self-contained x64 artifact contract including all five native-plugin
probes.

### Rebuild the live CD surface without changing optical-read behavior

**Exact files:** `CUETools.Wpf/Controls/DiscModel3D.cs`,
`CUETools.Wpf/Controls/DiscReadMap.cs`, `CUETools.Wpf/Services/ThemeColor.cs`,
`CUETools.Wpf/Services/ThemeService.cs`, focused WPF tests, R12 design notes,
and R80.

**Safety and unchanged behavior:** retain `Progress`, `Active`, `RereadActive`,
`RereadFrac`, and `Unreadable` as the only live inputs. Preserve the equal-area
25 to 58 mm read-radius mapping and the existing re-read/unreadable camera
target. Keep the bad-sector zoom ease-in, recovery ease-out, and unreadable
hold behavior. Limit the change to materials, representative surface texture,
physical layer geometry, pickup presentation, and theme-aware fallback drawing.

**Checks:** assert radius clamping and equal-area values, pickup movement, and
damage zoom transitions. Require matching typed light/dark palette tokens.
Render idle, reading, re-reading, and unreadable states offscreen in both themes,
then inspect live 96-DPI captures. Run the full WPF suite, warning gate, and
self-contained publish contract.

**Rollback:** revert the material and geometry layer as one unit. The existing
telemetry bindings and 2D fallback remain available.

**Observability:** none. This changes only local rendering and adds no new
telemetry, logging, persistence, or optical-drive commands.

**Status:** fixed and visually verified 2026-07-29 under R80. Actual dark/light
1180x740 windows pass at 96 DPI. A ten-frame offscreen matrix covers the four 3D
states and tier-zero fallback in both themes. Focused visual contracts pass 8/8,
including a 1,000-frame allocation bound. The WPF suite passes 423/423, and the
warning budget emits zero fingerprints.
The self-contained x64 artifact contract passes all 19 plugin registrations and
five native-plugin probes.

### Measure the live CD surface during real damaged-media recovery

**Exact files:** `CUETools.Wpf/Controls/DiscFrameMetrics.cs`,
`CUETools.Wpf/Controls/DiscModel3D.cs`,
`CUETools.Wpf/Services/RipService.cs`, focused WPF tests, R12 design notes,
live release evidence, R81, and R82.

**Safety and unchanged behavior:** keep the sampler disabled unless
`CUETOOLS_DISC_FRAME_METRICS` names an output file. Observe the existing
`CompositionTarget.Rendering` callback after it has advanced the model. Do not
change optical commands, retry policy, progress reporting, damage state, camera
motion, or output publication. Use fixed histograms after construction so the
measurement adds no per-frame allocation. Record only numeric timing and visual
state, never disc identity, drive identity, paths, metadata, or audio.

**Checks:** test disabled-by-default behavior, numeric-only output, independent
idle/reading/re-reading/unreadable buckets, state transitions, percentile
calculation, and the post-warmup allocation bound. Run the focused visual suite,
full WPF suite, warning gate, and self-contained publish. Then run a real
Paranoid Test & Copy on K: and require at least one measured re-read interval
before closing the benchmark.

**Rollback:** remove the sampler and its single call site. The committed R80
visual and its existing state contract remain unchanged.

**Observability:** the opt-in JSON receipt contains schema and product versions,
process id, UTC state-transition times, frame/callback histograms, progress
fractions, and zoom values. The normal application log remains the independent
proof that a measured interval was a real optical re-read. Every early Test &
Copy failure now emits one phase-bound terminal diagnostic; a failed sink cannot
replace the original result.

**Status:** fixed and hardware-measured 2026-07-29. WPR and PresentMon both
require elevation on this host, so the source-bound fixed-histogram sampler
provided the non-administrative lane. Commit `31d839b` measured 49,984 re-read
frames over 714.9 seconds during 385 real recovery passes. Normal and re-read
p99 were both 15.0 ms; re-read callback time averaged 0.0227 ms and peaked at
1.1332 ms. K:'s Copy phase later failed at 92 percent on the separately tracked
R69 cache-defeat `IllegalRequest 24/00`; the staging root was empty afterward.

### Add provenance-bound command encoders without weakening user override

**Exact files:** `CUEToolsCodecsConfig`, WPF `EncoderCatalog` and tuning/settings
views, runtime trust tests, WPF project/publish/notices, external encoder
preparation manifest/script, release artifact validator/manifest/safety tests,
codec review records, and license text.

**Safety and unchanged behavior:** keep every in-process codec path unchanged.
Offer a command encoder only when its executable and, for lossless output, its
independent verifier are usable. Check the receipt-bound per-user directory
before packaged fallbacks, so an intentionally imported update wins. Bind both
imports and packaged executables to an exact SHA-256 and length immediately
before process launch while holding a non-replaceable read lease. Treat
app-adjacent absolute paths and PATH as explicitly user-managed compatibility
paths without making an origin claim.

**Checks:** pin HTTPS archive URL, byte length, SHA-256, selected entry hash,
license, and source archive in one manifest. Refuse download drift, zip-entry
drift, reparse output, or an unlisted packaged executable. Require artifact
hashes to agree with preparation and runtime pins. Test tampered imports,
tampered packaged tools, alias discovery, imported override, lossy defaults,
lossless verifier contracts, and implementation selection. Run real stdin
encodes through the exact packaged Opus and Vorbis files, focused WPF trust
tests, warning-clean release build, notices generation, and the self-contained
artifact contract.

**Rollback:** remove each affected packaged executable and its source/build
artifact entries together.
The catalog retains user imports; do not remove receipts or weaken lossless
verification. A licensing/provenance failure disables packaging, not runtime
trust checks.

**Observability:** bounded diagnostics state the curated executable name and a
reason code such as hash, location, or approval. They never log imported source
paths or audio identity.

**Status:** implemented and verified 2026-07-29. Focused external-encoder tests
pass 22/22 and the full WPF suite passes 440/440. The deterministic
current-libopus build, RareWares oggenc2, and the deterministic Musepack build
performed real stdin encodes. The release build is warning-clean, the artifact
contract passes 52 required paths, and third-party notices include the
binary/source provenance.
exhale 1.2.2 and OptimFROG 5.100 real CLI contracts were also exercised; their
documented patent/notification boundaries keep them import-only.

### Lock first-party NuGet dependency closures without dirtying vendors

**Exact files:** root `Directory.Build.props`, one `packages.lock.json` beside
each first-party `PackageReference` project, the lock inventory test, release
safety, R32, and the review ordering.

**Safety and unchanged behavior:** enroll only the explicit first-party project
list. Do not apply root policy to pinned `ThirdParty` projects or generated
vendor staging. Local restores may regenerate locks intentionally; GitHub's
existing `CI=true` environment turns on `RestoreLockedMode` and refuses an
unreviewed direct or transitive dependency change.

**Checks:** discover first-party PackageReference projects from evaluated XML,
require the policy list and filesystem to agree exactly, parse every lock file,
and reject a generated vendor lock. Run force-evaluate once to create the
closures, then repeat solution, WPF-test, and ripper-test restores in locked
mode. Run the release-safety suite and verify all vendor submodules remain clean.

**Rollback:** remove the root policy and all 13 lock files together. Do not leave
`RestoreLockedMode` enabled without committed locks or disable it only in the
release workflow.

**Observability:** restore failures identify the project whose dependency graph
changed. The policy adds no application logging or runtime data.

**Status:** implemented and verified 2026-07-29. The inventory finds exactly 13
first-party PackageReference projects and 13 valid lock files. Locked solution,
WPF-test, and ripper-test restores pass; no vendor submodule or staged vendor
source was modified.

### Convert the paired FLACCL projects without changing their runtime architecture

**Exact files:** `CUETools.Codecs.FLACCL/CUETools.Codecs.FLACCL.csproj`,
`CUETools.FLACCL.cmd/CUETools.FLACCL.cmd.csproj`, `CUETools.sln`,
`Directory.Build.targets`, R8/R12/R88 records, and GPU coverage records.

**Safety and unchanged behavior:** retain net47, assembly identity/version,
plugin and command output paths, localized resource names/bytes, `flac.cl`
bytes/copy behavior, project references, and the command host's effective
32-bit-preferred PE contract. Do not change encoder or command source.
Keep Core-MSBuild-only resource support out of the command host because it has
no resources of its own.

**Checks:** capture an old-project binary baseline; require locked restore plus
zero-warning Core and full-MSBuild builds. Compare public IL declarations,
manifest and satellite resource hashes, kernel hash, config probing, and PE
flags. Exercise modes 0-8 with verify, two CPU workers, 24-bit input, the exact
4096-sample boundary, and verify-on/off output identity on the available OpenCL
device.

**Rollback:** revert both project conversions and the solution type GUIDs as one
unit. Do not retain an SDK command host without its explicit 32-bit preference.

**Observability:** build and test output only. This changes no application
logging, network access, settings, audio metadata, or telemetry.

**Status:** implemented and verified 2026-07-29. The first 64-bit SDK-host run
failed `OUT_OF_RESOURCES`; an isolated host/plugin matrix proved architecture
was the differentiator. With the legacy 32-bit-preferred flag explicit, the
RTX 3060/OpenCL 3.0 matrix passes. The plugin retains all 126 public IL
declarations, exact resource payload hashes, and exact kernel bytes.

### Retire the unreachable CLParity experiment

**Exact files:** `CUETools.CLParity/`, its `CUETools.sln` entry/configurations,
the S8 reachability ledger, D7/R8/R12 scope, R89, and this plan.

**Safety and unchanged behavior:** require independent proof that no current
project consumes the assembly, registration is disabled, release collection
does not ship it, and the project cannot build from tracked dependencies. Do
not translate its obsolete settings/writer API into a current encoder by
assumption. Preserve recovery through Git history.

**Checks:** enumerate first-party project candidates and all C#/project/solution
references; inspect the registration and `IAudioDest` mismatch; scan release
manifests; remove the project and require zero code/project/solution matches.
Run release safety and canonical tests afterward.

**Rollback:** recover the 35-file tree and solution entries from the parent
commit only if a current product contract, CPU oracle, and supported OpenCL
test matrix are supplied together.

**Observability:** none. The experiment had no runtime reachability, packaging,
settings, or logging surface.

**Status:** implemented 2026-07-29. Among 68 first-party project candidates,
only four files inside the experiment plus the solution mentioned its types or
assembly. The 308,630-byte tracked tree and solution entry are removed; the
post-removal code/project/solution scan has zero matches.

### Convert BluTools as the first classic-GUI SDK-project pilot

**Exact files:** `CUETools.eac3ui/BLUTools.csproj`, its `CUETools.sln` project
type, `Directory.Build.targets` scope notes, and R8/R12/R90/S9 records.

**Safety and unchanged behavior:** retain net47, `BluTools` assembly identity,
WinExe output, Release AnyCPU PE32/IL-only behavior, application icon, generated
settings/resources, exact output path, project references, and every tracked
WPF/image resource. Change no application source or workflow behavior.

**Checks:** capture the old Release executable and generated config; require
zero-warning Core and full-MSBuild builds; compare public declarations, fields,
method declarations, assembly identity, PE flags, config bytes, manifest
resource names, and each embedded image payload. Start both old and new
executables hidden and require a healthy live window with the expected title.
Run canonical WPF and release-safety gates.

**Rollback:** revert the project and solution type as one unit. The baseline is
recoverable from the parent commit; do not retain a conversion that changes
runtime architecture or drops a resource.

**Observability:** build/test/startup output only. The project conversion changes
no application logging, settings values, network behavior, media processing, or
telemetry.

**Status:** implemented and verified 2026-07-29. Core and full MSBuild complete
with zero warnings. Old/new builds match across 33 public declarations, 44
fields, 59 method declarations, assembly identity, PE flags, exact generated
config, and 19 embedded image payloads. The WPF compiler changed only the BAML
encoding; both binaries construct a live hidden `BluTools` window.

### Convert CUERipper and its ProgressODoom dependency as the second classic-GUI slice

**Exact files:** `CUERipper/CUERipper.csproj`,
`ProgressODoom/ProgressODoom.csproj`, their `CUETools.sln` project types,
`Directory.Build.targets` restore-boundary note, and R8/R12/R91/S9 records.

**Safety and unchanged behavior:** retain net47, assembly/file versions
(including ProgressODoom's intentional `1.0.*` assembly version), WinExe/library
roles, AnyCPU PE32/IL-only behavior, application manifest/icon, ClickOnce
properties, bootstrapper declarations, unsafe-code setting, output paths,
project references, form/localized resources, generated settings, and the
`plugins` config probe. Change no application source.

**Checks:** capture old packaged binaries, config, and satellites; perform a
Core restore/build and a separate canonical full-MSBuild restore/build with
zero warnings; compare IL classes/fields/methods/public declarations, assembly
and file identity, PE flags, XML-normalized config, every manifest resource,
localized satellites, and decoded pixels for compiler-rewritten WinForms
image streams. Start old and new executables hidden, enumerate their top-level
windows, and require a responsive `CUERipper 2.2.6` main form. Run canonical
WPF and release-safety gates.

**Rollback:** revert both projects and their solution types together. Do not
retain a CUERipper conversion whose resource/control dependency stays
old-style, or infer full-build closure from a Core-generated assets file.

**Observability:** build/resource/startup evidence only. This changes no rip,
network, settings value, device, metadata, logging, or telemetry behavior.

**Status:** implemented and verified 2026-07-29. CUERipper retains 33 classes,
200 fields, 274 methods, and 179 public declarations; ProgressODoom retains 45,
241, 424, and 378 respectively. PE flags, config semantics, satellites, all 26
control icons, and all decoded GUI images match. Both old/new applications
create 13 top-level windows with the expected responsive main form.

### Convert CUEPlayer after capturing its runtime and resource contract

**Scope:** replace only `CUEPlayer/CUEPlayer.csproj` with an SDK-style net47
WinForms project and update its solution project-type GUID. Preserve assembly
metadata, references/copy-local behavior, publish/bootstrapper properties,
Release unsafe-code behavior, generated settings/resources, output path, and
the historical exclusion of two dataset source files. Change no application
source and do not add CUEPlayer to a release package.

**Checks:** capture the old executable/config; perform separate Core and
canonical full-MSBuild restores/builds; compare normalized IL declarations,
assembly identity, PE flags, XML config, manifest resources, and decoded image
pixels. Start old and new executables beside the same dependency closure and
require the same responsive `CUEPlayer 2.2.6` window set. Run canonical WPF,
release-safety, and NuGet-lock gates.

**Rollback:** revert the project and solution type together. Do not infer
shipping reachability from solution membership.

**Observability:** build/resource/startup evidence only. This changes no
playback, streaming, credential, settings value, logging, or telemetry
behavior.

**Status:** implemented and verified 2026-07-29. CUEPlayer retains 29 classes,
175 normalized fields, 236 methods, and 118 public declarations; PE/config
semantics and all five decoded GUI images match; and both builds create eight
top-level windows with the expected responsive main form.

### Convert classic CUETools and reject silently duplicated resource names

**Scope:** replace only `CUETools/CUETools.csproj` with an SDK-style net47
WinForms project and update its solution project-type GUID. Preserve assembly,
architecture, unsafe-code, icon/manifest, ClickOnce/bootstrapper, content,
generated settings/resource, localized resource, output, and project-reference
contracts. Exclude the four nested test-project trees from SDK default items.
Remove only later `.resx` nodes whose complete XML is identical to the retained
first node.

**Checks:** capture the old executable/config/PDB/satellites; perform separate
Core and canonical full-MSBuild restores/builds; compare normalized IL
declarations, assembly identity, PE flags, XML config, every main manifest
resource, and every localized resource entry. Start old and new executables
beside the same dependency closure and require the same responsive
`CUETools 2.2.6` window set. Scan all first-party `.resx` files for duplicate
names and run canonical tests, lock, and release-safety gates.

**Rollback:** revert project, solution type, exact-duplicate cleanup, and guard
together. Do not retain a resource rewrite unless compiled resource equality is
proven.

**Observability:** build/resource/startup evidence only. This changes no
conversion, verification, repair, network, settings value, logging, or
telemetry behavior.

**Status:** implemented and verified 2026-07-29. CUETools retains 53 classes,
463 normalized fields, 434 methods, and 229 public declarations; all ten main
resources and all 615 localized resource entries match; PE/config semantics
match; and both builds create 16 top-level windows with the expected responsive
main form. The new CI guard passes across 67 first-party resource files.

### Modernize and prove the standalone FFmpeg 8 path

**Scope:** update only the unshipped FFmpeg managed wrapper, package lock,
manual native workflow, focused worker, native inventory, and guard/docs. Keep
it outside both primary artifact collectors.

**Safety:** require binding/runtime major equality before native use; contain
all managed callback exceptions; give each native allocation one idempotent
owner; drain delayed frames; preserve deterministic AIFF seeking; never infer
runtime success from a compiled P/Invoke surface.

**Checks:** dual-target zero-warning build; source-build FFmpeg 8.1.2#3 from one
immutable vcpkg commit for x64 and x86; run matching-process path/stream/PCM/
seek/disposal/callback probes; actionlint; emit license, port manifest, versions,
sizes, and SHA-256.

**Rollback:** revert wrapper, lock, workflow, worker, inventory, and guard as one
unit. The primary packages remain unaffected because this path is unshipped.

**Status:** implemented 2026-07-29. Matching x64 and x86 16/24-bit runtime
proof passes; the hosted receipts complete the evidence record.

### Enforce publisher signing without invalidating plugin or provenance evidence

**Scope:** add a declarative signing policy, invocation/refusal script, static
gate, release workflow step, WPF dependency contract completion, and owner
runbook.

**Safety:** never commit or print credentials; import PFX non-exportably and
remove it in `finally`; sign only contract-selected publisher-built PE files;
exclude hash-pinned upstream bytes; require SHA-256 Authenticode and RFC 3161;
regenerate plugin hashes after signing; revalidate before provenance/SBOMs.

**Checks:** static policy/order/coverage checks; 117-file local plan; actionlint;
unsigned manual evidence must label itself non-releaseable; tags and explicit
signed dispatches must refuse missing or mismatched credentials.

**Rollback:** revert the policy/workflow/scripts/contracts together. Never
retain a workflow that can upload a nominal release after a skipped signing
step.

**Status:** repository policy implemented 2026-07-29. Public-trust certificate
identity and protected GitHub values remain external owner provisioning.

### Align locked test graphs and make the fresh classic build deterministic

**Scope:** convert the resource-free `CUETools.TestHelpers` library to SDK-style
net47, explicitly exclude it from the preserialized-resource package graph,
refresh only the two dependent test locks, and change the receipted Visual
Studio commands from `/Rebuild` to `/Build` after the orchestrator's existing
declared-output pre-clean.

**Safety:** preserve assembly identity, public surface, references, output path,
and app config. Keep command-plan changes bound to the receipt. Permit an
explicit stale-source recovery to archive, but never execute, an older
command-plan intent.

**Checks:** compare the legacy and SDK assembly/resource/config contracts; run
Core and full-MSBuild restores; hash locks across the real devenv build; run
the exact fresh-output Any CPU/x64/Win32 release transaction; require zero
native-warning drift and exact artifact publication.

**Rollback:** revert the project conversion, two locks, eligibility rule,
command plan, tests, and docs as one unit. Retain abandoned intent/log bytes as
evidence.

**Status:** implemented and verified 2026-07-30. The helper retains 2 public
types, 15 public methods, 7 public properties, 7 public fields, zero resources,
the same five references and version, and the same app config. Devenv built
58/58 projects without changing either lock. The full receipted release
transaction passed all three configurations and exact artifact collection.

### Remove deprecated hosted action-runtime shims

**Scope:** update immutable checkout, .NET setup, artifact-upload, and vcpkg
action pins across the four workflows and add the hosted-annotation rule to the
local audit steering material.

**Safety:** use only current upstream action releases and their exact commit
SHAs. Preserve every workflow input, permission, matrix, shell, step order,
artifact name, and artifact path.

**Checks:** actionlint; FFmpeg workflow, signing-policy, and release-safety
contracts; source-bound classic/WPF CI; dual-architecture FFmpeg behavior and
artifact inspection; unsigned release artifact inspection; no deprecated action
runtime annotations.

**Rollback:** revert all action pins and the rule together only if a current
action changes the established workflow contract. Do not accept the old
compatibility warning as the rollback success criterion.

**Observability:** hosted annotations, job conclusions, exact source revision,
and downloaded artifact receipts only. Product runtime behavior is unchanged.

**Status:** closed 2026-07-30. FFmpeg run `30516040154`, classic CI
`30518472651`, WPF CI `30518472662`, and release run `30518479906` succeeded
with zero final check-run annotations.

### Make exact-byte and net20 fallback tests host-independent

**Scope:** pin the RAR fixture checkout bytes; activate the locked net20
reference-assembly fallback only for Core MSBuild; retain the same restore-only
direct dependency under full MSBuild; give the Core compatibility probe its own
serialized restore graph; strengthen the package-role gate.

**Safety:** do not change the archive, decoded payload, codec code, dependency
version, or lock. Limit the EOL rule to the exact fixture and keep the archive
binary.

**Checks:** Git attribute inspection; focused production RAR enumeration,
full-read, and backward-seek test; Core net20 restore/build and exception relay;
unchanged lock hash; Core/full role assertion; lane-isolated
`MSBuildProjectExtensionsPath`; hosted legacy lane on the image without an
installed .NET 2.0 targeting pack.

**Rollback:** revert attributes, Core package role, and its guard together.
Do not restore a host-dependent text oracle or a fallback that is only visible
to the build host that does not need it.

**Observability:** test bytes, package evaluation, build output, and hosted
receipts only. Runtime packaging remains unchanged.

**Status:** closed 2026-07-30. The first replacement hosted run proved the RAR
checkout fix, then falsified reuse of full MSBuild's serialized assets by the
Core no-restore build; the isolated restore graph is the resulting repair.
Final classic run `30518472651` passed 18/22 parity, 111/113 codec, 9/10
processor, and 17/17 ripper tests, with only the declared skips.

### Make hash-bound encoder build support checkout-stable

**Scope:** pin every repository text input selected by external-command encoder
source-support manifests to LF and assert the complete selected set in release
safety.

**Safety:** do not change the patches, build instructions, manifests, expected
sizes, expected hashes, downloaded sources, or encoder binaries. The attributes
only preserve their committed LF bytes across checkout hosts.

**Checks:** `git check-attr`; exact source-support size/hash validation; release
safety; clean WPF publication; source-bound unsigned hosted release.

**Rollback:** revert the attributes and their release assertion together only
if the manifest gains an explicit non-text member with its own byte contract.
Do not restore host-dependent CRLF expansion.

**Observability:** exact path, byte length, SHA-256, checkout attributes, and
hosted publication receipt.

**Status:** closed 2026-07-30. The first hosted release reached clean WPF
publication only after all release controls, classic builds, tests, fuzzing,
and classic artifact validation passed; it then exposed Opus source support
expanded from 307 to 317 bytes. Final release run `30518479906` published and
the downloaded payloads passed independent artifact/hash inspection.

### Repair stale native-input provenance and pin checkout bytes

**Scope:** update the libFLAC patch digest after its reviewed 31-line change;
give every native-inventory pinned file an explicit checkout representation;
derive the complete-set attribute and hash checks from the inventory.

**Safety:** do not edit or regenerate the libFLAC patch, native source, SDK
archive, or vendored binaries. Bind the inventory to the current committed blob
and retain its intentional mixed line endings byte-for-byte.

**Checks:** history attribution; 13-file current/blob SHA-256 comparison;
`git check-attr`; native preparation; release safety; both local provenance
receipts; source-bound unsigned hosted release and downloaded receipt
inspection.

**Rollback:** revert the digest, byte attributes, and complete-set guard
together only if the corresponding source change is also reverted. Never make
provenance pass by weakening or skipping its input hash check.

**Observability:** current and committed-blob digests, exact paths, attribute
results, provenance receipts, and hosted source revision.

**Status:** closed 2026-07-30. The first post-EOL hosted release passed both
application publications and unsigned signing-policy evaluation before the
stale `81a305c6...` digest rejected the current `e57a0c47...` patch. Final
release run `30518479906` accepted the corrected native inventory, and both
downloaded receipts bind its SHA-256.

### Make derived-source provenance independent of Git ignore visibility

**Scope:** validate the complete Monkey's Audio archive expansion through the
native inventory helper; record its closure in both provenance receipts;
exclude only exact validated derived members from unknown untracked source;
separate classified build residue from source-state dirtiness.

**Safety:** do not ignore the SDK directory by prefix and do not trust file
extensions as source identity. Require the pinned archive and four overrides,
reject archive traversal/collisions/reparse points, compare every expanded
member's path/size/hash, reject every foreign file, and retain classified
generated counts in the receipt.

**Checks:** 423 archive members and four overrides under Windows PowerShell
5.1; stable closure digest; source-state policy unit cases; local provenance;
clean hosted clone where private `.git/info/exclude` is absent.

**Rollback:** revert the closure helper, provenance classification, state
policy, and tests together. Never regain `clean` by hiding the expansion or
dropping unknown untracked files from enumeration.

**Observability:** closure identity/counts, unknown untracked records,
classified build-residue counts, patch ID, submodule states, and final
source-state verdict.

**Status:** closed 2026-07-30. Both clean-clone receipts from release run
`30518479906` report clean source, no unknown untracked files, five clean
submodules, and the exact 423-member expansion with closure SHA-256
`5777ba9a6debcd55565ba49c2e713fdb46a62d81474bc17d394ef17893eeb578`.

### Preserve and validate typed SBOM evidence after normalization

**Scope:** replace PowerShell object canonicalization with a package-free net8
JSON guard; refresh the SPDX sidecar after the final byte transform; validate
exact SPDX artifact membership, CycloneDX dependencies, and Microsoft SBOM
Tool results; remove the expected hosted no-package annotation.

**Safety:** keep tool versions, root package identities, source commit/time,
and the complementary SBOM roles unchanged. Suppress component-detector
warnings only after the SPDX file closure and the separate CycloneDX package
graph pass stronger explicit postconditions. Do not accept a generator or
validator process exit without inspecting its result document.

**Checks:** zero-warning guard build; malformed one-element-array regression;
idempotent canonicalization; current 97-file classic and 557-file WPF
inventories; matching final sidecars; 24/25 and 37/38 CycloneDX graph counts;
Microsoft validation; zero hosted annotations.

**Rollback:** revert the guard and generation script together. Do not restore
the PowerShell 5.1 `ConvertFrom-Json`/`ConvertTo-Json` round-trip or retain a
sidecar generated before normalization.

**Observability:** typed JSON shapes, artifact hashes, file-ID sets,
root-package relationships, dependency refs, sidecar hash, validator result,
and GitHub job annotations.

**Status:** closed 2026-07-30. Release run `30518479906` and the independently
downloaded artifact both passed exact classic 97/97 and WPF 557/557 SPDX
validation, populated CycloneDX graph checks, final sidecar checks, and zero
hosted annotations.

### Bind packaged native wrappers to the manifest-approved production path

**Scope:** add one process-local approved native-path registry in
`CUETools.Codecs`; publish each already hashed, full-path-loaded dependency from
`PluginTrustManifest`; migrate the five codec wrappers from `Assembly.CodeBase`
to that registry with their existing assembly-relative architecture folder as
the classic-host fallback; make Monkey's Audio finalizer cleanup nonthrowing.

**Safety:** a packaged codec may use only the exact path that the trust manifest
rehashes, loads, and confirms through the Windows module path. Do not add a bare
DLL name, application-root search, `PATH` search, or a second native copy. Reject
conflicting registrations. The classic fallback remains one exact
`<managed-assembly>/<architecture>/<native-name>` path.

**Checks:** resolver path and conflict tests; all five wrapper source guards;
focused native settings/version probes; real encode/decode tests; self-contained
WPF publication; a child WPF apphost probe launched from the published layout;
artifact validation with no root `x64` workaround.

**Rollback:** revert the registry, manifest handoff, wrapper migrations, and
process probe together. Do not regain a passing artifact probe by copying native
DLLs beside the root managed duplicates.

**Observability:** manifest relative path, managed codec identity, bounded native
filename, readiness category, child-process exit, and probe receipt. Do not log
user paths or command arguments.

**Status:** closed 2026-07-31. The trust loader now registers only the exact
rehashes-and-loaded native path, conflicting bindings fail closed, and all five
wrappers use that approved path with the classic assembly-relative fallback.
The clean self-contained WPF artifact launched its real apphost with root-loaded
managed wrappers, then initialized and finalized real FLAC, WavPack, Monkey's
Audio, and LAME outputs plus HDCD. Lossless smoke outputs decoded and compared
inside their wrappers. The artifact contract passed with 77 required files, 14
runtime trust entries, 19 registrations, five native probes, and no root native
copy. Monkey's Audio finalizer cleanup is nonthrowing after partial initialization.

### Make codec selection truthful before optical work starts

**Scope:** normalize stale command-encoder verification fields, hide structural
profile properties from the generic editor, model every format face with one
health/status/origin descriptor, add a grouped and sortable codec picker, and
validate the selected encoder before Rip or Test & Copy takes hardware ownership.

**Safety:** lossless command encoders still require an independently executable
verification contract. Lossy profiles must not inherit that contract. Unavailable
rows stay visible for explanation but cannot be selected. Readiness probing may
touch settings/version metadata only; it must not create output files or read a
disc. Preserve the extension plus persisted lossless/lossy face as the engine
contract.

**Checks:** JSON migration tests for stale lossy profiles; browsable-property
guard; command and native health tests; deterministic grouping and guidance-sort
tests; Rip/Test & Copy preflight tests; XAML reachability and light/dark token
inspection; full WPF suite and warning gate.

**Rollback:** revert the descriptor/picker and preflight together while retaining
the native-path repair. Do not restore a raw extension list that silently removes
unavailable codecs or lets a known-bad selection begin reading the disc.

**Observability:** codec display name, extension, selected implementation,
lossless/lossy category, origin class, readiness class, and bounded failure type.

**Status:** closed 2026-07-31. Lossy profiles normalize stale verifier state only
at controlled load/catalog boundaries; a mutable property assignment still cannot
weaken an existing lossless verification contract. Structural fields are hidden.
Rip, Convert, and Queue share one grouped picker with full names, extensions,
implementations, origins, readiness, licensing, history, best use, unavailable
rows, and honest guidance sorts. Queue records retain the exact implementation
identity. Rip and Test & Copy validate that implementation before settings
publication or drive ownership and freeze it for the complete evidence transaction.
The canonical gate passed 643 discovered tests with 637 passes, zero failures, six
declared skips, and zero managed warning fingerprints.

### Recover the observed BEh logical-unit communication rejection once

**Scope:** give the observed H: `HL-DT-ST BD-RE WH16NS40` firmware 1.05 exact
normal payload `ReadCdBEh` 16-sector command one local retry when it returns
`DeviceFailed / HardwareError / 08/0A` outside speed and cache transitions;
preserve that retry in completion telemetry; format the
unassigned qualifier as the standard ASC 08 communication family plus raw
ASC/ASCQ rather than `NO SENSE STRING`.

**Safety:** this is a command-local retry for the repeated real H: signature at
widely separated addresses, not damaged-media evidence. It does not apply to
another drive or firmware, `ReadCdD8h`, one-sector pinpoints, batch decomposition,
cache eviction, control transitions, another transfer size, another sense identity,
or a second failure.
Only bytes from a successful retry may be reorganized or voted. A failed retry
remains fatal with retry context and never marks a sector untrusted.

**Checks:** deterministic positive and negative policy matrix; known and
unassigned ASC 08 formatting tests; source guard proving the retry stays in the
top-level payload loop; focused ripper tests; all SCSI target builds; full managed
test and warning gates; source-bound H: Test & Copy crossing the previously
observed addresses.

**Rollback:** revert the policy, top-level retry, diagnostic counter, formatter,
and tests together. Do not replace the exact policy with a general hardware-error
retry or teach the medium-error pipeline to consume this failure.

**Observability:** relative sector, sector count, command, speed, transition
flags, raw sense key/ASC/ASCQ, `communication-retry=True` on a failed repeat, and
one aggregate successful/attempted retry counter. Never log sector payload.

**Status:** closed 2026-07-31. Four retained H: failures show the same
`HardwareError / 08/0A` on normal 16-sector BEh reads at relative sectors 36,000,
36,576, 192,224, and 241,968. The exact classifier/source-contract suite passes
32/32, all three SCSI targets build, the 641/647 canonical suite and empty warning
gate pass, release safety passes, and the separate R105 self-contained artifact
passes its production contract. Source-bound probes crossed all four addresses,
then a full concurrent H: Test & Copy passed in 846 seconds: 412-second Test,
413-second Copy, 11 verified FLAC files, AR 107/424, CTDB 114/544, zero reread or
failed windows, and decoded-output verification. Both phases recorded zero
communication retries, so live branch activation remains an explicit unknown.
