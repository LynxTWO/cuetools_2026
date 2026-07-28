# Reference: Adversarial Edge-Case Review

Use this reference after the first architecture pass and first slice plan when you want to challenge the current repo understanding instead of extending it.

**Mode:** read-only review of code plus docs.

For confidence levels, the unknowns entry shape, the canonical approval-gated list, and the canonical sensitive-data class list, see `00-conventions.md`.

## Contents

- Goal, triggers, and deliverables
- Repo shape, trust, protected areas, and coverage challenges
- Specialty-stack traps and output shape
- Rules and acceptance

## Goal

Try to break the current picture of the repo. Look for blind spots, hidden runtime paths, weak trust-boundary notes, protected-area mistakes, and false claims of coverage.

The point is not to make output look nicer. The point is to make overclaiming harder.

## When to run this

Run when one or more fit:
- the repo is old, mixed, or large
- the repo has many scripts, tools, or side channels
- the repo has auth, billing, secrets, deletion, migrations, or other high-risk paths
- the repo is a game repo, mobile stack, infra repo, AI system, or data system
- the repo has locale, transcreation, authored-content, generated-prose, or saved-text boundaries
- earlier passes claimed confidence that feels broader than the evidence

## Deliverables

Create or update:
- `docs/review/adversarial-pass.md`
- `docs/unknowns/adversarial-pass.md`

Update the coverage ledger when this pass changes confidence, scope, or slice order.

## What to challenge

### 1. Claimed repo shape

Check whether the system map and slice plan missed:
- hidden runtime entrypoints
- admin-only tools
- backfills or one-shot scripts with live side effects
- package scripts or task targets that hit live systems
- notebooks used for prod support, data repair, or model operations
- feature flags that bypass normal paths
- scheduled jobs that act like quiet control planes
- editor tools or import hooks that change shipped behavior
- test helpers or fixtures that ship into real runtimes
- generated code that hides hand-written logic nearby
- bootstrap scripts that set permissions, state, or seeds
- CI or CD jobs that deploy, seed, migrate, backfill, or repair live state
- release tooling or platform-console steps that quietly change shipped behavior
- submodules, workspace links, or sibling repos that own part of the live path
- remote config, feature flags, or CMS content that alter behavior outside the repo
- locale files, content overlays, generated copy, prompt text, or CMS prose that silently changes runtime decisions

### 2. Trust boundaries

Check whether trust changes were mapped honestly. Boundaries include:
- public user to authenticated user
- standard user to admin user
- client to backend
- service to database
- service to queue
- queue to worker
- repo to third-party platform
- CI or CD system to cloud control plane
- release tooling to app-store or platform console
- source-locale text to translated or transcreated text
- rendered copy to structured runtime truth
- model or agent layer to tool execution
- game client to authoritative server
- mobile client to secure storage or native bridge
- infra runner to cloud control plane
- local file picker or drag-and-drop to decoded content
- optional third-party provider setting to authenticated request
- mutable UI selection to a long-running or published job

For these acquisition boundaries, challenge:

- a retained path or URI presented as if it were immutable content
- validation followed by a second read that can observe replacement bytes
- encoded-size limits without decoded, expanded, dimension, or object-graph limits
- extension or `Content-Type` checks without magic and decoder validation
- a shared credential collected when the API documents a narrower key or token
- one protection purpose reused across unrelated secrets
- secrets placed in URL-path cache keys, logs, exceptions, diagnostics, screenshots,
  tests, or persisted plaintext
- an optional provider enabled before credentials, attribution, distribution terms,
  rate policy, and failure isolation are explicit
- a UI choice that can change after a job starts while the job still reads live
  view-model state
- browser-only, low-confidence, alternate, or non-primary candidates entering an
  automatic-selection path because ranking and eligibility were treated as one rule

### 3. Protected areas

Check whether protected areas were handled with enough caution. Use the approval-gated list from `00-conventions.md` as the baseline. Add repo-specific protected areas verified from the repo: saved rendered text that participates in replay, hashes, audit trails, user history, or product commitments; training data lineage; model routing; eval gates; tool execution; live economy; anti-cheat; platform purchase flows; infra state backends; IAM edges; runners; secret stores; privileged CI or CD automation; release tooling; support tooling with production reach.

If a prior pass edited a protected area without approval, call that out plainly.

### 4. Coverage honesty

Check whether the coverage ledger overstates what was actually reviewed. Look for:
- one helper treated as if it covered a whole flow
- one service treated as if it covered a whole monorepo
- a single comment pass treated as if a critical path is done
- a logging audit that skipped client telemetry, crash reports, worker logs, or support tools
- large excluded areas with no plan to explain them elsewhere
- one clean path used to hide a dirtier parallel path through scripts, tools, notebooks, CI jobs, release tooling, or remote config
- a zero-candidate search reported as proof of absence, safety, or a "pure managed" implementation
- a plugin called unreferenced after checking only static project references, without its copy, discovery, dependency, enablement, and invocation paths
- a failure called a source defect when the path was unexercised or blocked by a toolchain, native dependency, or unavailable capability
- an assurance claim such as verified, bit-exact, atomic, or repaired supported only by a happy path, frame-level check, or successful write, without finalization and whole-output evidence

### 5. Specialty-stack traps

Pick the branch that fits.

**Game repo:** client-authority mistakes, live economy logic outside the server boundary, entitlement checks split across client and server, narrative text that invents mechanics, locale text that rewrites canonical ids, analytics or crash events with player data, asset or scripting paths that bypass normal review, save, replay, or anti-cheat paths that expose trust mistakes.

**Mobile or native app:** local storage of sensitive data, permission flows under-documented, native bridge calls with weak validation, push or background tasks with hidden side effects, crash or analytics SDKs with rich payload capture, build or release or provisioning steps that quietly change runtime behavior, app-store or platform-console steps outside normal repo review.

**Infra-as-code repo:** remote state risks, IAM sprawl, secret-store drift, modules that look read-only but write live state, runner or pipeline permissions that exceed their job, environment-specific behavior hidden in variables or templates, import or migration steps that bypass normal guardrails.

**AI or data system:** prompt or response logging, tool traces with raw user content, notebooks that feed production without clear guardrails, eval sets or labeled data with weak lineage, routing layers that change model behavior quietly, safety filters that sit outside the documented flow, support scripts that can read or write prod data outside the main runtime, model prompts or tool schemas or safety settings pulled from remote stores or dashboards.

**Locale-heavy or content-heavy repo:** English copy parsed as truth, translated text changing ids or keys, saved rendered strings used for replay or hashes, shared UI widgets hiding copy policy, locale overlays targeting unapproved fields, transcreated prose adding mechanics or policy claims the structured system does not support.

## What to put in `docs/review/adversarial-pass.md`

Record:
- areas reviewed in this pass
- what earlier passes got right
- what earlier passes overstated or missed
- which risks moved up or down after this review
- which slices should move earlier in the queue
- which protected areas need human approval before any edit

Direct tone. Name the claim. Name the evidence. Name the gap.

## What to put in `docs/unknowns/adversarial-pass.md`

Use the unknowns entry shape from `00-conventions.md`.

## Rules

- No application code changes.
- Do not turn suspicion into fact without evidence.
- Do not flatten specialty stacks into generic web-app language.
- Do not let one clean path hide a dirtier parallel path.
- Do not claim a risk is closed when the evidence only covers one branch.
- Mark cross-repo or out-of-repo boundaries when they shape the live path.

## Acceptance checklist

The result should:
- challenge earlier confidence with evidence
- catch blind spots in runtime shape, trust boundaries, or protected areas
- tighten the coverage ledger instead of making it look nicer than it is
- surface specialty-stack risks when the repo is not a plain web app
- leave the next reviewer with a clearer map of what still needs proof
