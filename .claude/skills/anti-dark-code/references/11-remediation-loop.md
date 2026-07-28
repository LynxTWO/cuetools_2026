# Reference: Post-Rollout Remediation and Verification Loop

Use this reference after the first anti-dark-code rollout when you want the outputs to turn into a bounded remediation program instead of sitting in docs.

**Mode:** bounded edits under approval rules. Slice-aware. Stop points required.

For confidence levels, the unknowns entry shape, the canonical approval-gated areas list, and default deliverable paths, see `00-conventions.md`.

## Contents

- Goal, triggers, companion references, and preflight
- Backlog and bounded safe-fix batches
- Assurance claims, transactions, and TODO lifecycle
- Evidence gaps, approval packets, and touched-slice verification
- Maintenance, companion loop, and acceptance

## Goal

Turn current anti-dark-code outputs into:
- a ranked remediation backlog
- bounded safe-fix batches
- evidence-gap checks for uncertain items
- approval packets for protected areas
- touched-slice verification after each batch
- a clear next slice instead of a vague sense of progress

Keep work evidence-backed. Keep batches small. Keep protected areas behind approval. Keep coverage claims honest. Keep language boundaries from becoming hidden mechanics or policy.

## When to use this reference

Use it when the repo already has most or all of these:
- steering files
- `docs/architecture/system-map.md` or `docs/architecture/service-map.md`
- `docs/architecture/coverage-ledger.md` and `docs/architecture/repo-slices.md` when needed
- one or more files under `docs/unknowns/`
- `docs/security/logging-audit.md` when telemetry matters
- `docs/review/adversarial-pass.md` when the repo is large, old, mixed, or high-risk
- `docs/review/scenario-stress-test.md` and `docs/review/scenario-scorecard.md` when the repo has been break-tested

If those inputs are missing or stale, refresh them first. Do not build remediation on top of outdated maps.

## Companion references

- `00-preflight.md` for the canonical preflight that decides whether remediation can start now or earlier passes need a refresh
- `10-maintenance-harness.md` for guardrails for future contributors, before or after the first remediation wave
- `03-critical-path-comments.md` when a backlog item is best handled as comment-only clarification
- `06-writing-hygiene.md` after any step that writes comments, docs, commit text, PR notes, ADRs, or unknowns files
- `combined-03-06-loop.md` for bounded slice-by-slice comment loops with immediate cleanup
- `12-transcreation-boundary.md` when a backlog item involves locale, copy ownership, authored content, generated prose, saved text, or source-language assumptions

## Step 0: Preflight and stale-artifact check

Run `00-preflight.md` first. The remediation-loop's preflight needs are a strict subset of what `00-preflight.md` covers, so do not duplicate the work - open it, run it, and use its recommendation to decide whether Step 1 can start now or whether an earlier pass needs to refresh first.

The duplication here is intentional: the preflight reference is also a first-class entry point for engagements that begin without remediation in mind. Pass `11` cross-links to it so a remediation-led engagement still gets the same gate.

Stop conditions stay the same regardless of where preflight was triggered:

- a new runtime or control-plane path landed after the map was written
- the ledger no longer reflects touched slices
- a recent telemetry change was never audited
- a recent protected-area change happened without current approval notes
- a recent locale or copy change touched saved text, hashes, ids, policy, or mechanics without a boundary note
- a comment loop would now run on a stale slice plan

## Step 1: Build the remediation backlog

Use current anti-dark-code outputs as inputs.

Create or update:
- `docs/review/remediation-backlog.md`
- `docs/unknowns/remediation-pass.md`

Use the `remediation-backlog.md` template under `assets/templates/`.

Rules:
- do not guess
- do not claim full coverage unless the ledger supports it
- no application code in this pass
- separate findings into:
  1. safe-to-fix now
  2. approval-gated
  3. needs more evidence
- rank each item by risk and likely user or system impact
- group items by slice, subsystem, runtime unit, or control-plane path when that helps
- name exact files, paths, runtime units, scripts, jobs, CI or CD paths, release paths, or control-plane boundaries
- include evidence source, smallest safe next action, and verification needed
- call out holes caused by hidden entrypoints, out-of-repo control surfaces, sibling repos, submodules, release tooling, remote config, feature flags, notebooks, admin tools, support scripts, migrations, one-shot ops scripts
- call out holes caused by English-only assumptions, shared components hiding copy policy, locale overlays targeting unapproved fields, or rendered text mixed with runtime truth

For each backlog item include:
- title
- area or slice
- risk level
- why it matters
- evidence found
- confidence (`verified` | `inferred` | `unknown`)
- approval needed (`yes` or `no`)
- recommended next reference or pass type
- smallest safe next step
- verification plan
- owner if known
- current status (`open` | `ready` | `in progress` | `blocked` | `deferred` | `fixed`)

## Step 2: Implement safe fixes in bounded batches

Use `docs/review/remediation-backlog.md` and the coverage ledger.

Pick only backlog items that are:
- verified or strongly evidenced
- not approval-gated
- behavior-preserving, comment-only, docs-only, or narrow redaction and hardening changes
- small enough to review cleanly

Before editing, create or update `docs/review/safe-fix-plan.md` with, for each selected item:
- exact files to change
- why the change is safe now
- what behavior must remain unchanged
- tests or checks to run
- docs to update
- rollback note
- observability note if relevant
- whether the item is comment-only, docs-only, or behavior-preserving code cleanup

### Assurance-claim oracle

Treat words such as `verified`, `bit-exact`, `atomic`, `repaired`, `complete`, and `safe` as contracts, not adjectives. For each claim, record:

1. the claimed object and scope (frame, stream, finalized file, staged output set, or published result)
2. the producer path and an independent oracle that can falsify the claim
3. every finalization boundary (`finish`, `flush`, `close`, process exit, final length, manifest completion) and how its return or exception is checked
4. the whole-output proof when the claim covers a whole artifact; frame checks alone do not prove final blocks, container metadata, sample count, or truncation safety
5. positive, mismatch, truncation, finalization-failure, cancellation, and unavailable-capability results when applicable
6. the exact runtime, toolchain, target tuple, and observed counts
7. every UI, tooltip, log, or document that repeats the claim
8. every later copy, move, metadata rewrite, upload, or publication boundary that
   carries the claim; a Boolean detached from its evidence is not a transferable
   receipt

For lossless audio or similar transforms, a whole-output oracle normally reopens or decodes the finalized output and compares format, expected length or sample count, and all payload data (directly or by a collision-resistant digest over the exact bytes). A successful write or per-frame comparison does not cover an ignored final flush. When the required runtime or codec is unavailable, record `capability unavailable`; do not report the real path as passing.

Treat disagreement between the product's verifier and a credible independent oracle as
a verifier finding, not automatic proof that the produced artifact is corrupt. Preserve
the exact rejected artifact, identify the first failing object and boundary, and test
both implementations against it before changing the producer or suppressing the check.
For buffered parsers, distinguish logical input extent from array capacity, allocation
slack, speculative cache position, or the address of a physical read cursor. Bound the
logical units consumed; otherwise a hardening guard can still over-read, or falsely
reject valid data already held in a cache.

Do not let one implementation lend its guarantee to another format that lacks the same oracle.
When a verified artifact is transformed or re-homed, carry an immutable proof that
binds the exact constrained output set, content identity, and relevant semantic
oracle. Revalidate that proof at the new boundary or explicitly clear/downgrade the
claim. Prevent time-of-check/time-of-use gaps by holding an appropriate read lease
while hashing, copying, or reopening the bytes that the proof names.

### Measurement-driven hardware and calibration checklist

Use this when correctness depends on a measured hardware capability, timing result,
device cache, boundary read, media property, or persisted calibration.

- Treat newly required calibration as a state migration, not an optional settings
  screen. Trigger it before the first operation whose assurance depends on it, version
  the record, and refuse the operation when the required probe cannot complete.
- Distinguish positive evidence from failure to observe. Once hardware has
  demonstrated a safety-relevant behavior such as caching, a later noisy timing run
  that does not observe it does not prove the behavior disappeared.
- For safety quantities where oversizing costs only performance and undersizing can
  invalidate evidence, retain a conservative high-water measurement. Record why that
  direction is conservative; do not apply this rule blindly to quantities whose
  larger value can damage hardware or data.
- Make proof-establishing side effects complete or explicit. A partial cache flush,
  incomplete seek-away read, shortened scrub, or ignored device status must not be
  reported as an independent reread.
- At a hardware or parser boundary, separate bad subject data from failure of the
  reader, device, transport, or command. Convert only the subject-data class into
  explicitly untrusted evidence that the existing retry, quarantine, or repair
  policy can consume. Keep removal, not-ready, unit-attention, transport, protocol,
  illegal-command, and hardware failures fatal unless independent evidence defines
  a narrower recovery. Test the classifier without hardware, then exercise the real
  failure beyond its original time or location.
- Probe the exact command, transfer shape, flags, and range the runtime will consume.
  A multi-block boundary probe can falsely reject a valid one-block overread; a
  successful capability flag is useless if the read path still pads or bypasses it.
- Treat completion of a hardware control command as evidence for that command only,
  not proof that the next payload command is ready. Serialize control transitions
  with payload I/O, add a measured bounded settle when the device requires one, and
  retry only the exact observed transient while the transition is pending. A repeat
  or unrelated failure stays fatal. Preserve transition and payload shape in the
  failure context so intermittent state bugs can be distinguished from bad ranges.
- A rejected batch, archive, page, or bulk operation does not prove that every
  contained item is bad. When the operation's semantics allow independent reads,
  decompose it at the trust boundary and continue only from independently successful
  item results. Do not turn a command-shape, transport, or container failure into
  damaged-subject evidence. If any required item still fails, preserve that item's
  exact identity and failure class and stop under the existing fatal policy. Bound
  the fallback and record successful use so compatibility recovery remains visible.
- Nested recovery must snapshot and report the innermost failing item before another
  operation can overwrite shared error state. Do not attach a child failure to its
  parent's range, count, or identity. A child failure may enter an untrusted-subject
  path only when independent parent evidence and a bounded repeat corroborate that
  narrower classification, and no bytes from a rejected attempt are consumed. Every
  different repeat keeps its own fatal class.
- Size an edge or range probe from the real configured offset or requirement. Proving
  the nearest block does not prove a larger correction range.
- Persist semantic evidence roles, not only interchangeable values. Test, Copy,
  baseline, confirmation, and tie-break reads must keep their names; a third read
  must not silently replace or be mislabeled as one of the first two.
- Publish immutable evidence when its producing phase completes. In a long
  multi-pass operation, do not hide a completed Test, scan, validation, or receipt
  until a later Copy, publication, or tie-break finishes. Label phase evidence
  separately from the final composite outcome, preserve prior roles not replaced by
  the new phase, and isolate presentation callbacks from the producer.
- If a later confirmation, tie-break, publication, or indexing phase fails, do not
  collapse an already completed recoverable stage into a generic failure and delete
  it. Keep composite assurance separate from phase completion. Retain the owned,
  validated stage in an explicit held or pending state with bounded user resolution;
  never promote it automatically.
- Separate machine-stable control artifacts from human-facing sidecars. Control
  markers, receipts, and discovery keys keep contract-stable names. Human exports
  should carry a sanitized, length-bounded identity that remains meaningful when
  copied alone. During migration, accept legacy names, reject ambiguous duplicates,
  and keep overwrite and repair discovery extension-aware without guessing.
- Bind every displayed or persisted hardware result to the exact physical device
  that produced it. On multi-device systems, make selection explicit, invalidate
  stale identity and evidence immediately when it changes, and prevent a settings
  view from showing device A's calibration while an operation uses device B.
  Lock device selection for the lifetime of a hardware-owning operation or prove
  that the operation uses an immutable selection snapshot.
- Exercise the real device beyond the prior failure time, window, or state
  transition. A quick open/TOC smoke does not prove repeated flush, seek, reread, or
  end-of-media behavior.
- Report unavailable hardware, wedged device handles, access-denied resets, and
  incomplete probes as explicit evidence gaps. Do not convert them into a negative
  capability result or a passing software test.

### Transaction and atomic-publication checklist

Use this checklist for repair, import, output publication, multi-file encoding, migration artifacts, or any operation advertised as atomic:

- Resolve canonical source, staging, quarantine, and final paths; enforce containment and reject reparse or traversal surprises where the threat model requires it.
- Reserve the destination against concurrent writers. State the coordination domain
  (thread, process, terminal session, host, or shared filesystem); a session-local
  mutex does not serialize another session or machine that can reach the same store.
- Create a unique same-volume sibling stage with an unpredictable ownership token. Delete, move, or quarantine it only after proving ownership; a path name alone is not ownership.
- Write only to the stage. Keep the source and final destination unchanged until all producers finalize successfully.
- Check every finalize, flush, close, and child-process result. Require the exact expected output set, nonempty required files, and no silent missing tail outputs.
- Reopen the staged result through an independent reader or validator. For repairs, prove the repair engine actually applied the intended correction and validate the repaired copy, not the source or an in-memory promise.
- Define the preserved representation as well as the payload. Test source-derived
  filenames, tags or properties, artwork, sidecars, ordering, timestamps when
  promised, and any format-specific fields. A repaired payload with synthetic names
  or dropped metadata is not a complete preserved result.
- Separate user-authored or identity metadata from payload-dependent verification
  claims. Preserve representable human/custom fields exactly, but recompute or
  deliberately remove stale checksums, confidence values, signatures, and proof tags
  after the payload changes. Never copy old proof merely to make raw tag sets match.
- Give preservation and recovery operations their own policy. Do not let a user's
  ordinary conversion preferences silently disable source names or metadata that the
  preservation contract requires. Apply metadata before the final independent
  validation and publication step so the oracle covers the bytes the user receives.
- Derive destination names in a contained stage and reject collisions after every
  normalization, extension change, and case-folding rule that the destination
  filesystem applies. Do this before writing any output.
- If a producer can finish with a recoverable degraded result, expose recovery from
  that producer's completion path as well as from any later maintenance or verify
  screen. Exercise both routes and every supported output shape; proving that repair
  works after a user manually reopens one file does not prove that a completed
  multi-file, image, batch, or transactional output can reach it safely.
- Test the platform's real lease and rename behavior before designing a proof handoff.
  On Windows, a child file opened with delete sharing can still prevent a parent
  directory rename. Do not infer directory-move compatibility from a file-share
  flag or another operating system's behavior.
- If proof leases cannot remain open across the atomic move, retain destination
  reservation and transaction ownership, move into a pending-publication state,
  reopen and revalidate the exact result at its destination, then expose success.
  On post-move validation failure, clear the assurance claim and quarantine the
  owned result instead of publishing it or deleting the only evidence.
- Bound both stored bytes and decoded/expanded bytes before parsing persisted or
  compressed input. Validate the complete object graph, not only syntax and a non-null
  root, before a read-modify-write path can republish it.
- Write a completion marker only after validation, then publish with one same-volume atomic rename into an absent final destination.
- Never replace a pre-existing destination merely because its path matches an
  expected release or output name. Require a validated ownership/completion receipt
  that binds the exact tree; otherwise refuse and preserve the foreign directory.
- Before moving an existing owned destination aside, write a recovery journal. Bind
  stage, backup, cleanup, and journal actions to one unpredictable transaction token;
  validate the published destination before deleting the backup. If rollback cannot
  be proven, retain the backup and journal with a deterministic recovery path.
- Name the exact commit point. Once it succeeds, cleanup, marker removal, reservation
  release, advisory callbacks, or diagnostics must not reclassify the operation as
  failed and invite a destructive or duplicate retry. Make post-commit cleanup
  best-effort or report it through a separate non-transactional channel.
- On failure or cancellation, expose no partial final result. Preserve the original source and any independently verified evidence; quarantine or remove only owned incomplete staging.
- During backup recovery, never rotate a known-bad primary over the last validated
  backup. Promote the validated copy, quarantine corrupt evidence under a separate
  role, and fault-test the next restart plus a second corruption.
- Define stale-stage recovery without allowing a replaced or attacker-controlled directory to be deleted.
- Fault-test mid-write failure, finalization failure, verification mismatch, cancellation, concurrent publication, missing or extra outputs, stale recovery, stage replacement before cleanup, and cleanup failure immediately after the commit point.

State the guarantee precisely. Atomic publication hides partial final results; it does not by itself prove durable storage, content correctness, or successful repair.

### Subprocess timeout and termination checklist

Use this for external tools, isolated parser/fuzz children, helpers, and service probes:

- Bound startup, active work, and stdout/stderr draining according to the actual
  progress contract; a total-runtime timeout is not interchangeable with an idle
  timeout.
- On timeout, attempt process-tree termination where the runtime supports it and
  preserve the termination failure as explicit evidence.
- Bound the post-kill reap as well. Never follow a timed `WaitForExit` with an
  unbounded wait on the path that exists specifically to detect nontermination.
- Ensure redirected pipes cannot fill while the parent is waiting. Drain
  concurrently or impose an output cap appropriate to the harness.
- Return failure if the child cannot be proven terminated. Cleanup failure must not
  be silently converted into a passing timeout assertion.
- Fault-test a normal exit, timeout plus successful kill, kill failure, and a child
  that remains alive after the kill deadline where the platform permits injection.

### Toolchain and installer coherence checklist

Use this when a build is blocked on an IDE, SDK, compiler, workload, extension, or
package-manager installation:

- Distinguish selected components, installer inventory, files on disk, and a working
  target build. None proves the next layer by itself.
- Verify a claimed restart from the operating system's boot timestamp and pending-
  reboot state. A returned user session does not prove that locked installer files
  were replaced.
- Check that resolvers, tasks, and their dependent assemblies form one compatible
  version set. Presence of every file can still leave a split, unloadable toolchain.
- Record the exact host/toolset tuple when one IDE or build host drives another
  installation's compiler, targets, or SDK. A successful hybrid build exercises that
  tuple; it does not clear the unhealthy installation.
- Run one direct build of the previously blocked target. Classify a remaining failure
  as source defect, toolchain blocker, native dependency blocker, or unexercised path
  instead of treating installer success as a passing build.
- For release collection, distinguish a fresh exact staging tree from fresh compiled
  inputs. Require a build receipt that binds the source commit or dirty-worktree
  fingerprint, configuration, platform/toolchain tuple, and hashes of compiled
  inputs, or make one command clean, build, receipt, collect, and validate without
  accepting pre-existing binaries.
- Treat a build receipt as generated evidence, not caller-supplied assertions. It
  must record the exact commands that actually ran, their exit results and logs, the
  resolved executable identities, the complete planned source/input set, and every
  consumed native or generated binary, including ignored files outside normal source
  globs. Reject receipts whose tool path, command sequence, source set, input digest,
  or artifact digest cannot be reconciled with the release plan.
- Keep clean, dependency preparation, build, receipt creation, collection, and
  validation under one orchestrator and one release lease. A collector may borrow
  that already-held lease, but it must not create a gap in which another process can
  replace inputs between receipt creation and copying.
- Scope the release lease to every shared mutable output, not only the final artifact
  name. Two versions, plans, helper scripts, or native builders that write the same
  leaves must contend on one stable lock. Helper entrypoints must not expose a
  supported path that creates a receipt or publishes under a shorter replacement
  lease.
- Recover or refuse a retained build intent before deleting any prior output. Validate
  its exact schema, plan identity, and ownership first; preserve the intent and logs
  as abandoned-build evidence. A corrupt or foreign intent must leave every output
  leaf untouched.
- Make failed-intent inspection non-destructive. A refused recovery must not consume
  the only lease token or ownership fact needed for a later valid recovery. If source
  must change to fix the failed build, require an explicit stale-intent abandonment
  mode, preserve the original intent bytes, and start the replacement build under a
  new receipt only after archival succeeds.
- Do not use a tracked file as the sentinel for an expanded archive when that file is
  present in the repository's partial overlay. Pin the archive digest, validate every
  destination, repair only missing archive-owned files, and reject unexpected drift.
  Prove the clean-checkout shape in a fixture where tracked overlays exist but
  archive-only build inputs do not.
- Git status is not a source receipt for expanded or generated inputs hidden by
  ignore rules or local excludes. Bind the exact consumed tree independently, including
  locally patched archive targets; proving that patch hunks reverse-apply does not
  detect unrelated edits elsewhere in the same file.
- Keep pinned dependency worktrees immutable during builds. Materialize the recorded
  commit plus tracked patches into an owned ignored stage, record hashes for the pin,
  patches, and staged file manifest, and reject dirty or gitlink-drifted inputs.
  Redirect every project, restore, build, test, packaging, and release consumer to the
  stage, then assert dependency status is unchanged after the real build.
- Treat stage-local restore assets, generated files, and compiler output as
  disposable build state, not source. Prepare the stage before restore, validate
  reusable stages against their manifest, and quarantine a stage whose ownership or
  inputs cannot be proven.
- Pin one compatible compiler toolset across a native project graph unless the graph
  explicitly documents and tests a mixed-toolset contract. A parent project choosing
  one Visual Studio installation does not prove its child projects chose the same
  compiler or targets.
- Evaluate warning or policy baselines from the logs of the build whose bytes are
  actually collected. A successful preflight rebuild does not gate a later rebuild.
  Bind the baseline digest, exact log digests, normalized findings, and pass result to
  the same receipt before publication.
- Bind collected bytes directly to the receipt with hashes of the complete receipt
  and the exact source artifacts, then copy from handles that deny mutation during
  the read. A matching build id or tree label is not artifact identity.
- Before repair or reinstall, inspect installer logs for locked processes, deferred
  replacement codes, and the first failing package. Prefer a real restart and bounded
  repair over blind component churn.

### Duplication and hot-path refactoring checklist

Use this before consolidating repeated code, especially in streaming, device, parser,
publication, repair, or destructive paths:

- Classify the repetition as shared behavior, pure computation, boilerplate, or a
  multi-step process. Choose a language-native helper, generic, base type, generated
  code, or whole-sequence extraction only after the category is clear.
- Prove semantic equivalence before extracting. An extra guard, bound, verification,
  cleanup, or error path is a possible invariant, not cosmetic drift.
- For each hot loop, record the existing allocation, blocking, I/O, dispatch, and call
  shape. Do not introduce allocation, formatting, boxing, captured closures,
  iteration helpers, locks, waits, synchronous I/O, or extra dynamic dispatch without
  a measured budget that permits it.
- Keep service seams testable, but prefer direct or statically resolvable calls where
  per-item dispatch is material. Apply inlining hints only to measured small hot
  helpers; a hint is not performance evidence.
- When duplicated code writes, moves, repairs, publishes, or deletes artifacts,
  extract the complete ordered transaction rather than isolated lines. Centralize its
  guards, commit point, validation, rollback, and cleanup semantics together.
- When a rendered collection can filter, skip, sort, or fail individual rows, never
  use its ordinal as persisted-model identity. Carry a stable id or model reference
  through selection, mutation, playback, and deletion paths.
- Do not imitate a mechanism from another language when the active stack has no honest
  equivalent. Name the mismatch and use the simplest native construct that preserves
  the contract.
- Verify behavior and failure parity first, then measure allocations and throughput on
  the touched loop. List look-alike blocks deliberately left separate and why.
- For asynchronous telemetry, visualization, or progress consumers, do not replace
  per-event arrays with one reused buffer unless ownership proves consumption is
  complete. Prefer a bounded preallocated SPSC ring or mailbox; a slow UI may drop
  presentation samples, but it must not block, allocate on, or alter the producer's
  correctness path. Test stalled-consumer bounds, slot lifetime, ordering, scaling,
  producer-thread allocation, and queue-full behavior.

### Compatibility, defaults, and user-facing truth checklist

Use this when a remediation changes defaults, serialized settings, public plugin
surfaces, network connection code, or UI claims about side effects:

- Before changing a `DefaultValue`, default-on flag, or constructor default, inspect
  every serializer and migration path. Historical values omitted because they matched
  the old default are indistinguishable from "unset"; a new default can silently
  reverse an existing user's choice. Preserve the old default or provide an explicit
  migration, opt-out, and rollout/benchmark evidence.
- Treat public member type/signature changes in plugins and legacy assemblies as a
  binary-compatibility event even when every in-repo consumer rebuilds. Keep a safe
  compatibility shim or deliberately version and document the break.
- Treat approved in-process plugins as code-execution grants, not sandboxes. A staged
  registrar can make its own collection publication all-or-none, but it cannot claim
  rollback of arbitrary constructor, module-initializer, thread, native-load, or
  mutation side effects without real isolation.
- A network `Connect` or HTTP success status does not prove a streaming integration.
  Exercise the first real payload write, sustained body transfer, final flush/close,
  authentication rejection, and any ancillary metadata request. Observe the server
  side when the protocol is full-duplex or acknowledgement timing is ambiguous.
- Propagate persistence and publication outcomes to the UI separately from the main
  operation's result. Clear the prior job's status when a new job starts, and never
  display "saved", "recorded", "calibrated", or "published" after that side effect
  failed or was not attempted.
- Treat UI notifications, progress callbacks, and observer events as untrusted
  ancillary consumers. Marshal UI mutation to the correct thread and contain
  listener exceptions after the durable state transition. A broken display
  subscriber must not turn a successful calibration into failure, abort a rip, or
  skip cleanup of a correctness-critical ownership scope.
- When a formerly swallowed failure becomes an exception or explicit error, inspect
  every reachable consumer. Ancillary failures must be contained at the boundary
  where product policy says the primary operation should continue.

### External content, provider credentials, and selection snapshots

Use this when a feature imports local files, downloads optional third-party content,
stores a provider credential, or lets a mutable UI choice feed a long-running job:

- Verify the provider's current authentication contract from its authoritative
  documentation. Collect the smallest usable secret, such as an API key or token;
  do not collect an account password when the protocol does not use it.
- Keep optional providers off until their credential, attribution, distribution
  terms, rate policy, and user-visible source label are satisfied. One provider's
  failure must not disable the primary operation or erase other candidates.
- Protect unrelated secrets with purpose-separated current-user or platform
  protection. Test round trip, wrong purpose, corrupt value, clear behavior,
  migration when applicable, and diagnostic redaction.
- Treat credentials in URL paths as secrets even over HTTPS. Never log the request
  URI or response body, and hash any persisted or in-memory cache key that would
  otherwise retain the credential or private query.
- Accept one local object at a time unless batch semantics are designed explicitly.
  Reject directories, links or reparse points when they cross the threat model,
  unsupported magic, oversized encoded input, unsafe decoded shape, and expansion
  beyond the declared cap before expensive allocation.
- Open a local input once, enforce the byte cap while reading, validate the retained
  bytes, and carry those bytes forward. Do not validate one path read and later
  publish another. Never log a private source path unless product policy explicitly
  permits it.
- Separate candidate ranking from automatic eligibility. Low-confidence,
  alternate-side, auxiliary, watermarked, or browser-only content may remain
  inspectable without becoming an automatic choice.
- Bind a user override to the current subject or selection generation. State which
  events preserve it, re-derive it, return to automatic choice, or clear it.
- Freeze an immutable content snapshot when a job starts. Later refresh, selection,
  resize, provider completion, or another window may affect only a later job.
- Test malformed, truncated, oversized, renamed, replaced, multi-item, cancellation,
  provider outage, rate limit, stale generation, manual override, automatic fallback,
  no-selection, and final published-output cases.

Edit rules:
- keep changes small and single-purpose
- do not bundle unrelated fixes
- keep unknowns explicit
- stop and document if a selected item turns out to touch a protected area or hidden control-plane path
- update `docs/unknowns/` if new uncertainty appears
- update the coverage ledger statuses after the fixes
- after edits, run `06-writing-hygiene.md` on all touched text
- if the selected work is comment-only, use `03-critical-path-comments.md` and then `06`, or use `combined-03-06-loop.md`

### Bounded execution rule for Step 2

Do not let one remediation run march across the repo.

Default limits unless the user gives a different bound:
- review checkpoint after 10 commits
- hard stop after 20 commits for review
- stop sooner if the current slice is complete, a new protected-area dependency appears, or evidence goes soft

Commit guidance:
- one commit usually covers one backlog item, one slice checkpoint, or one tightly related docs plus code unit
- do not let a commit sprawl across unrelated slices
- after each commit or small batch, update the plan, coverage ledger, and unknowns notes before moving on
- if clean commit boundaries are impossible, stop early and ask for review

### TODO lifecycle

A safe fix sometimes has to leave a `TODO` behind because the full fix is approval-gated, blocked on evidence, or larger than the current commit budget. Track every such TODO end-to-end so it does not become the next generation of dark code.

Lifecycle:

1. **Plant** - the TODO comment names the area, the reason, and the unknowns or backlog entry it points to. A bare `TODO: fix this` is not allowed in remediation work.
   ```ts
   // TODO(adc): redaction here is shallow because the trace SDK formats the body
   // upstream. Tracked in docs/review/remediation-backlog.md#auth-trace-redaction
   // and docs/unknowns/logging-audit.md#auth-trace-shape.
   ```
2. **Track** - every planted TODO has a matching backlog row (or unknowns row, when evidence is still soft). The row carries the same status vocabulary the rest of the workflow uses (see `00-conventions.md`).
3. **Clear** - when the underlying work lands, the TODO is removed in the same change that closes the row. The change record names both the TODO removal and the row that closed.
4. **Audit** - the maintenance harness (pass `10`) should add a reviewer-checklist item asking whether new `TODO(adc):` lines were planted with backlog references, and whether any cleared TODOs left behind a stale comment.

If a TODO outlives the engagement that planted it, the next preflight should treat it as a stop condition and decide whether the row still makes sense before any new pass extends it.

## Step 3: Resolve evidence gaps for blocked or uncertain items

Can run in parallel with Step 2 for blocked items. Do not wait for every safe fix to land if a gap is holding a risky item open.

Read-only pass unless a doc-only update is needed.

Create or update:
- `docs/review/evidence-gap-check.md`
- `docs/unknowns/evidence-gap-pass.md`

For each item:
- state the claim that is not yet proven
- list the exact files, scripts, workflows, configs, or docs checked
- state what evidence supports the concern
- state what evidence is still missing
- state the confidence level (`verified` | `inferred` | `unknown`)
- name the next best repo-local check
- name any out-of-repo boundary that blocks certainty
- downgrade confidence where needed instead of flattening uncertainty away

Focus on:
- hidden runtime entrypoints
- CI or CD and release paths
- migrations and backfills
- support scripts and admin tools
- remote config and feature flags
- notebooks and one-shot ops scripts
- sibling repos, submodules, mirrored trees, external control planes
- locale, copy, authored-content, generated-prose, saved-text, and render-on-read boundaries

## Step 4: Prepare approval packets for protected areas

Start as soon as an approval-gated item has enough evidence. Do not wait for unrelated evidence gaps in other slices.

No application code edits.

Create or update `docs/review/approval-packets.md`.

For each approval-gated item:
- exact area and files
- protected-area category
- why the risk matters
- current evidence
- smallest safe edit after approval
- what could break
- verification plan
- rollback plan
- what human decision is required
- which unknowns still block the edit, if any

## Step 5: Re-run the verification loop on touched slices only

Run after each meaningful safe-fix batch and again at the end of a remediation wave.

Inputs:
- updated coverage ledger
- remediation backlog
- recent code diffs
- updated unknowns files
- logging audit
- adversarial pass
- scenario stress-test

Check whether recent fixes actually closed the targeted holes without creating new dark areas.

Do:
- update coverage statuses honestly
- confirm docs match current code
- confirm no new hidden entrypoints or control-plane paths were introduced
- confirm no sensitive data was added to logs, comments, tests, docs, examples, screenshots, or commit text
- confirm protected areas were not edited without approval
- identify what remains uncovered
- estimate the next slice to run
- name whether `04`, `07`, `08`, or `10` should rerun on the touched slice

Extra checks when they fit:
- if observability code changed, rerun the logging audit on the touched slice
- if a control-plane path changed, refresh the map or adversarial notes for that slice
- if comment-only work landed, confirm the comments still match the code after the final cleanup pass

## Step 6: Install or refresh the maintenance harness when the repo is ready

Use `10-maintenance-harness.md` when the repo needs ongoing guardrails for future contributors.

Good times to run 10:
- right before a broad remediation wave (PR and review guardrails in place first)
- right after the first remediation wave (lock in the new rules)
- after the repo gains a new runtime, new release path, new control-plane path, or new protected area
- after the repo gains locale packs, transcreation tooling, generated prose, or a new saved-text contract

## Optional companion loop: combine 03 and 06 slice by slice

Use this loop when the next useful work is comment-only clarification on critical paths. Especially useful when the coverage ledger shows risky slices still hard to explain but not yet needing logic changes.

How to run:
1. pick the next uncovered or weakly explained slice from the coverage ledger
2. run `03-critical-path-comments.md` on that slice only (or use `combined-03-06-loop.md`)
3. immediately run `06-writing-hygiene.md` on all touched comments, docs, summaries, and unknowns files
4. commit that slice as one bounded unit
5. update the coverage ledger and `docs/unknowns/critical-paths.md`
6. repeat on the next slice

Stop rules for the comment loop:
- review checkpoint every 10 commits
- hard stop after 20 commits for review unless the user says continue
- stop sooner if the slice touches a protected area, reveals stale maps, or uncovers a hidden control-plane path that needs a different pass first

Good summaries after each slice:
- what code path was clarified
- which invariants or edge cases the new comments explain
- what remains unclear
- which next slice should run
- whether the repo is still in comment-only mode or now needs a behavior-preserving cleanup pass

## Acceptance checklist

The result should:
- turn the first rollout into an actionable backlog instead of a pile of notes
- keep safe fixes bounded and reviewable
- keep protected areas behind approval
- turn evidence gaps into explicit checks instead of guesses
- verify touched slices after each batch
- keep the coverage ledger honest
- give the user a clear stop point for review
- make the next slice obvious
- track every planted TODO from creation to clearance
