# CUETools mutation harness

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

| Profile | Risk | Production surface | Quick measured / floor | Full measured / floor |
| --- | --- | --- | --- | --- |
| `ripper-policies` | critical | secure-read recovery, voting, timeout, concealment, slip correlation | 77.49 / 76.0 | 75.97 / 74.5 |
| `verification-discovery` | high | album, CUE, playlist, and lossless-source discovery | 77.45 / 76.0 | 70.18 / 68.5 |
| `test-copy-history` | critical | Test & Copy resolution, CRC history, bounded persistence | 85.00 / 83.5 | 70.28 / 68.5 |
| `naming-safety` | high | portable album/track names and path collision handling | 77.30 / 74.0 | 54.10 / 52.5 |
| `output-guard` | critical | non-clobbering publication directory selection | 70.59 / 69.0 | 68.57 / 67.0 |
| `artwork-ranking` | medium | release-identity and image-quality ordering | 78.00 / 76.5 | 77.22 / 75.5 |

Quick uses Stryker's Basic mutation level and is the pull-request lane. Full uses Standard and is the
scheduled/manual deep lane. Exact source inventories, score floors, and no-coverage ceilings live in
`profiles.json`; changing them is a reviewable quality-policy change.

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
