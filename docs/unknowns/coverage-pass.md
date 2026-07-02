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

### Do the MSTest suites pass on current code?

- **Area or file:** `CUETools/CUETools.Test*`, `CUETools/TestRipper/`
- **Concern:** tests exist but were last known green at an unknown point; CI never runs them, and some (TestRipper) likely need hardware or fixtures.
- **Why it matters:** S14 is the cheapest evidence generator for the high-risk math slices; a red suite changes the remediation order.
- **Evidence found so far:** MSTest references verified; no execution attempted yet (solution not built in this engagement).
- **Confidence:** unknown
- **Likely owner:** repo owner
- **Next best check:** build Release|x64 and run `vstest.console` on TestParity and TestCodecs first (least environment-dependent).
- **Risk level:** medium
- **Status:** open

## Closed items

- **Does CUDA.NET.dll load at runtime?** — resolved 2026-07-02. `FlaCuda` is not referenced anywhere in `CUETools.sln` (grep finds zero hits): both `CUETools.Codecs.FlaCuda/` and `CUETools.FlaCudaExe/` are orphaned projects that no build touches. Their csproj wants `CUDA.NET 2.2.0` from `..\..\CUDANET\lib\` (outside the repo), and `ThirdParty\CUDA.NET.dll` (2.3.7) is referenced by nothing. Both the directories and the DLL are dead weight; removal deferred by user decision until the initial rollout completes.
