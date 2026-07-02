# Maintenance Harness

Pass 10, 2026-07-02. Lightweight guardrails to keep CUETools from drifting back into dark code as new work lands. Kept to what fits this repo's actual tooling (GitHub Actions + devenv, a docs/ map, MSTest suites).

## What was installed

- **`.github/pull_request_template.md`** — repo-fit PR checklist: protected-area gate, docs-update prompts, sensitive-data self-check, and comment/boundary continuity. Names this repo's actual protected areas rather than generic ones.
- **CI test gate (already live, safe-fix batch 1):** `dotnet test` on TestParity and TestCodecs in both `CI-windows.yml` and `release-windows.yml`. Regressions in the covered parity/codec math block the build and releases.

## Hard gates (enforced by tooling)

- CI builds Release for Any CPU / x64 / Win32 and runs the two green suites; a red suite fails the run.
- Releases run the same suites before packaging (`release-windows.yml`).

## Reviewer guidance (human, not automated)

The PR template asks the reviewer to confirm, for each PR:
- whether a protected area was touched (CRC repair gate, plugin loader, EAC plugin/installer, credential storage, CI/release path, secure-ripping vote) and how it was approved;
- whether docs that describe the changed area were updated in the same PR;
- whether any trust-boundary / invariant comment was removed without preserving its meaning;
- that no sensitive value was added to any surface.

Do not label these as automated guarantees; they depend on the reviewer reading the diff.

## Protected areas (approval required before edit)

From `00-conventions.md` plus repo specifics verified this session:
- the CRC self-check in `CUETools.AccurateRip\CDRepair.cs` (repair correctness)
- `CUETools.Processor\CUEProcessorPlugins.cs` (unsigned in-process plugin load)
- `CUETools.CTDB.EACPlugin*` (runs inside EAC; net2.0; main CTDB inbound path)
- credential storage (`ProxyPassword`, Icecast password)
- the release/CI path and submodule patches (`.github/`, `collect_files*.bat`, `ThirdParty/*.patch`)
- the secure-ripping vote / C2 handling in `CUETools.Ripper.SCSI\SCSIDrive.cs`

## Known limits of this harness

- The test gate covers only TestParity + TestCodecs. The engine (TestProcessor) and ripper (TestRipper) suites cannot run yet (missing fixtures / hardware), so regressions there are not caught automatically. Tracked in `docs/unknowns/coverage-pass.md`.
- No automated secret-scanning or comment-continuity check is wired (the repo has no such workflow today); these are reviewer-checklist items only. A future `gh` workflow could add a grep-based secret check and a protected-comment-removal diff check if drift appears.
- CI depends on the VS Enterprise `devenv.com` path and GitHub-hosted Windows image (out-of-repo control surface).

## What still needs harness support later

- A `dotnet test` step for TestProcessor once synthetic CUE fixtures exist.
- Dependabot / SBOM for the vendored binaries once they move to NuGet (modernization idea 7).
- If a crash reporter or analytics is added, a rule to exclude the config object (carries ProxyPassword) from capture.
