# Mutation harness re-baseline, 2026-08-24

## Outcome

**The harness could not be re-baselined, and the thresholds were deliberately left unchanged.**
The blocker is not threshold drift. It is that the mutation harness landed on master without the
production tests it was measured against, so every profile now scores far below its floor for a
reason that is not a code regression.

Status verbs below follow `docs/review/` convention: measured, verified, inferred, rejected.

## Host and toolchain receipt

| Item | Value |
| --- | --- |
| Machine | DESKTOP-D084LOM, AMD Ryzen 9 5950X 16-Core |
| OS | Microsoft Windows 11 Pro Insider Preview, 10.0.26220.0 |
| Shell | PowerShell 7.6.5 |
| .NET SDK | 8.0.422 |
| Runtime | Microsoft.NETCore.App 8.0.30 |
| Stryker | dotnet-stryker 4.16.0, pinned in `eng/mutation/.config/dotnet-tools.json` |
| Target framework | net8.0 |
| Configuration | Release |
| Repository state | master `439decf9`, plus the harness repairs described below |

`eng/ci/Prepare-VendorSources.ps1` was **not** required and was not run. The mutation profiles are
standalone net8.0 projects that link individual first-party `.cs` files; no tracked profile input
references `ThirdParty` or `obj/vendor-sources`. Restore produced no `packages.lock.json` churn.

## The harness did not run at all on current master

The landed README states that all 16 `expectedSources` still resolve. That claim is **rejected**.
On master `439decf9`, only the 7 `CUETools.Ripper.SCSI` entries resolve. All 9 `CUETools.Wpf`
entries are missing, and 5 `expectedTests` entries are missing.

Four separate causes, all measured:

1. **The App.Core extraction.** Nine mutated sources moved from `CUETools.Wpf/` to
   `CUETools.App.Core/` with identical subpaths. The same move broke the `RepairEvidence.cs` and
   `AlbumOutputTransaction.cs` contract assertions in `Test-MutationHarness.ps1`. Repaired by
   repointing the paths. The files kept their `CUETools.Wpf.*` namespaces, so no other change was
   needed.
2. **Four files that never landed.** `CUETools.Ripper.Tests/SecureSectorVoteTests.cs`,
   `CUETools.Wpf.Tests/NamingMutationContractTests.cs`,
   `CUETools.Wpf.Tests/NamingPresentationTests.cs`, and
   `CUETools.Wpf/Services/NamingPresentation.cs` exist only on `origin/agent/mutation-harness`,
   added by `2a8df3e3`. The landing commit `e8e21739` took 41 files, all under `eng/mutation/`,
   and left these behind. Their references were removed so the harness could run.
3. **A new source-generated serializer context.** `GzJson.cs` and `VerifyHistory.cs` now resolve
   through `StoreJsonContext`, declared in `CUETools.App.Core/Services/StoreJsonContext.cs`.
   Linking that file would pull `HistoryStore` and `CUETools.Wpf.Models` into the isolated graph.
   Added `eng/mutation/profiles/TestCopyHistory/Target/StoreJsonContract.cs` as a dependency
   contract registering only the two roots the linked sources use, and pinned the production
   declaration in `Test-MutationHarness.ps1` per the harness rule for seams.
4. **`RipService` in a production test file.** `CUETools.Wpf.Tests/TestAndCopyResolverTests.cs`
   now calls `RipService.BuildTestCopyCrcEvidence`. `RipService.cs` is 3223 lines and pulls
   `CUETools.AccurateRip`, `Codecs`, `CTDB`, `Processor`, `Ripper`, and `Ripper.SCSI`. This is the
   exact legacy graph the profiles exist to avoid, and the method is behavior-bearing logic rather
   than an identity, so a contract shim would be dishonest. **`test-copy-history` therefore cannot
   run.** The profile was left in place, per the standing instruction not to delete a profile that
   cannot run.

## Measured: current master

Quick mode, Basic mutation level, 2026-08-24. `test-copy-history` is blocked and produced no
number. Floors and ceilings shown are the unchanged 2026-08-08 values.

| Profile | Risk | Score | Floor | No coverage | Ceiling | Killed | Survived |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `ripper-policies` | critical | **78.18** | 95.0 | 0 | 0 | 258 | 70 |
| `verification-discovery` | high | **48.11** | 89.0 | 14 | 2 | 51 | 41 |
| `test-copy-history` | critical | blocked | 92.5 | n/a | 4 | n/a | n/a |
| `naming-path-safety` | high | **78.43** | 89.0 | 0 | 0 | 40 | 11 |
| `naming-semantics` | high | **53.33** | 92.0 | 10 | 0 | 48 | 32 |
| `output-guard` | critical | **58.82** | 75.0 | 4 | 0 | 10 | 3 |
| `artwork-ranking` | medium | **32.00** | 89.0 | 17 | 0 | 16 | 17 |

Every runnable profile fails its floor, and four of six also breach their no-coverage ceiling.

Full mode, Standard mutation level, same day and host:

| Profile | Risk | Score | Floor | No coverage | Ceiling | Killed | Survived |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `ripper-policies` | critical | **76.19** | 93.0 | 4 | 1 | 336 | 92 |
| `verification-discovery` | high | **41.48** | 84.0 | 42 | 8 | 95 | 92 |
| `test-copy-history` | critical | blocked | 84.0 | n/a | 29 | n/a | n/a |
| `naming-path-safety` | high | **73.03** | 89.0 | 8 | 3 | 111 | 33 |
| `naming-semantics` | high | **31.34** | 91.0 | 119 | 7 | 136 | 179 |
| `output-guard` | critical | **61.43** | 90.0 | 17 | 2 | 43 | 10 |
| `artwork-ranking` | medium | **29.11** | 90.0 | 32 | 0 | 23 | 24 |

Full mode fails every runnable profile on both score and no-coverage. The README records the
2026-08-08 Full measurements as 94.42, 85.78, 85.85, 90.13, 92.35, 91.43, and 91.36. Standard
level generates more mutants than Basic, so the missing tests cost more here: `naming-semantics`
alone now carries 119 no-coverage mutants against a ceiling of 7.

## Verified: the baseline commit still reproduces its documented numbers

To separate "the harness is wrong" from "the tests are missing", the same seven profiles were run
unmodified in a detached worktree at `8961b0b6`, the tip of `origin/agent/mutation-harness`.

**All seven pass, and every score matches the landed README to the hundredth.**

| Profile | README documented | Reproduced 2026-08-24 | Master today |
| --- | --- | --- | --- |
| `ripper-policies` | 96.14 | **96.14** | 78.18 |
| `verification-discovery` | 90.72 | **90.72** | 48.11 |
| `test-copy-history` | 94.09 | **94.09** | blocked |
| `naming-path-safety` | 90.20 | **90.20** | 78.43 |
| `naming-semantics` | 93.10 | **93.10** | 53.33 |
| `output-guard` | 76.47 | **76.47** | 58.82 |
| `artwork-ranking` | 90.20 | **90.20** | 32.00 |

This is a verified result, not an inference. The harness is deterministic and sound, and the
2026-08-08 thresholds were honestly measured. The collapse is entirely on the master side.

## Why master scores low: the tests were never landed

Commit `2a8df3e3` hardened the production tests and the harness together. Only the harness half
landed. Test method counts, counted as `[TestMethod]` occurrences:

| Test file | master | branch | Delta |
| --- | --- | --- | --- |
| `CUETools.Wpf.Tests/VerificationSourceDiscoveryTests.cs` | 12 | 36 | -24 |
| `CUETools.Wpf.Tests/NamingMutationContractTests.cs` | 0 | 19 | -19 |
| `CUETools.Wpf.Tests/VerifyHistoryStoreTests.cs` | 9 | 24 | -15 |
| `CUETools.Wpf.Tests/TestAndCopyResolverTests.cs` | 20 | 31 | -11 |
| `CUETools.Wpf.Tests/ArtworkCandidateTests.cs` | 3 | 12 | -9 |
| `CUETools.Ripper.Tests/SecureSectorVoteTests.cs` | 0 | 6 | -6 |
| `CUETools.Ripper.Tests/SampleConcealmentTests.cs` | 6 | 12 | -6 |
| `CUETools.Wpf.Tests/OutputGuardTests.cs` | 10 | 15 | -5 |
| `CUETools.Ripper.Tests/SlipCorrelatorTests.cs` | 3 | 8 | -5 |
| `CUETools.Wpf.Tests/AlbumArtifactNamesTests.cs` | 2 | 7 | -5 |
| `CUETools.Wpf.Tests/GzJsonTests.cs` | 8 | 13 | -5 |
| `CUETools.Wpf.Tests/NamingPathsTests.cs` | 14 | 17 | -3 |
| `CUETools.Ripper.Tests/FailedSectorAccountingTests.cs` | 8 | 11 | -3 |
| `CUETools.Wpf.Tests/NamingPresentationTests.cs` | 0 | 3 | -3 |
| `CUETools.Wpf.Tests/NamingTokenTests.cs` | 3 | 5 | -2 |
| `CUETools.Ripper.Tests/RecoveryPolicyTests.cs` | 5 | 6 | -1 |
| `CUETools.Ripper.Tests/ReadTimeoutPolicyTests.cs` | 4 | 4 | 0 |
| `CUETools.Ripper.Tests/PayloadReadFailurePolicyTests.cs` | 30 | 23 | +7 |

The ordering matches the score drops. `artwork-ranking` fell furthest, -58.20, and retains 3 of 12
tests. `verification-discovery` fell -42.61 and retains 12 of 36.

One secondary observation, **measured and minor**: master's `NamingEngine.cs` is 388 lines and
still holds the `Presets`, `PaletteFields`, and `Examples()` catalogs inline, because the split
into `NamingPresentation.cs` was part of the unlanded half. The README predicted this would
depress `naming-semantics`. It does not explain the drop: bucketing that profile's mutants by line
region gives a behavior-region-only score of 53.57 against 53.33 overall. The missing
`NamingMutationContractTests` is the cause, not the catalogs.

## Why the thresholds were not rewritten

The instruction was to set each threshold from what the code actually scores now. Applied
literally here, that would write 32.00 in as the `artwork-ranking` floor and 48.11 as
`verification-discovery`, permanently encoding the loss of tests that were written, reviewed, and
measured, and that still exist on a branch. A gate calibrated to a suite missing two thirds of its
assertions is worse than no gate, because it reports green.

Nothing in the mutated production sources regressed. The measurement apparatus lost its tests.
Those are different problems, and only one of them is fixed by moving a number.

## Decision needed

Recorded as D12 in `docs/review/decisions-needed.md`. In short: land the production-test half of
`2a8df3e3` onto master and then re-baseline against the restored suite, or accept the reduced
suite and re-baseline to the numbers above. The second option should be taken only deliberately.

`test-copy-history` needs its own answer either way, because `BuildTestCopyCrcEvidence` is
Test-and-Copy evidence logic that now sits inside an unlinkable 3223-line service.
