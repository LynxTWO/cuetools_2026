# Reference: Preflight and Repo-Shape Sniff

Use this reference at the start of any anti-dark-code engagement, and again before any later pass when you suspect the existing maps, ledgers, or review docs may have gone stale.

**Mode:** read-only. Doc-only updates are allowed when they exist solely to mark staleness.

## Contents

- Goal and minimum inputs
- Repo shape and artifact freshness
- Entry-point and stop decisions
- Mini-mode, rules, and acceptance

## Goal

Decide three things before committing to a heavier pass:

1. What kind of repo this is (so the right specialty branches engage in later passes).
2. Whether prior anti-dark-code artifacts exist, and whether they are still aligned with the current repo state.
3. Which pass should run next - and whether **mini-mode** (see `SKILL.md`) is the right shape for this repo.

The preflight pass keeps the rest of the workflow from acting on a wrong picture.

## Inputs to inspect

Read-only. Do not edit application code. Inspect at minimum:

- `README.md`, root docs, top-level dotfiles
- existing steering files (`AGENTS.md`, `CLAUDE.md`, tool-specific siblings)
- `docs/` tree, especially `docs/architecture/`, `docs/security/`, `docs/review/`, `docs/unknowns/`
- package manifests, lockfiles, workspace files
- `CODEOWNERS`, ownership docs, service catalogs
- submodule declarations and each initialized submodule's internal status
- CI workflows, deploy configs, release tooling, migration runners
- engine config, app manifests, platform config when present
- locale, copy, translation, content, prompt directories when present
- recent commits and the diff against the last anti-dark-code artifact (if any)

## What to produce

The preflight is a short note, not a deliverable program. Write it as a task summary or as `docs/review/preflight.md` only when the engagement justifies a persistent record.

The note must answer:

### 1. Repo shape

- Repo type: service / monorepo / frontend / library / mobile / native / game / infra-as-code / AI or data system / mixed
- Primary languages and frameworks
- Runtime model: number of runtime units, deployment style, how the system is entered
- Sensitive domains present: auth, billing, deletion, regulated data, anti-cheat, model routing, infra state, etc.
- Out-of-repo control surfaces present: remote config, feature flags, vendor dashboards, app-store consoles, sibling repos, submodules

Keep this to a paragraph or a short bullet list. No invention. If something is uncertain, mark it `inferred` or `unknown`.

### 2. Existing artifacts and their freshness

For each anti-dark-code artifact the repo already has, report:

- present / absent
- last touched (date or commit)
- broadly aligned with current code, or stale

Stale signals:

- a new runtime, control-plane path, or protected area landed after the artifact was written
- the coverage ledger does not reflect recently touched slices
- a recent telemetry change happened with no logging-audit update
- a recent protected-area change happened with no approval note
- a recent locale, copy, or saved-text change touched ids, hashes, or mechanics with no language-boundary note

Do not decide freshness from dates alone. Diff the artifact's last meaningful commit to `HEAD` and enumerate new or removed project manifests, entrypoints, workflows, package or release scripts, runtime configuration, and test projects. Record the size and shape of that delta. A polished old map is stale when it omits a new runtime, even if every statement it still contains is true.

For each submodule, record the superproject gitlink and expected commit, initialization state, checked-out commit, gitlink drift, tracked modifications, untracked files, ignored build artifacts by count or class, and nested submodules. A dirty submodule worktree is not the same finding as superproject gitlink drift, and a clean parent status does not prove the submodule internals are clean.

### 3. Recommended entry point

Pick exactly one **immediate next pass** from:

- **Full mode, run `01` first** - repo has no steering or steering is stale
- **Full mode, run `02` first** - steering is current; no system map
- **Full mode, run `05` first** - repo is large or mixed; needs slicing before broader passes
- **Full mode, refresh stale artifact** - name which artifact and which pass refreshes it
- **Mini-mode** - small, single-runtime, or new repo (see `SKILL.md` for the abridged flow)
- **Stop for review** - preflight surfaced a problem big enough that the user should decide the next step

Justify the pick in one or two sentences anchored to repo evidence.

A compound audit may require several passes. Record their intended sequence, but nominate only
one as the immediate next pass and complete it before beginning the next. "One entry point" means
one next action, not that a broad engagement must pretend it needs only one pass.

### 4. Stop conditions surfaced during preflight

If preflight uncovers any of these, stop work that depends on the contradicted claim and flag it:

- a protected-area edit landed without an approval note
- a sensitive-data leak is visible in logs, telemetry, comments, tests, or commit messages
- the existing map or ledger contradicts current code in a load-bearing way
- a hidden control-plane path (script, CI job, notebook, release tool) appears in code but not in any artifact
- a steering file claim conflicts with what the code now does

For each stop condition, record the area, the evidence, and the smallest next action.

Do not turn an expected stale-map finding into a dead end. When the user asked for a refresh and
the contradiction is confined to a dated review artifact, mark the old claim stale, downgrade its
confidence, and continue with the pass that refreshes it. Human review is required before
continuing only when the contradiction changes authorized scope, affects a protected area, or
cannot be resolved from repository evidence.

## Mini-mode trigger checklist

Recommend mini-mode only when **all** of these hold:

- single primary runtime unit (one app, one service, one library, or one frontend)
- single primary language family
- no migrations, no privileged CI or CD, no release tooling with production reach
- no auth, billing, deletion, regulated data, or other approval-gated domain at meaningful scale
- repo size is small enough that one engineer can hold the map in their head

If any of those fail, recommend full mode.

## Rules

- No application code changes.
- Do not execute repo code, gates, or scripts during preflight. Read what they would do instead. When a later pass needs gate output as ground truth, follow the gate-truth rules in `references/orchestration-mode.md`, including real exit-code capture.
- Do not start a heavier pass during preflight. Preflight only chooses the next pass.
- Do not claim artifacts are current without checking the diff between the artifact and recent code.
- Apply the negative-search evidence rules in `00-conventions.md`; zero candidate files never support a clean or absent claim.
- Do not flatten out-of-repo control surfaces into local coverage claims.
- Use `00-conventions.md` for confidence levels, risk levels, status vocabulary, and unknowns shape.

## Acceptance checklist

The preflight result should let the next pass start with:

- a clear repo type and runtime model
- an honest read on which prior artifacts can be trusted
- one explicit recommended entry point
- any stop conditions surfaced and named
- a mini-mode vs full-mode decision when the trigger checklist applies
