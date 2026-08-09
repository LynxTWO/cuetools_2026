# CUETools Upstream Candidates

Queue only repo-agnostic lessons. Local product facts stay in the other calibration files.

## ADC-CUETOOLS-001: Gate environment identity

- Status: promoted
- Scope: repo-agnostic
- Lesson: Bind deterministic gates to reviewed environment overlays and a value-free execution fingerprint.
- Evidence: CUETools nested PowerShell reproduction; shared tests for overlay execution, privacy, and refusal.
- Limits: overlays remain bounded, reviewed, and non-sensitive.
- Proposed target: shared deterministic verification tooling and reference.
- Proposed change: completed in shared version `2026.08.09-unified.5`, commit `729bb40`, PR 1.

## ADC-CUETOOLS-002: Assurance and repository-shape contracts

- Status: promoted
- Scope: repo-agnostic
- Lesson: Strong claims need finalization, branch activation, exact target, reachability, transaction, provenance, and UI-policy falsifiers; mixed plugin/native/release repos need separate dependency and shipping graphs.
- Evidence: CUETools codec, repair, multi-drive, native, external encoder, and release audits plus the bounded mutation harness review.
- Limits: load only the contract sections matching an active finding.
- Proposed target: shared assurance contracts, conventions, architecture, maintenance, and mutation guidance.
- Proposed change: completed in shared version `2026.08.09-unified.5`, commit `729bb40`, PR 1.
