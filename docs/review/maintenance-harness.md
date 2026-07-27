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
- **Vendor-source staging:** `Prepare-VendorSources.ps1` materializes four pinned
  submodules plus tracked patches into an identity-bound ignored tree. CI and release
  run its contract tests and fail if a real build changes a submodule worktree.

## Hard gates (enforced by tooling)

- The modern CI lanes run all five selected suites, enforce their count/skip
  contracts, run the net20 probe, and reject new managed/native warning
  fingerprints.
- Restore and build consume `obj/vendor-sources/current`; direct in-place patching is
  unsupported. `Test-VendorSubmodulesClean.ps1` checks every initialized gitlink
  after CI and release builds.
- Releases run the same tests and fuzz gate before packaging. The WPF artifact
  must satisfy its required-file and trust manifests and initialize all five
  packaged native codecs. Both release contracts require the per-user plugin
  enrollment script; its focused gate rejects extra files, unknown directories,
  reparse points, implicit replacement, incorrect hashes, and merged publication.
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

- Aggregate discovered/pass/skip totals are refreshed by the final canonical gate
  after current changes land; older totals are historical. Declared fixture skips
  are not converted into passes. WMA Lossless also has separate passing live net8
  encode/finalize/independent-decode evidence.
- The modern ripper suite is covered; the newly modernized
  `CUETools/TestRipper` canonical run passed 17/17 with zero skips. Its private
  captures and stale copied vote algorithm are gone: SDK net47 tests call the same
  `SecureSectorVote.CorrectSector` helper as shipping `SCSIDrive`, using deterministic
  flagged/unflagged corruption, insufficient-clean-pass confidence, and C2-plane
  reconstruction without false secure assurance. The canonical contract requires
  17 tests and zero skips.
- No automated secret-scanning or comment-continuity check is wired (the repo has no such workflow today); these are reviewer-checklist items only. A future `gh` workflow could add a grep-based secret check and a protected-comment-removal diff check if drift appears.
- CI depends on the VS Enterprise `devenv.com` path and GitHub-hosted Windows image (out-of-repo control surface).
- The focused net20 exception-relay probe passes, but it is not a whole-solution net20
  certification. Classic passes found and fixed a tuple-metadata blocker in
  slip-correlation and a net35-only `SortedSet<T>` in adaptive speed. The resulting
  classic AnyCPU solution build is green (58 succeeded, 0 failed, 11 skipped).
  The x64 and Win32 configurations each pass at 9 succeeded, 0 failed, and 60 skipped
  configuration
  entries; TTA compiles and links for both. The targeted Installer Projects build
  passes 8/0 and produces a 929,792-byte MSI.

## What still needs harness support later

- Keep the passing canonical TestRipper production-helper fixture in the normal CI
  selection with its 17-test/zero-skip floor.
- Retain the passing WMA Lossless, FLACCL/RTX 3060, H:/K: optical, H: Test & Copy,
  CTDB repair, Icecast 2.5.0, and local actionlint evidence in repeatable release
  lanes. Repeat H: Test & Copy against final source after the `SecureSectorVote`
  extraction; add cross-vendor, TLS/certificate, and deliberate hardware-failure
  cases.
- Preserve the K: 24-track damaged-disc lane as the completed-rip CTDB repair oracle:
  final lossless proof must succeed before publication, the Rip page must expose the
  recoverable sector count, and repair must publish a verified sibling. The repaired
  audio basenames, standard and unknown tags, and embedded artwork must match their
  source representation. The original top-level aggregate hash must remain unchanged.
- Retain the passing frozen classic receipt and exact 97-file artifact, then repeat
  the local AnyCPU/x64/Win32/TTA/MSI matrix on the pinned hosted VS2022 image.
  The local route used Visual Studio 18.8 with the VS2022 v143 toolset. The exact
  release bound 95 inputs, 61 native warning lines, and eight known fingerprints.
- Add signing/attestation. The four patched submodules now stay clean and detached at
  their recorded gitlinks; the ignored stage binds their commits, patches, and file
  manifests. The current SBOM, vendor evidence, and hashes establish inventory and
  byte identity, not publisher identity.
- If a crash reporter or analytics is added, a rule to exclude the config object (carries ProxyPassword) from capture.
