# CUETools mutation harness

> **Status after the first real run (2026-08-24): measured, and the thresholds still stand
> unchanged on purpose. Do not treat any profile here as passing.**
>
> Run on DESKTOP-D084LOM (Ryzen 9 5950X) under Windows 11 Pro 10.0.26220.0, PowerShell 7.6.5,
> .NET SDK 8.0.422, runtime 8.0.30, `dotnet-stryker` 4.16.0, net8.0 Release. The .NET Framework
> toolchain turned out not to be needed: these profiles are standalone net8.0 projects, and
> `Prepare-VendorSources.ps1` is not a prerequisite for them.
>
> The harness did **not** run on master `439decf9` as landed. The claim that all 16
> `expectedSources` still resolve is **wrong** - only the 7 `CUETools.Ripper.SCSI` entries did.
> Repairs in this commit: the 9 mutated sources moved to `CUETools.App.Core/`; a new
> `StoreJsonContext` seam needed a dependency contract; and four files the harness depends on were
> never landed at all, because `e8e21739` took only `eng/mutation/**` and left the production-test
> half of `2a8df3e3` on `origin/agent/mutation-harness`.
>
> That missing half is why the numbers collapsed. Master now measures 78.18 / 48.11 / 78.43 /
> 53.33 / 58.82 / 32.00 in Quick against floors of 95.0 / 89.0 / 89.0 / 92.0 / 75.0 / 89.0. The
> same seven profiles, run unmodified in a worktree at `8961b0b6`, **all pass and reproduce the
> measured column below to the hundredth**. The harness is sound; the tests it grades are missing.
>
> The thresholds were therefore not lowered to the master numbers. Doing so would encode the loss
> of roughly two thirds of the assertions as the new standard. The owner decision is D12 in
> `docs/review/decisions-needed.md`; the full evidence is
> `docs/review/2026-08-24-mutation-harness-rebaseline.md`.
>
> `test-copy-history` **cannot run at all** and is left in place, not deleted.
> `CUETools.Wpf.Tests/TestAndCopyResolverTests.cs` now calls `RipService.BuildTestCopyCrcEvidence`,
> and `RipService.cs` is 3223 lines pulling AccurateRip, Codecs, CTDB, Processor, Ripper, and
> Ripper.SCSI - the exact graph these profiles isolate against. That method is behavior-bearing
> logic, so it does not qualify for a contract shim.
>
> Still deliberately **not wired into any workflow**. It gates nothing and cannot break a build.

This harness asks a stricter question than line coverage: if a decision in a critical pure-logic
surface is changed, do the tests fail? It runs pinned `dotnet-stryker` 4.16.0 against small .NET 8
projects that link the real production source and the real production tests.

The isolation is intentional. Running Stryker from the full legacy solution pulls Buildalyzer into
mixed .NET Framework, C++/CLI, WPF, native, and shared-output project graphs. The profiles here keep
mutation deterministic without copying the production logic. Small dependency contracts are
allowed only when `Test-MutationHarness.ps1` verifies their behavior-bearing identities against the
production source before any test runs.

## Profiles and calibrated gates

The floors are regression gates, not claims that every survivor is a defect. They sit below the
measured 2026-08-08 baseline so small timing or compiler differences do not create a false failure.
The no-coverage ceiling separately prevents a stable score from hiding newly untested code.

| Profile | Risk | Production surface | Quick measured / floor | Full measured / floor | No coverage quick / full |
| --- | --- | --- | --- | --- | --- |
| `ripper-policies` | critical | secure-read recovery, voting, timeout, concealment, slip correlation | 96.14 / 95.0 | 94.42 / 93.0 | 0 / 1 |
| `verification-discovery` | high | album, CUE, playlist, and lossless-source discovery | 90.72 / 89.0 | 85.78 / 84.0 | 2 / 8 |
| `test-copy-history` | critical | Test & Copy resolution, CRC history, bounded persistence | 94.09 / 92.5 | 85.85 / 84.0 | 4 / 29 |
| `naming-path-safety` | high | artifact names, portable paths, and collision handling | 90.20 / 89.0 | 90.13 / 89.0 | 0 / 3 |
| `naming-semantics` | high | token expansion, metadata normalization, and fallbacks | 93.10 / 92.0 | 92.35 / 91.0 | 0 / 7 |
| `output-guard` | critical | non-clobbering publication directory selection | 76.47 / 75.0 | 91.43 / 90.0 | 0 / 2 |
| `artwork-ranking` | medium | release-identity and image-quality ordering | 90.20 / 89.0 | 91.36 / 90.0 | 0 / 0 |

**The measured columns describe `8961b0b6`, not master.** They were taken on 2026-08-08 and
re-confirmed exactly on 2026-08-24 in a worktree at that commit. Master `439decf9` scores far lower
because the production-test half of that branch never landed; see the status note at the top and
`docs/review/2026-08-24-mutation-harness-rebaseline.md` for both measurement sets side by side.

Quick uses Stryker's Basic mutation level and is the pull-request lane. Full uses Standard and is the
scheduled/manual deep lane. Exact source inventories, score floors, and no-coverage ceilings live in
`profiles.json`; changing them is a reviewable quality-policy change.

The 2026-08-08 survivor review split naming into two profiles. Path safety and token semantics now
have independent scores. `NamingPresentation.cs` contains preset names, palette entries, and sample
previews. Those catalogs are covered by exact-value tests but are not mutation-scored because string
replacement mutants measure catalog text, not naming correctness.

On master that split does not exist: `NamingPresentation.cs` was part of the unlanded half, so
`NamingEngine.cs` still carries `Presets`, `PaletteFields`, and `Examples()` inline. Measured
2026-08-24, that costs `naming-semantics` almost nothing - bucketing its mutants by line region
gives 53.57 for the behavior region against 53.33 overall. The missing `NamingMutationContractTests`
is what moved that score, not the catalogs.

The same review added missing behavior contracts for secure-sector voting, C2 weighting, sample
concealment, slip alignment, failed-sector accounting, Test & Copy evidence, CRC history roles,
gzip store boundaries, discovery limits, output collision fallback, and artwork ranking. Remaining
survivors fall into four reviewed groups:

- Equivalent boundaries, such as `<= 0` versus `< 0` when the zero path already produces zero, and
  signed versus unsigned shifts on nonnegative indices.
- Defensive fallbacks that cannot change a valid result, such as secondary ordering after unique
  disc numbers and an extra loop iteration with no body work.
- Error text and default-value mutations where status, exception type, and preservation behavior are
  the asserted contract.
- OS, timeout, and durability branches that need integration or fault-injection tests. These remain
  visible through the no-coverage ceilings instead of being excluded from mutation.

## Run it

From the repository root:

```powershell
# Fast contract and baseline test gate. Does not run mutants.
.\eng\mutation\Test-MutationHarness.ps1 -Build

# Basic mutants for all profiles, or a selected profile.
.\eng\mutation\Invoke-MutationTests.ps1 -Mode Quick
.\eng\mutation\Invoke-MutationTests.ps1 -Mode Quick -Profile output-guard

# Standard mutants for the scheduled/manual deep pass.
.\eng\mutation\Invoke-MutationTests.ps1 -Mode Full
```

**Known failure until D12 is decided.** `Test-MutationHarness.ps1 -Build` stops on
`test-copy-history`, whose test project does not compile (see the status note above), and
`Invoke-MutationTests.ps1` with no `-Profile` stops on the first profile below its floor. To
measure the six runnable profiles today, drive them one at a time with `-Profile` and read the
score out of the run's `mutation-report.json`; the runner writes the report before it throws. The
contract gate alone, `Test-MutationHarness.ps1` without `-Build`, passes.

Every run uses a new `TestResults/Mutation/...` directory by default. It writes a compact summary,
the Stryker JSON and HTML reports, and logs. A failed profile also writes a bounded
`failure-packet.json` with the command, counts, report, log, reason, and replay command. The runner
refuses to reuse a non-empty results directory.

## What belongs here

Add a profile when all of these are true:

1. The behavior is deterministic decision logic with a meaningful wrong answer.
2. The source can be linked without importing a hardware, UI, native, or vendor build graph.
3. Existing production tests can be reused; profile-only tests are limited to verified compile seams.
4. A human reviews the source inventory, survivors, no-coverage set, and initial floor.

Mutation testing is not the strongest oracle for every CUETools surface:

- Optical-drive commands, cache flushing, offsets, and CTDB repair orchestration need hardware and
  fault-injection scenarios.
- Native and vendored codecs need native warning gates, sanitizers where supported, corpus fuzzing,
  and encode/decode differential checks.
- WPF layout and theming need UI automation, screenshot/accessibility checks, and manual visual review.
- File publication and repair transactions need integration, crash-window, and injected I/O failure
  tests in addition to their pure policy profiles.
- Release, signing, installer, and workflow code needs policy tests and clean hosted builds.

Do not chase a percentage by mutating generated code, vendor code, trivial properties, or diagnostics.
Kill meaningful survivors with behavior tests. Record equivalent or unreachable survivors in review
notes, and never hide a broad operator merely to raise the score.
