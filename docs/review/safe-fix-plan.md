# Safe-Fix Plan

**Current authority, 2026-07-26:** the wave below has been executed through its
locally available gates. Individual batch notes preserve intermediate counts and
plans, but the final status/counts are in `2026-07-26-autonomous-audit.md` and
`remediation-backlog.md`: 388 discovered, 381 passed, 7 expected skipped. Hosted
classic, hardware, and external-service checks remain pending; no intermediate
"ready" label should be read as the current state.

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

**Status:** ready after the repair seam is mapped.

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
