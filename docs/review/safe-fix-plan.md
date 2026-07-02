# Safe-Fix Plan

Bounded remediation batches under pass 11 Step 2. One section per batch. Update statuses as batches land.

## Batch 1: test modernization and CI gating (2026-07-02)

**Backlog source:** coverage ledger S13/S14 next-pass entries; closed unknown "Do the MSTest suites pass on current code?" in `docs/unknowns/coverage-pass.md`.

**Goal:** the green suites (TestParity, TestCodecs) build without full Visual Studio and gate every push.

**Why safe now:** suite results are known green (verified 2026-07-02); changes touch test projects, the solution file, and CI only — no shipped code. Not approval-gated (S13 is flagged approval-gated for release-path changes; adding a test gate does not alter the release artifact path, and rollback is removing the steps).

### Exact changes

1. `CUETools/CUETools.TestParity/CUETools.TestParity.csproj` — rewrite as SDK-style, net47, MSTest v2 packages (MSTest 3.6.1 + Microsoft.NET.Test.Sdk 17.11.1), `GenerateAssemblyInfo=false` (keeps `Properties\AssemblyInfo.cs`), exclude orphaned `CDRepairTest.cs` and `CDRepairEncodeTest.cs` (present on disk, absent from the old compile list).
2. `CUETools/CUETools.TestCodecs/CUETools.TestCodecs.csproj` — same, plus: `PlatformTarget=x64` (native libFLAC is x64), flatten `Data\*` to the output root (tests open bare filenames), copy `ThirdParty\x64\libFLAC_dynamic.dll` into output when it exists, drop the unused `CSScriptLibrary` reference, exclude orphaned `FileGroupInfoTest.cs` (TestProcessor owns the live copy).
3. `CUETools/CUETools.TestCodecs/FlacWriterTest.cs` — remove the two `[DeploymentItem]` attribute lines pointing at `../ThirdParty*/x64/libFLAC_dynamic.dll`; they only resolved under the original author's VS test settings, and their presence switches MSTest v2 into deployment mode, which breaks every bare-filename data access. The csproj copy above replaces them.
4. `CUETools/CUETools.TestProcessor/CUETools.TestProcessor.csproj` — same SDK-style conversion (build-only; its tests stay red until fixtures exist, so CI does not run it).
5. `CUETools.sln` — flip the project-type GUID for the three converted projects from `{FAE04EC0-...}` (legacy C#) to `{9A19103F-...}` (SDK-style) so `devenv` loads them correctly.
6. `.github/workflows/CI-windows.yml` and `release-windows.yml` — add two steps after the existing builds: `dotnet test` on TestParity and TestCodecs (Release). The Release|x64 devenv build already produces `ThirdParty\x64\libFLAC_dynamic.dll` via `$(SolutionDir)`-relative OutDir, so the codec tests get their native dependency.

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
