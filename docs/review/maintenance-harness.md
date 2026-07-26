# Maintenance Harness

Pass 10, 2026-07-02; current-state refresh 2026-07-26. Lightweight guardrails to
keep CUETools from drifting back into dark code as new work lands. Kept to what
fits this repo's actual tooling (GitHub Actions + devenv, a docs/ map, MSTest
suites, artifact contracts, and deterministic local gates).

## What was installed

- **`.github/pull_request_template.md`** - repo-fit PR checklist: protected-area gate, docs-update prompts, sensitive-data self-check, and comment/boundary continuity. Names this repo's actual protected areas rather than generic ones.
- **CI test gate:** the canonical suite runner selects parity, codecs, Processor,
  modern ripper, and WPF tests; enforces discovery/skip floors; and runs the
  separate net20 exception-relay probe. The same selection is used by CI and
  release workflows.
- **Release evidence gates:** managed and native warning fingerprints, deterministic
  fuzz smoke, clean artifact contracts, plugin/native load probes, provenance,
  and CycloneDX/SPDX generation are wired into the current workflows.

## Hard gates (enforced by tooling)

- The modern CI lanes run all five selected suites, enforce their count/skip
  contracts, run the net20 probe, and reject new managed/native warning
  fingerprints.
- Releases run the same tests and fuzz gate before packaging. The WPF artifact
  must satisfy its required-file and trust manifests and initialize all five
  packaged native codecs.
- Classic Release still builds Any CPU / x64 / Win32 through `devenv`; hosted
  production of that complete artifact remains required evidence.

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
- `CUETools.Processor\CUEProcessorPlugins.cs` and `PluginTrustManifest.cs`
  (manifest-bound managed/native in-process plugin load)
- `CUETools.CTDB.EACPlugin*` (runs inside EAC; net2.0; main CTDB inbound path)
- credential storage (`ProxyPassword`, Icecast password)
- the release/CI path and submodule patches (`.github/`, `collect_files*.bat`, `ThirdParty/*.patch`)
- the secure-ripping vote / C2 handling in `CUETools.Ripper.SCSI\SCSIDrive.cs`

## Known limits of this harness

- The current local aggregate is 388 discovered, 381 passed, and 7 expected
  skips. Availability-gated WMA and external-fixture cases are not converted
  into passes.
- The modern ripper suite is covered, but the retired
  `CUETools/TestRipper` project still depends on 64 machine-specific hardware
  captures and remains explicitly excluded.
- No automated secret-scanning or comment-continuity check is wired (the repo has no such workflow today); these are reviewer-checklist items only. A future `gh` workflow could add a grep-based secret check and a protected-comment-removal diff check if drift appears.
- CI depends on the VS Enterprise `devenv.com` path and GitHub-hosted Windows image (out-of-repo control surface).

## What still needs harness support later

- Replace the retired TestRipper hardware captures with a deterministic in-memory
  C2/majority fixture.
- Require non-skippable WMA Lossless evidence on a compatible Windows host and
  retain a full hosted classic artifact validation.
- Add signing/attestation and recover immutable provenance for vendored binaries;
  the current SBOM and hashes establish inventory and identity, not publisher
  identity.
- If a crash reporter or analytics is added, a rule to exclude the config object (carries ProxyPassword) from capture.
