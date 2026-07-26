# Reference: Orchestration Mode

Use this reference when the harness can spawn subagents or run journaled multi-agent workflows, and the active pass would benefit from fan-out. This is a **runnable mode**, like `combined-03-06-loop.md`, not a numbered pass.

**Mode:** inherits the active pass's mode. A read-only pass stays read-only for every subagent.

If the harness has no subagent facility, skip the fan-out sections and run every pass inline as written, but still use **Gate truth** whenever a later pass executes repo gates. This mode changes execution shape only. It never changes pass content, pass order, approval gates, or evidence rules.

## Contents

- Non-negotiable rules and shared-worktree ownership
- Work assignment and gate truth
- Evidence promotion and adversarial verification
- Repo-specific fan-out shapes
- Capability detection, budget, and recovery
- Fabricated-specificity guard and acceptance

## Rules that do not bend

- One numbered pass at a time. Fan-out lives inside a pass: many slices, many lenses, one pass.
- Subagents never write the shared deliverables. The orchestrator is the single writer.
- In a write-enabled pass, subagents edit only their assigned, non-overlapping paths.
- Every subagent claim carries a citation (file plus line, or the exact command and output). No citation means the claim caps at `inferred` no matter how confident it sounds.
- Subagents inherit the pass mode. In read-only passes they must not build, test, migrate, push, or execute repo code.
- Do not force `verified` where the stack cannot be safely exercised. Record the next best check instead.

## Shared-worktree ownership

When agents share one worktree, every edit is immediately visible to every agent. Before fan-out:

- record each agent's owned paths and whether it may edit or is review-only
- assign one writer per path; send overlapping review findings to that writer
- inventory pre-existing modified, untracked, ignored, and submodule-internal state

During and after fan-out:

- do not reset, clean, checkout, stash, move, stage, or commit another agent's or the user's changes
- do not use broad staging such as `git add -A`; the orchestrator stages exact reviewed paths after writers finish
- require agents to report their touched files and validation commands; compare that report with the scoped diff
- pause the overlapping task if an unplanned writer appears in an owned path
- allow a subagent to commit only when the orchestrator explicitly delegated an exact commit scope

## What runs where

| Work | Preferred mechanism |
| --- | --- |
| Deterministic enumeration: file discovery, disk use, git history, extension counts, citation checks, dedup, progress counts | Local deterministic tools; do not delegate what a direct command can settle. |
| Wide per-slice recon (pass 02, 04, 05 slice work) | Available subagents with bounded, non-overlapping scopes. |
| The single highest-risk slice (deploy, migrations, auth, secrets, money) | The strongest available review path within the engagement's stated resource bounds. |
| Adversarial challengers and cross-slice synthesis (pass 07, 08, map synthesis) | Independent challenger lenses with condensed, evidence-bearing inputs. |
| Orchestrator synthesis and deliverable writing | The orchestrator itself, reading condensed reports, not the raw repo. |

Before naming model tiers, effort controls, paid work, cache hits, prices, replay behavior, journals, or run IDs, inspect which capabilities the current harness actually exposes. If a capability or its economics cannot be observed, describe only the work shape and mark cost, cache, or replay behavior `unknown`. Never promise that a command, agent, cache hit, or resumed call is free.

### Gate truth

Repo gates (build, test, lint, validators) provide deterministic repo evidence, but they execute repo code. Only run them when the user owns the repo or has said it is safe to execute. On an inherited or unknown repo, read the package scripts and CI definitions first and record what the gates would do.

When gates do run:

- Run long gates in the background before launching the fan-out so they overlap it.
- Build a read/write dependency graph before parallelizing gates. Serialize any
  producer and consumer that share an output directory or dependency file: a native
  rebuild that replaces DLLs must finish before a publish that requires them, and two
  clean-publish jobs must not clean the same tree. Parallelism is safe only when
  mutable outputs are disjoint or one side is read-only after the producer completes.
- Capture the real exit code. A pipeline such as `<gate> 2>&1 | tail` can report the consumer's status instead of the gate's, so a red suite reads as success. Use the shell's pipeline-status mechanism (for example Bash `${PIPESTATUS[0]}`), capture the gate status before post-processing, or write structured output and inspect it separately.
- Match the status mechanism to how the command was invoked. In PowerShell,
  `$LASTEXITCODE` is authoritative for a native child such as `pwsh.exe -File`, but
  it can be stale or unset after invoking a `.ps1` in the current session. Use
  terminating errors plus `try`/`catch` or `$?` for in-session scripts, or launch a
  child PowerShell process and check that process exit code.
- Treat exit zero as necessary, not sufficient. Inspect structured results or the full summary for tool-level errors and partial work; some orchestration layers can print failed inner operations while the outer process still exits successfully.
- Assert that the expected work happened. Record discovered, executed, passed, failed, and skipped test counts; expected projects, targets, configurations, frameworks, and architectures; and required artifact paths or manifest entries. A test command that discovers zero tests is not a passing test suite.
- Bound every result to the exact toolchain and target tuple: command, tool path and version, SDK or workload resolver, framework, configuration, runtime identifier, architecture, and relevant environment overrides. Separate a repo defect from a local toolchain or missing-workload blocker.
- Treat skips, inconclusive results, expected failures, allowlists, and known-dead entries as explicit debt. A green gate may prove only that no new item escaped the allowlist; it does not prove the allowlisted condition is fixed or that unresolved inputs were analyzed.
- Anything a gate actually proves needs no agent verification. Record it as `verified` with the command, bounded result, counts, and artifact evidence.

## The verified promotion rule

This rule exists because unsupervised fan-out drifts on evidence labels.

- A subagent claim enters the ledger at `inferred`, with provenance: batch or run id when exposed, agent label, and the citation.
- It is promoted to `verified` only when one of these holds:
  1. The orchestrator personally re-opened the citation and it says what the claim says.
  2. Independent traces reach the claim from different evidence sources or behavior paths.
  3. A deterministic command settles it. Execution beats opinion.
- Two agents repeating one citation are not independent evidence. Count the citation once.
- Spend promotion effort risk-first. Personally re-check every top-risk citation (deploy, migrations, auth, secrets, money). Sample at least one citation per subagent below that. A subagent that fails its sample check has its whole report treated as `inferred` pending re-check.
- Label the claim, not the hope. A config or text claim can be `verified` by reading the file. The live behavioral guarantee it implies stays `inferred` until observed. "The workflow YAML has no approval gate" is verifiable; "deploys always block on approval" is not, from text alone.
- Record what was not promoted and why. The label must be auditable, not asserted.

## Verification allocation for finding passes

For passes that produce findings or challenge claims (07, 08, 11):

- Fan out one challenger per lens, blind to the other challengers. Use the specialty-stack trap lists in `07-adversarial-review.md` as the lens catalog.
- Scale verification by stakes. A top-risk defect claim gets two independent verifiers: one told to refute it, one told to trace the concrete trigger path. Lower-risk claims get one refuter.
- Verifiers receive the claim and the cited code only, never the finder's narrative. Default to refuted when uncertain.
- Design and remediation suggestions are not adversarially verified. They get one bounded reality-check pass against the repo.
- Cap verification volume. Pass overflow through labeled `unverified` and log the drop count. Silent truncation is a coverage lie.

## Fan-out shapes per repo type

Pick the branch that fits. Mixed repos slice by runtime unit first, then apply the matching branch per unit. Do not flatten specialty stacks into one shape.

**Game repo.** Slices: client, authoritative server, tools, content-pipeline code, CI and CD. Challenger lenses: client authority, live economy, save and replay and anti-cheat, narrative text claiming unimplemented mechanics. When safe to execute, settle determinism claims by running the engine twice with one seed and diffing; observed output is stronger than a second opinion.

**Mobile or native app.** Slices: shared or JS layer, native bridges, local storage, build and provisioning, release tooling. Lenses: secure storage, permission flows, bridge validation, SDK payload capture.

**Infra-as-code repo.** Slices: per module, per environment overlay, state backend, pipeline permissions. Lenses: IAM sprawl, remote state, secret drift, runner powers. Applying changes is not a safe check. `verified` comes only from read evidence or plan output already on disk. Otherwise cap at `inferred` with a named next check.

**AI or data system.** Slices: pipelines, notebooks, evals, routing, prompt and tool schemas. Lenses: prompt and response logging, data lineage, quiet routing changes, remote-config control planes.

**Locale-heavy or content-heavy repo.** Slices: per rendered-text surface, overlay or compile pipeline, saved-prose stores. Lenses: copy parsed as truth, id renames, saved strings participating in replay or hashes.

## Capability detection, budget, and recovery

- Quote the fan-out shape before launching: agent count, scopes, stages, and any model or effort controls the harness actually exposes. Quote prices or usage effects only when the current environment provides authoritative data.
- After a usage-limit event, re-check available capabilities and state which resource bound changed. A thoroughness request does not silently override resource limits.
- Checkpoint between the wide recon stage and any deeper or more resource-intensive stage. Let evidence gate the next stage.

## Tier calibration at checkpoints

Verification produces a calibration signal only when it is instrumented. Record every verdict per task family (dimension, slice, or claim type), and at each checkpoint compute the per-family survival rate before deciding what the next batch runs on. Unrecorded results calibrate nothing.

- When the harness exposes model tier and effort separately, treat them as two ordinal dials: tier changes the capability ceiling; effort changes deliberation depth. Otherwise tune scope, lens, and review depth without inventing controls.
- Refutations shaped like misunderstanding (a wrong model of how parts interact) are a ceiling signal. Raise the tier when that control exists, deepen review, or stop the family and bank what survived.
- Failures shaped like incomplete coverage (stopped early, traced one path of four, shallow verdicts) are a diligence signal. Raise effort when that control exists; otherwise narrow the scope and deepen review.
- Both signals on a bounded scope: raise both exposed dials, or narrow the scope and deepen review.
- No failure signal: change nothing. A winning recipe is not an invitation to tune.
- Record each checkpoint decision with its input rates in the run report, so the next engagement inherits the calibration instead of rediscovering it.
- Give every delegated unit a stable task description and record its inputs. Use cache keys or byte-stable prompts only when the harness documents those semantics; prompt stability alone does not prove cache reuse.
- On a crash or usage-limit stop, resume a run only when the harness exposes resumable run IDs. Treat an in-flight result as suspect. Do not claim completed calls replay at no cost unless the environment confirms it.
- Do not interrupt healthy in-flight work merely to retrofit an improvement. Record the improvement for the next batch.
- When a journal exists, monitor it with deterministic tools rather than delegating a progress summary.
- Clean your own scratch when the pass ends: distill results into the pass deliverables, then tier and clear working files per `09-artifact-gc.md`. An orchestrated run must not leave unexplained artifacts behind.

## Fabricated specificity guard

Subagents import context from their environment: working directory names, tool descriptions, prior conversations. A repo name, version, owner, or purpose that does not appear in gathered evidence is not evidence. Strip it or mark it `inferred` with its source named as assumption.

## Acceptance checklist

An orchestrated pass is done when:

- the pass's own acceptance checklist passes unchanged
- every ledger claim sourced from a subagent carries provenance and an honest label under the promotion rule
- the deliverables state what the fan-out did not cover, including verification overflow
- the user saw the fan-out and known resource shape before it ran; unsupported cost or cache claims were marked unknown
- recovery behavior matches capabilities the harness actually exposes
