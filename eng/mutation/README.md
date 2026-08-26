# CUETools mutation harness

> **Baselined 2026-08-24 against current master. All seven profiles pass both gates.**
>
> Run on DESKTOP-D084LOM (Ryzen 9 5950X) under Windows 11 Pro 10.0.26220.0, PowerShell 7.6.5,
> .NET SDK 8.0.422, runtime 8.0.30, `dotnet-stryker` 4.16.0, net8.0 Release. The .NET Framework
> toolchain is not needed: these profiles are standalone net8.0 projects, and
> `Prepare-VendorSources.ps1` is not a prerequisite for them.
>
> Getting here took two steps. The harness did **not** run on master `439decf9` as landed - only
> 7 of its 16 `expectedSources` resolved, because nine mutated sources had moved to
> `CUETools.App.Core/` and a new `StoreJsonContext` seam needed a dependency contract. Then the
> real problem: `e8e21739` landed only `eng/mutation/**`, leaving the production-test half of
> `2a8df3e3` on `origin/agent/mutation-harness`. Roughly 170 test methods the thresholds had been
> calibrated against were simply not on master. That half is now merged (D12, option A), which
> also moved `BuildTestCopyCrcEvidence` out of `RipService` into `TestAndCopyResolver` and
> unblocked `test-copy-history`.
>
> Six thresholds moved; the rest are unchanged from 2026-08-08. `output-guard` was **raised**
> (75.0 -> 81.0 quick, 90.0 -> 91.0 full) because master's own tests improved that surface.
> `verification-discovery` was temporarily lowered for named new code that had arrived without
> mutation-killing tests; the follow-up tests landed on 2026-08-26 (a subst-drive fixture for the
> filesystem-root guard, exact-value storm counters, and the slip-overlap boundary), and its
> floors are back at their original 89.0 quick / 84.0 full. `ripper-policies` now carries zero
> full-mode no-coverage mutants. `docs/review/2026-08-24-mutation-harness-rebaseline.md` is the
> full evidence record.
>
> **Wired into CI on 2026-08-26**: `.github/workflows/mutation.yml` runs Quick on pull requests
> and Full weekly and on manual dispatch, uploading the reports as run artifacts.

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
measured baseline so small timing or compiler differences do not create a false failure. The
no-coverage ceiling separately prevents a stable score from hiding newly untested code.

**Measured columns are the latest recorded runs (2026-08-24 baseline, the two profiles
re-measured 2026-08-26), scored as detected mutants: killed plus timeout, over eligible.** A
timeout is a detection - the mutant made the program hang, which the tests observed, and Stryker
itself scores it as killed. The harness originally counted timeouts in the denominator only,
which made the gate timing-flaky: the same mutant flips between Killed and Timeout run to run on
a loaded machine, and on 2026-08-26 that swung `verification-discovery` between 91.09 and 81.19
with identical code. Under the detected formula both runs score 91.09.

| Profile | Risk | Production surface | Quick measured / floor | Full measured / floor | No coverage quick / full |
| --- | --- | --- | --- | --- | --- |
| `ripper-policies` | critical | secure-read recovery, voting, timeout, concealment, slip correlation | 96.36 / 95.0 | 95.92 / 93.0 | 0 / 0 |
| `verification-discovery` | high | album, CUE, playlist, and lossless-source discovery | 91.09 / 89.0 | 86.49 / 84.0 | 2 / 8 |
| `test-copy-history` | critical | Test & Copy resolution, CRC history, bounded persistence | 94.57 / 92.5 | 85.85 / 84.0 | 4 / 29 |
| `naming-path-safety` | high | artifact names, portable paths, and collision handling | 90.20 / 89.0 | 90.13 / 89.0 | 0 / 3 |
| `naming-semantics` | high | token expansion, metadata normalization, and fallbacks | 93.10 / 92.0 | 92.35 / 91.0 | 0 / 7 |
| `output-guard` | critical | non-clobbering publication directory selection | 82.35 / 81.0 | 92.86 / 91.0 | 0 / 2 |
| `artwork-ranking` | medium | release-identity and image-quality ordering | 90.20 / 89.0 | 91.36 / 90.0 | 0 / 0 |

Quick uses Stryker's Basic mutation level and is the pull-request lane. Full uses Standard and is the
scheduled/manual deep lane. Exact source inventories, score floors, and no-coverage ceilings live in
`profiles.json`; changing them is a reviewable quality-policy change.

The 2026-08-08 survivor review split naming into two profiles. Path safety and token semantics now
have independent scores. `NamingPresentation.cs` contains preset names, palette entries, and sample
previews. Those catalogs are covered by exact-value tests but are not mutation-scored because string
replacement mutants measure catalog text, not naming correctness.

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

The 2026-08-24 re-baseline named four sites in that last group, all in code master wrote after
the original review, and the 2026-08-26 follow-up closed every one. The filesystem-root guard in
`VerificationSourceDiscovery` (the `selected.Length > 1` boundary and the empty-directory branch)
is exercised by a subst-drive fixture: `SubstDrive` in `VerificationSourceDiscoveryTests` maps a
free drive letter onto a shared temp directory so tests can put real manifests at a genuine root,
and the guard itself is internal so its branches are unit-tested directly. The scrubbed counter
text in `DescribeForFailureContext()` is pinned by an exact-value test, per CLAUDE.md's rule that
failure context never carries payload bytes. The `count < n / 2` overlap-sufficiency guard in
`SlipCorrelator` has both a spurious-tiny-overlap rejection test and an exactly-half-overlap
boundary test. Measured after landing: `verification-discovery` 91.09 quick / 86.49 full and
`ripper-policies` 94.10 full with zero no-coverage mutants.

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

All three commands exit 0 as of 2026-08-24. Note that both runners stop at the first profile that
fails, so a red run reports one profile rather than the whole set; drive the rest with `-Profile`
to see them. The runner writes the JSON report before it throws, so a failed profile still leaves a
readable score behind.

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
