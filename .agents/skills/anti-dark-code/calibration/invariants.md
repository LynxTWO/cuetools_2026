# CUETools Invariants

Freshness: reviewed at commit `2a8df3e3` on 2026-08-09. Recheck after changes to `CLAUDE.md`, `Directory.Build.props`, `eng/ci`, `eng/release`, `eng/mutation`, plugin discovery, ripping evidence, or publication transactions.

This file indexes load-bearing repo truth. `CLAUDE.md` is the canonical operating policy; the referenced tests and manifests are the falsifiers.

## Build, Dependency, and Release

- Rule: vendor submodule worktrees stay immutable; patched or generated vendor sources are built from the owned staging tree.
  - Evidence: `CLAUDE.md` Vendor and dependency preparation; `eng/ci/Test-VendorSourceStaging.ps1`; `eng/ci/Test-VendorSubmodulesClean.ps1`.
  - Confidence: verified by deterministic policy tests; real toolchain builds remain target-specific.
- Rule: managed restores use committed lockfiles and locked mode; changing dependencies is an intentional lock regeneration event.
  - Evidence: `Directory.Build.props`; `eng/ci/Test-NuGetLockFiles.ps1`; hosted workflows.
  - Confidence: verified for configured consumers; invalidated by project, package, SDK, or workflow changes.
- Rule: classic release clean, build, receipt, collection, validation, signing, and publication stay under the canonical release orchestrator and lease.
  - Evidence: `CLAUDE.md` Build and release orchestration; `eng/release/Invoke-ClassicRelease.ps1`; release policy tests.
  - Confidence: verified as a policy contract; exact artifact claims require the matching hosted or local release receipt.

## Ripping, Verification, and Publication

- Rule: Test, Copy, repair, and final-output proof keep distinct semantic roles; later failure must not erase completed phase evidence.
  - Evidence: `CLAUDE.md` Test and Copy and verification history; `eng/mutation/profiles/TestCopyHistory`; focused production tests.
  - Confidence: verified for pure contracts; optical-drive and crash-window branches retain their hardware or fault-injection boundary.
- Rule: a verification, repair, or bit-exact claim covers finalization and the exact published bytes, not merely successful writes or partial frames.
  - Evidence: `docs/review/code-audit-prompt.md`; `docs/review/flac-verify-on-encode-finding.md`; release artifact validators; codec tests.
  - Confidence: target-specific. Do not generalize one codec, wrapper, output shape, or architecture to another.
- Rule: repair and output publication preserve user identity metadata intentionally, recompute payload-dependent proof, and never clobber an unrelated destination.
  - Evidence: `CLAUDE.md` output and repair boundaries; `docs/review/r12-naming-system-design.md`; output-guard and naming mutation profiles.
  - Confidence: verified for covered pure policies; filesystem transaction and failure windows need integration evidence.

## Runtime and UI

- Rule: an optical drive is owned by one operation at a time, while independent drives may use independent sessions; selection and displayed capability state remain bound to physical identity.
  - Evidence: `CLAUDE.md` multi-drive session policy; WPF session and drive tests.
  - Confidence: verified for modeled state; simultaneous physical-drive exercise is hardware evidence.
- Rule: UI progress, animation, and notifications observe durable work and cannot change producer correctness or cleanup.
  - Evidence: `CLAUDE.md` UI and thread-affinity policy; WPF tests; `docs/review/r12-3d-disc-visualization-design.md`.
  - Confidence: verified for covered state contracts; rendered-window and allocation claims require UI/runtime measurements.

## Mutation Evidence

- Rule: mutation scores are scoped profile evidence. Report absolute mutant counts, no-coverage counts, exclusions, elapsed time, and survivor classification; do not treat display catalogs or equivalent mutants as semantic defects.
  - Evidence: `eng/mutation/README.md`; `eng/mutation/profiles.json`; `eng/mutation/Test-MutationHarness.ps1`.
  - Confidence: verified at the recorded profile baselines; invalidate when source inventory, linked tests, Stryker, floors, or no-coverage ceilings change.
