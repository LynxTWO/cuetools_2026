# CUETools Anti-Dark-Code Findings Calibration

Freshness: reviewed at commit `2a8df3e3` on 2026-08-09.

Canonical product findings remain in `docs/review/remediation-backlog.md`, the dated review documents, and `docs/unknowns`. This file records only migration and shared-skill decisions.

## Open

None introduced by the skill migration.

## Fixed

| ID | Fix | Regression guard | Evidence | Closed |
|---|---|---|---|---|
| ADC-MIG-001 | Replaced the tracked Claude-only policy copy with the canonical managed core plus thin host adapter | installed manifest validation and repo binding | `.agents/skills/anti-dark-code`; `.claude/skills/anti-dark-code/SKILL.md` | 2026-08-09 |
| ADC-MIG-002 | Promoted CUETools and mutation-harness lessons into shared v5 assurance and gate contracts | shared 53-test suite and upstream PR | shared commit `729bb40`; anti-dark-code-skill PR 1 | 2026-08-09 |

## Refuted

| ID | Original claim | Refuting evidence | Limits | Closed |
|---|---|---|---|---|
| ADC-GATE-001 | Root `dotnet test` is an appropriate exact repository gate | mixed legacy, native, vendor, WPF, and isolated mutation project graph; canonical `Invoke-TestSuites.ps1` manifest | a future solution and test-orchestrator redesign may change this | 2026-08-09 |

## Deferred

Heavier build, mutation, hardware, classic release, and hosted gates remain under their existing orchestrators. They are not implicitly approved by this calibration migration.
