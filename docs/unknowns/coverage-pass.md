# Unknowns: coverage pass

Things that block honest coverage claims, found while building the slice plan and ledger (2026-07-02). Entry shape and vocabulary come from `.claude/skills/anti-dark-code/references/00-conventions.md`.

## Entries

### CUEPlayer and eac3ui internals unscanned

- **Area or file:** `CUEPlayer/`, `CUETools.eac3ui/`
- **Concern:** both apps are in the ledger only at directory level; their data flows (typed DataSet in CUEPlayer, eac3to process invocation in eac3ui) are unmapped.
- **Why it matters:** unmapped GUI apps can hide network calls, file writes, or process launches the map does not show.
- **Evidence found so far:** solution entries, project files, one DataSet designer file path.
- **Confidence:** verified (that they are unscanned)
- **Likely owner:** repo owner
- **Next best check:** 02-style fill-in read of both apps' main forms and Program.cs.
- **Risk level:** medium
- **Status:** open

### ProgressODoom and ttalib-1.1 provenance

- **Area or file:** `ProgressODoom/`, `ttalib-1.1/`
- **Concern:** both look like third-party source mirrored into the repo (a progress-bar library and the TTA codec library). Version, upstream, and local modifications are unrecorded.
- **Why it matters:** mirrored trees should be excluded from inline comment churn and tracked for upstream fixes; local diffs may hide bugfixes worth preserving.
- **Evidence found so far:** naming, directory shape, `ttalib-1.1` version suffix. Not compared against any upstream.
- **Confidence:** inferred
- **Likely owner:** upstream maintainer
- **Next best check:** check upstream ProgressODoom (SourceForge-era .NET library) and TTA 1.1 SDK for diffs; record verdict in the ledger (mirror vs forked-and-owned).
- **Risk level:** low
- **Status:** open

### TestRipper fixtures were never committed

- **Area or file:** `CUETools/TestRipper/CDDriveReaderTest.cs:75-110`
- **Concern:** despite its name, the test never touches a CD drive; `ClassInitialize` loads 64 passes of raw sector dumps plus C2 data from a hardcoded `Y:\Temp\dbg\960\` path on the original author's machine, then unit-tests the C2 error-correction voting math offline.
- **Why it matters:** the C2 voting algorithm (the heart of secure ripping) has a real test that cannot run anywhere; and the project cannot be CI-gated until fixtures exist or the test is rewritten against synthetic dumps.
- **Evidence found so far:** source read 2026-07-02. Separately, a live hardware smoke test the same day (drive K:, `CUETools.Ripper.Console --test --drive K`) succeeded: read command negotiated (BEh, 12h, 16 blocks) against a real disc, so the SCSI stack itself works on current Windows.
- **Confidence:** verified
- **Likely owner:** upstream maintainer
- **Next best check:** generate synthetic multi-pass dumps with injected C2 patterns (the commented-out `Random`-based generator at `CDDriveReaderTest.cs:68-69,97-108` shows the original intent) and commit them or the generator.
- **Risk level:** medium
- **Status:** open

### TestProcessor fixtures were never committed

- **Area or file:** `CUETools/CUETools.TestProcessor/`, `Test Images/`
- **Concern:** `ProcessorTest.cs` opens CUE fixtures (`Amarok\Amarok.cue`, `Circuitry\1.cue`, `No Man's Land\1.cue`, and others) that exist nowhere in the repo; the `Test Images` folder is empty of `.cue` files.
- **Why it matters:** 3 of 5 TestProcessor tests can never pass from a clean checkout; CUESheet (the highest-risk class) effectively has no runnable engine tests.
- **Evidence found so far:** test run 2026-07-02: 1 passed, 3 failed on `FileNotFoundException` for fixture paths, 1 skipped.
- **Confidence:** verified
- **Likely owner:** upstream maintainer (fixtures likely lived on the author's machine)
- **Next best check:** synthesize small CC0 fixtures (silence-filled WAV + CUE variants covering gaps appended/prepended, CD-Extra, one-track) and commit them.
- **Risk level:** medium
- **Status:** open

## Closed items

- **Do the MSTest suites pass on current code?** — resolved 2026-07-02 by running them. Results: **TestParity 18 passed / 0 failed / 4 skipped** (skips are long-running and speed tests); **TestCodecs 34 passed / 0 failed / 1 skipped** (the two libFLAC tests need a locally built x64 `libFLAC_dynamic.dll` and an x64 test host); **TestProcessor 1 passed / 3 failed / 1 skipped** (failures are missing fixtures, tracked in the open entry above, not code bugs); TestRipper not run (needs an optical drive). Environment notes for reproducing: the projects reference MSTest v1 (`Microsoft.VisualStudio.QualityTools.UnitTestFramework`), which only ships with full Visual Studio, so on a Build-Tools-only machine the reference was locally swapped to MSTest v2 2.2.10 (not committed); .NET FW 4.7 reference assemblies came from the `Microsoft.NETFramework.ReferenceAssemblies.net47` package via `-p:TargetFrameworkRootPath`; built with `dotnet build` (.NET 8 SDK); run via `vstest.console` with the MSTest v2 adapter, `DeploymentEnabled=false`, and `Data\*` staged next to the test assembly (dependencies must also be copied in because `Private=False` keeps them out of the tests output dir). Remediation input: migrate the four test projects to SDK-style + MSTest v2 so they build without full VS and run in CI.
- **Does CUDA.NET.dll load at runtime?** — resolved 2026-07-02. `FlaCuda` is not referenced anywhere in `CUETools.sln` (grep finds zero hits): both `CUETools.Codecs.FlaCuda/` and `CUETools.FlaCudaExe/` are orphaned projects that no build touches. Their csproj wants `CUDA.NET 2.2.0` from `..\..\CUDANET\lib\` (outside the repo), and `ThirdParty\CUDA.NET.dll` (2.3.7) is referenced by nothing. Both the directories and the DLL are dead weight; removal deferred by user decision until the initial rollout completes.
