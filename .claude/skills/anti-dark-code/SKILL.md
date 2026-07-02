---
name: anti-dark-code
description: Use when working in an unfamiliar or messy codebase and the user asks to map a repo, explain what runs where, identify trust boundaries or sensitive-data paths, comment critical logic without changing behavior, audit logs or telemetry for leaks, build coverage ledgers for large repos, challenge architecture understanding with adversarial or scenario reviews, preserve localization or transcreation boundaries, turn findings into a remediation backlog, or install steering files and maintenance guardrails. Trigger keywords - dark code, legacy code review, critical paths, approval gates, unknowns files, anti-dark-code, hidden control flow, transcreation, localization, i18n, "we do not understand this repo".
---

# Anti-Dark-Code

Turn an unfamiliar repo into a mapped, documented, evidence-backed codebase. Run one bounded pass at a time, load only the reference file for that pass, and stop when approval gates or evidence gaps appear.

## Start Here

1. Run pass `00` first to sniff repo shape and pick the right entry point.
2. Map the user's request to the earliest matching pass.
3. Read only the reference file for the active pass.
4. Check whether prerequisite docs already exist and are still current.
5. Respect the repo's existing doc paths; if none exist, use the defaults in `references/00-conventions.md`.
6. Record unknowns instead of smoothing them over.
7. Stop when the slice is complete, an approval-gated edit is next, or a bound is reached.

## Pass Router

- `00` Preflight: read `references/00-preflight.md`
  Use to sniff repo type, check whether prior anti-dark-code artifacts are still fresh, and decide whether full mode or mini-mode applies.
- `01` Steering files: read `references/01-steering.md`
  Use for `AGENTS.md`, tool-specific steering siblings, approval gates, sensitive-data rules, and repo-specific operating guidance.
- `02` Architecture map: read `references/02-architecture-map.md`
  Use to map runtime units, entrypoints, stores, external dependencies, and trust boundaries.
- `03` Critical-path comments: read `references/03-critical-path-comments.md`
  Use to add why-focused comments on risky paths without changing behavior.
- `04` Logging and telemetry audit: read `references/04-logging-audit.md`
  Use to inspect logs, analytics, tracing, and error paths for leaks or over-collection.
- `05` Coverage and slicing: read `references/05-coverage-slicing.md`
  Use when the repo is too large for one honest pass.
- `06` Writing hygiene: read `references/06-writing-hygiene.md`
  Use after comment or docs passes to remove vague, inflated, or AI-sloppy text.
- `07` Adversarial review: read `references/07-adversarial-review.md`
  Use to challenge coverage claims, trust-boundary assumptions, and hidden control planes.
- `08` Scenario stress-test: read `references/08-scenario-stress-test.md`
  Use to test whether the current map survives realistic failure or abuse scenarios.
- `10` Maintenance harness: read `references/10-maintenance-harness.md`
  Use to add PR templates, drift checks, and review guardrails.
- `11` Remediation loop: read `references/11-remediation-loop.md`
  Use to turn existing findings into safe-fix batches, approval packets, and verification steps.
- `12` Transcreation boundary: read `references/12-transcreation-boundary.md`
  Use to map language, locale, UI copy, authored content, and saved-text boundaries without letting source-locale prose become hidden runtime truth.

There is no pass `09`. The numbering jumps from `08` to `10` to leave room for future passes between the review family and the maintenance family without renumbering existing references. This is intentional, not a missing file.

## Load Rules

### Conventions and shared shapes

- `references/00-conventions.md` is always available for vocabulary (confidence levels, risk levels, status, unknowns entry shape, sensitive data classes, approval gates, default doc paths). Open it on first use of any pass and keep it open for the engagement.
- Do not bulk-load every reference file.
- Read only the active pass reference, then load extra references only if that pass tells you to.

### Runnable loops vs reference examples

- `references/combined-03-06-loop.md` is a **runnable loop**, not a numbered pass. Use it when the user wants repeated comment-plus-hygiene passes on one slice at a time.
- `references/example-stress-test-report.md` is a **reference example**, not a runnable pass. Open it only when pass `08` needs an output example.

### Templates

- `assets/templates/` files load only when creating the matching artifact.

## Default Pass Order

1. Run `00` first to choose the entry point.
2. Run `01` if steering is missing or stale.
3. Run `02` next to map the repo.
4. Run `05` before broad audits when the repo is large or mixed.
5. Run `03` on one critical slice at a time.
6. Run `06` after each writing-heavy pass, or use the combined `03` + `06` loop.
7. Run `04` after a map exists.
8. Run `07` or `08` to challenge the current understanding.
9. Run `10` after initial outputs exist.
10. Run `12` when language, locale, copy ownership, saved prose, or future transcreation pressure matters.
11. Run `11` to convert findings into bounded remediation work.

## Mini-Mode

For small, single-runtime, or new repos, the full pass order is overkill. Mini-mode runs an abridged flow:

`00` → `01` → `02` → `03` + `06` (combined loop) → `04` → `11`

Use mini-mode only when **all** of the mini-mode trigger checks in `00-preflight.md` pass. If any trigger fails, fall back to full mode.

Mini-mode skips `05` (the repo is small enough that one map covers it), `07` and `08` (less surface area to overclaim), `10` (a plain PR template is enough), and `12` (defer until copy ownership becomes a real concern).

## Cross-Pass Rules

### Evidence Discipline

- Do not guess.
- Mark claims as `verified`, `inferred`, or `unknown` per `00-conventions.md`.
- Record unknowns using the canonical entry shape in `00-conventions.md`.
- Name out-of-repo boundaries when they affect live behavior.
- Do not claim whole-repo coverage from one clean path.

### Comment Discipline

- Add or update comments when a change creates or touches a non-obvious invariant, trust boundary, idempotency rule, deterministic id or hash, save semantics, approval gate, recovery path, or language boundary.
- Put comments beside the decision point or choke point, not in a distant summary.
- Keep comments short and evidence-backed. If the behavior is unclear, record an unknown instead of narrating a guess.
- Do not let feature work silently remove comments that explain rationale, invariants, trust boundaries, failure modes, ordering, idempotency, repair, or rollback hazards.

### Approval Gates

See `00-conventions.md` for the canonical list. Document the finding first. Propose the smallest safe edit. Stop before editing if the next step crosses a gate.

### Sensitive Data

See `00-conventions.md` for the canonical class list. Never place those values in logs, comments, tests, docs, screenshots, or commit messages. Name regulated data classes in steering files and treat them as high risk everywhere.

### Writing

See `00-conventions.md` for the writing rules. Run `06-writing-hygiene.md` after any pass that writes text.

### Language And Transcreation

- Treat stable ids, tags, payload fields, numbers, validated contracts, and persisted state as truth. Treat rendered language as a downstream view unless repo evidence says otherwise.
- Do not parse English UI strings, prose, or localized copy to recover runtime truth.
- Locale or transcreation work must not rename canonical ids, rewrite saved receipts, alter hash semantics, or change mechanics without the matching contracts, tests, docs, and approval notes.
- Shared UI and library layers should stay copy-light when practical. Put locale policy in the owning app, content, or presentation layer.

### Scope Honesty

- Do not flatten sibling repos, submodules, vendor consoles, remote-config systems, or external control planes into local coverage claims.
- Keep generated, vendored, minified, mirrored, serialized, or binary artifacts out of inline comment churn. Explain them in docs instead.
- Preserve repo-type specificity. Do not rewrite mobile, native, AI, infra, library, or mixed-stack systems as if they were generic web apps.

### Tooling Notes (Claude Code specific)

- Prefer `Read` for files, `Grep` for symbol or text searches, and `Edit` for in-place changes. Reserve `Bash` for shell-only operations like `find`, `git`, builds, or test runs.
- Use parallel tool calls for independent reads. A pass that opens 5 files for inventory should issue one batched call, not five sequential ones.
- Do not skip pre-commit hooks. Never use `--no-verify`, `--no-gpg-sign`, or equivalent unless the user explicitly asks.
- Avoid `cat`, `head`, `tail`, `sed`, `awk`, or `echo` in `Bash`. Use `Read`, `Edit`, or `Write` instead.
- When a pass needs to plant a new doc, prefer creating it under the repo's existing convention before falling back to the defaults in `00-conventions.md`.

## Bounded Execution

- Default review checkpoint: every `10` commits.
- Default hard stop: `20` commits.
- Stop sooner if the current slice is complete, a protected area is uncovered, the map is stale, evidence turns soft, or an approval-gated edit becomes next.
- Keep one commit to one backlog item, one slice checkpoint, or one tightly related docs-plus-code unit.

### No parallel passes

Run one numbered pass at a time. Do not interleave passes within a single slice. The combined `03` + `06` loop is the only sanctioned interleave, and only because `06` is text-cleanup of work `03` just produced.

### Abort on contradiction

Stop the active pass and surface the contradiction when:

- a finding contradicts a load-bearing claim in a steering file, system map, or coverage ledger
- evidence inside one slice contradicts evidence inside another that the current pass relies on
- a protected-area edit appears to have already happened without an approval note
- the active pass would force a `verified` claim where only `inferred` evidence exists

Document the contradiction, propose the next check, and wait for human review before continuing.

## Default Deliverables

See the deliverable path table in `references/00-conventions.md`. Use the repo's existing convention first. Fall back to those defaults only when the repo has none.

Templates live under `assets/templates/`:

- `system-map.md`
- `coverage-ledger.md`
- `repo-slices.md`
- `unknowns-file.md`
- `remediation-backlog.md`
- `pr-template.md`

## Report Back After Each Pass

Return:

- which pass ran and on what slice
- which files were created or updated
- which unknowns were recorded
- which risks moved up or down
- which approval gates were crossed or are still pending
- which pass should run next
- whether human review is required before continuing
