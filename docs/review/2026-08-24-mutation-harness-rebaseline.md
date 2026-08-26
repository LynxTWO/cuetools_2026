# Mutation harness re-baseline, 2026-08-24

## Outcome

**The harness runs on master, all seven profiles pass, and the thresholds are measured from a
restored test suite.** Getting there took two steps that the work order did not anticipate: the
harness did not run at all as landed, and the reason was that its companion production tests were
never merged.

The owner's decision on D12 was option A: land the missing test half, then re-baseline against it.
That is what this document records.

Status verbs follow `docs/review/` convention: measured, verified, inferred, rejected.

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
| Starting point | master `439decf9` |

`eng/ci/Prepare-VendorSources.ps1` is **not** a prerequisite for these profiles. They are standalone
net8.0 projects that link individual first-party `.cs` files; no tracked profile input references
`ThirdParty` or `obj/vendor-sources`. Restore produced no `packages.lock.json` churn.

## Step 1: the harness did not run on master as landed

The landed README claimed all 16 `expectedSources` still resolved. That claim is **rejected**. On
master `439decf9` only the 7 `CUETools.Ripper.SCSI` entries resolved. Four separate causes:

1. **The App.Core extraction.** Nine mutated sources moved from `CUETools.Wpf/` to
   `CUETools.App.Core/` with identical subpaths, which also broke the `RepairEvidence.cs` and
   `AlbumOutputTransaction.cs` contract assertions. The files kept their `CUETools.Wpf.*`
   namespaces, so repointing the paths was sufficient.
2. **Four files that never landed.** The landing commit `e8e21739` took 41 files, all under
   `eng/mutation/`, leaving the production half of `2a8df3e3` on `origin/agent/mutation-harness`.
3. **A new source-generated serializer context.** `GzJson.cs` and `VerifyHistory.cs` now resolve
   through `StoreJsonContext`. Linking the production file would pull `HistoryStore` and
   `CUETools.Wpf.Models` into the isolated graph, so
   `eng/mutation/profiles/TestCopyHistory/Target/StoreJsonContract.cs` registers only the two roots
   the linked sources use, with the production declaration pinned in `Test-MutationHarness.ps1`.
4. **`RipService` reached from a production test.** Resolved by step 2; see below.

## Step 2: landing the missing test half

Commit `2a8df3e3` changed the harness and the production tests together, and only the harness half
landed. The branch is exactly two commits off merge base `ad424f1c`, while master is 159 commits
ahead, so each file was brought across with a three-way merge rather than an overwrite. That
distinction matters: master had independently **gained** tests in
`PayloadReadFailurePolicyTests.cs` (30 methods against the branch's 23), and an overwrite would
have silently deleted seven of them. All 15 modified files merged with no conflicts.

Landed: 15 merged test files, 3 new test files, and 4 production sources. Deliberately **not**
landed, because wiring is a separate decision: `.github/workflows/mutation.yml`, `CI-wpf.yml`,
`Test-NuGetLockFiles.ps1`, and `Test-WorkflowActionPins.ps1`.

Test method counts, before and after, counted as `[TestMethod]` occurrences:

| Test file | before | after |
| --- | --- | --- |
| `CUETools.Wpf.Tests/VerificationSourceDiscoveryTests.cs` | 12 | 36 |
| `CUETools.Wpf.Tests/VerifyHistoryStoreTests.cs` | 9 | 24 |
| `CUETools.Wpf.Tests/NamingMutationContractTests.cs` | 0 | 19 |
| `CUETools.Wpf.Tests/TestAndCopyResolverTests.cs` | 20 | 31 |
| `CUETools.Wpf.Tests/ArtworkCandidateTests.cs` | 3 | 12 |
| `CUETools.Ripper.Tests/SampleConcealmentTests.cs` | 6 | 12 |
| `CUETools.Wpf.Tests/OutputGuardTests.cs` | 10 | 15 |
| `CUETools.Ripper.Tests/SlipCorrelatorTests.cs` | 3 | 8 |
| `CUETools.Ripper.Tests/SecureSectorVoteTests.cs` | 0 | 6 |
| `CUETools.Wpf.Tests/GzJsonTests.cs` | 8 | 13 |
| `CUETools.Wpf.Tests/AlbumArtifactNamesTests.cs` | 2 | 7 |
| `CUETools.Ripper.Tests/PayloadReadFailurePolicyTests.cs` | 30 | 30 |

**Measured:** `CUETools.Wpf.Tests` went from 561 to 730 passing, `CUETools.Ripper.Tests` holds at
97 passing. All 169 restored tests pass against master's production code with no rework, because
the merge carried their production-side changes with them.

### The `test-copy-history` blocker resolved itself

Before landing, `CUETools.Wpf.Tests/TestAndCopyResolverTests.cs` called
`RipService.BuildTestCopyCrcEvidence`, and `RipService.cs` is 3223 lines pulling AccurateRip,
Codecs, CTDB, Processor, Ripper, and Ripper.SCSI. That is the exact legacy graph the profiles
isolate against, and the method is behavior-bearing logic rather than an identity, so it did not
qualify for a contract shim.

The branch had already solved this: `2a8df3e3` moves the method to
`TestAndCopyResolver.BuildCrcEvidence` and repoints its four call sites. **Verified** that master's
copy of the method body is byte-identical to the merge base, so the move applied cleanly.
`TestAndCopyResolver.cs` is already a linked and mutated source, so the logic is now scored rather
than unreachable.

## Step 3: the re-baseline

All seven profiles pass both gates in a single command. `Invoke-MutationTests.ps1 -Mode Quick` and
`-Mode Full` both exit 0.

| Profile | Risk | Quick measured / floor | Full measured / floor | No coverage quick / full |
| --- | --- | --- | --- | --- |
| `ripper-policies` | critical | 95.76 / 95.0 | 93.42 / 93.0 | 0 / 3 |
| `verification-discovery` | high | 88.12 / 87.0 | 83.33 / 82.0 | 2 / 10 |
| `test-copy-history` | critical | 94.12 / 92.5 | 85.61 / 84.0 | 4 / 29 |
| `naming-path-safety` | high | 90.20 / 89.0 | 90.13 / 89.0 | 0 / 3 |
| `naming-semantics` | high | 93.10 / 92.0 | 92.35 / 91.0 | 0 / 7 |
| `output-guard` | critical | 82.35 / 81.0 | 92.86 / 91.0 | 0 / 2 |
| `artwork-ranking` | medium | 90.20 / 89.0 | 91.36 / 90.0 | 0 / 0 |

Six thresholds moved. Every other floor and ceiling is unchanged from 2026-08-08.

**Raised, because master's own tests improved the surface:**

| Threshold | From | To | Measured |
| --- | --- | --- | --- |
| `output-guard` quick floor | 75.0 | **81.0** | 82.35 |
| `output-guard` full floor | 90.0 | **91.0** | 92.86 |

`OutputGuardTests.cs` grew from 10 to 15 methods, and the quick score rose from the 2026-08-08
measurement of 76.47 to 82.35. Leaving the floor at 75.0 would have let a seven-point regression
pass silently.

**Lowered, with a named cause:**

| Threshold | From | To | Measured |
| --- | --- | --- | --- |
| `verification-discovery` quick floor | 89.0 | **87.0** | 88.12 |
| `verification-discovery` full floor | 84.0 | **82.0** | 83.33 |
| `verification-discovery` full no-coverage | 8 | **10** | 10 |
| `ripper-policies` full no-coverage | 1 | **3** | 3 |

These are not the restored suite falling short. They are new production code that master added
after the 2026-08-08 baseline, arriving without mutation-killing tests. That is exactly what the
gate exists to surface, so the sites are named here rather than left as a number.

`verification-discovery` lost 2.60 points against its 2026-08-08 measurement of 90.72. **Measured**
by mapping each surviving mutant to the diff against merge base: three of the ten survivors sit in
code master added, and all three are in the `IsFilesystemRoot` guard that stops a multi-file
selection whose only shared ancestor is the filesystem root from scoping a run to the whole disk.
Line 134 carries two survivors on the `selected.Length > 1` boundary, and line 459 carries a block
removal on the empty-directory guard. Killing them needs real files at a drive root, which is a
fixture question rather than a missing assertion, so they are recorded instead of chased.

`ripper-policies` keeps its score floor and moves only the ceiling. The three no-coverage mutants
are `PayloadReadFailurePolicy.cs:563-564`, two string mutants inside
`DescribeForFailureContext()`, and `SlipCorrelator.cs:49`, the `count < n / 2` overlap-sufficiency
guard. The first two fall in the README's reviewed "error text and default-value" group and mutate
scrubbed diagnostic counters rather than policy behavior.

Both are worth follow-up work, and neither is a regression in a shipping decision path.

## Two follow-ups, not done here

1. A drive-root fixture would let `verification-discovery` recover its 89.0 floor by killing the
   three `IsFilesystemRoot` mutants.
2. A test pinning `DescribeForFailureContext()` output would kill two of the three
   `ripper-policies` no-coverage mutants and is independently worth having, because CLAUDE.md
   requires scrubbed failure context that never includes sector payload bytes.

Both are new tests rather than merges, so they are left as reviewable quality changes.

**Completed 2026-08-26.** The drive-root fixture is `SubstDrive` in
`VerificationSourceDiscoveryTests`: it maps a free drive letter onto one shared temp directory
(the programmatic subst), so tests place real manifests at a genuine filesystem root without
touching an actual drive root, and every concurrent Stryker test session can stack the identical
mapping harmlessly. `IsFilesystemRoot` became internal so its empty-string and root/non-root
branches are unit-tested directly. The storm counters got their exact-value pin, and the
`SlipCorrelator` overlap guard got a spurious-tiny-overlap rejection test plus an
exactly-half-overlap boundary test, which also cleared the third no-coverage mutant.

Measured after landing: `verification-discovery` 91.09 quick / 86.49 full, floors restored to
their original 89.0 / 84.0 with the full no-coverage ceiling back at 8; `ripper-policies` 95.92
full with a no-coverage ceiling of zero. Quick-mode `ripper-policies` generates no string mutants
at Basic level, so its quick score moved only with the scoring correction below.

**Scoring correction, same day.** Proving the gate exposed a flake: two runs of identical code
scored `verification-discovery` 91.09 and then 81.19, and the report diff showed the entire swing
was ten mutants flipping between Killed and Timeout. A timeout is a detection - the mutant made
the program hang, which the tests observed, and Stryker itself scores timeouts as killed - but
the harness counted timeouts in the denominator only, so a timing flip on a loaded machine moved
the score. `Get-MutationCounts` now scores detected mutants, killed plus timeout, over eligible;
under that formula both runs score 91.09 exactly. The self-test in `Test-MutationHarness.ps1`
pins a timeout inside the boundary fixture so the formula cannot silently regress. Floors were
not moved for this: detection scores are equal or higher than the old formula's, so every
existing floor remains a valid minimum, and the README's measured columns were recomputed from
the recorded reports.

## Wired into CI, 2026-08-26

The harness gated nothing when this document was written; `mutation.yml` stayed on the branch on
purpose until the floors were trustworthy. With the follow-up tests landed, the floors restored,
and the timeout scoring corrected, the owner approved wiring. `.github/workflows/mutation.yml`
now runs Quick as the pull-request lane and Full weekly and on manual dispatch, with the same
pinned action set as the other lanes and the Stryker reports uploaded as run artifacts. The
reviewed pin counts in `eng/ci/Test-WorkflowActionPins.ps1` moved with it.
