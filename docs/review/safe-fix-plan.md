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
classic receipt remains pending until the active rip releases the running
application.

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
22/22. K: hardware evidence remains.

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

**Status:** implemented and software-verified on 2026-07-28 under R73. The WPF
suite passes 395/395, the WPF/fuzz warning gate is empty, the self-contained x64
artifact contract passes, all three live provider probes return HTTP 200, and the
local anti-dark-code skill validates. Interactive theme/DPI captures and
independent real embedded-output inspection remain.
