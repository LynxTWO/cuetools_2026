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

## ADC-CUETOOLS-003: OS-enforced exclusion semantics do not port

- Status: promoted 2026-08-15 to references/07-adversarial-review.md (finding-class verification), with the reviewing skeptic's corrected wording
- Scope: repo-agnostic
- Lesson: A mutual-exclusion or file-locking mechanism that relies on one
  operating system's enforced semantics (for example, mandatory file-share
  modes) can silently become a no-op when the code runs on another OS.
  Exclusion claims must be proven by an adversarial two-process live test
  on every supported OS - a second claimant must observably fail - not by
  reading the API contract.
- Evidence: a cross-process drive lease that was correct on its original
  OS failed open on a second OS: the second process acquired a held
  device and operated on it concurrently, observed live. The fix was an
  explicit advisory lock; the two-process denial test now passes.
- Limits: applies to ported or multi-platform code with concurrency or
  exclusion guarantees; single-platform repos only need the live test on
  their one platform.
- Proposed target: references/08-scenario-stress-test.md (new scenario:
  ported exclusion primitives) and references/07-adversarial-review.md
  (challenge: "which OS enforces this?").
- Proposed change: add a scenario/challenge entry requiring a live
  second-claimant test per supported OS for any exclusion mechanism.

## ADC-CUETOOLS-004: build-mode flags change dev-build runtime capabilities

- Status: promoted 2026-08-15 to references/14-deterministic-verification.md (capability-restricting build flags), with the reviewing skeptic's corrected wording
- Scope: repo-agnostic
- Lesson: A build or publish configuration flag can alter the runtime
  capability set of builds that never ship (for example, an
  ahead-of-time-compilation opt-in disabling reflection-based
  serialization even in ordinary debug runs). Verification must either
  run in the same capability mode as the shipped artifact or the
  restricted mode must be treated as the baseline for all builds; and
  when a host adopts such a flag, every site using the restricted
  capability needs a deterministic sweep, not just the paths that failed
  first.
- Evidence: three separate production-path failures from the same flag in
  one repo, weeks apart, the last one deep inside a commit path and
  found only by a live run; a grep-style sweep for the restricted
  pattern would have found all three at adoption time.
- Limits: the specific mechanism is toolchain-dependent; the general
  rule (publish-mode flags leak into dev-build behavior) is not.
- Proposed target: references/14-deterministic-verification.md.
- Proposed change: add a capability note: when a build flag restricts a
  runtime capability, generate a one-shot deterministic sweep for every
  use of that capability and gate on zero unmigrated sites.

## ADC-CUETOOLS-005: characterize hardware quirks before classifying them

- Status: promoted 2026-08-15 to references/11-remediation-loop.md (characterizing a component outside the repo), with the reviewing skeptic's corrected wording
- Scope: hardware-facing repos
- Lesson: When a device rejects an operation, run a minimal deterministic
  probe matrix (vary one command parameter at a time, at several
  addresses, with sibling devices as controls) before changing any
  failure-classification rule. Scope any resulting workaround to the
  exact device identity (vendor, product, firmware), and prefer
  reshaping the request plan over reinterpreting failure codes.
- Evidence: a drive deterministically aborted two specific transfer
  sizes at any address; a 27-shape probe across three devices isolated
  the exact failing shapes in minutes, the fix reshaped the request plan
  without touching classification, and a later same-code failure on a
  sibling device was recognized as a device-state wedge rather than a
  command-shape problem because the probe separated the two (the
  wedge's own cause was initially misattributed and corrected only by a
  second organic incident - probe evidence separates state from shape,
  not cause from coincidence).
- Limits: needs at least one control device or a known-good baseline;
  probe cost must be trivial relative to the operation it explains.
- Proposed target: references/repo-verification-profiles.md
  (hardware-facing repo profile).
- Proposed change: add probe-matrix-before-classification guidance and
  the exact-identity scoping rule for device workarounds.

## ADC-CUETOOLS-006: full-cmdline process matching kills the agent's own shell

- Status: promoted 2026-08-15 to references/10-maintenance-harness.md (self-matching process selectors), with the reviewing skeptic's corrected wording
- Scope: agent-harness operations
- Lesson: Agent harnesses execute shell commands wrapped in an
  interpreter whose command line contains the command text itself, so
  any process search matching full command lines (pgrep -f, pkill -f)
  matches the agent's own wrapper whenever the pattern appears in the
  command being run - and a kill then aborts the agent's own chain.
  Match by process name (-x), kill by saved PID, and isolate destructive
  process management in its own command.
- Evidence: three self-kill incidents in one day (chains aborted with
  exit 144, work after the kill silently skipped) until the pattern was
  identified; zero since adopting name-exact matching.
- Limits: harnesses that exec without a wrapping interpreter are not
  affected; the isolate-kills rule still applies as defense in depth.
- Proposed target: host addenda (references/host-adapters.md and the
  agent-host addendum files).
- Proposed change: add a shell-operations caution with the three rules.

## ADC-CUETOOLS-007: tests that read source by path join the move checklist

- Status: promoted 2026-08-15 to references/11-remediation-loop.md (moves and extractions), with the reviewing skeptic's corrected wording
- Scope: repo-agnostic
- Lesson: Contract tests that read source files from disk (asserting
  ordering, patterns, or invariants in the text) fail on file moves and
  do not show up in compiler-driven refactoring. Any extraction or move
  must include a deterministic sweep for path literals referencing the
  moved files, across test and script trees.
- Evidence: a 22-file extraction broke six source-inspection contract
  tests that compiled cleanly; a filename-literal sweep found the
  complete set at once, and a follow-up move caught its stragglers the
  same way.
- Limits: only repos using source-inspection tests or scripts keyed to
  paths; the sweep is one grep per moved filename.
- Proposed target: references/11-remediation-loop.md (move/extraction
  checklist).
- Proposed change: add the path-literal sweep as a required step when
  files move.

## ADC-CUETOOLS-008: history rewrites are receipt events

- Status: promoted 2026-08-15 to references/09-artifact-gc.md (history rewrites and other identifier changes), with the reviewing skeptic's corrected wording
- Scope: repo-agnostic (evidence-keeping repos)
- Lesson: Rewriting version-control history invalidates every recorded
  commit hash in docs and receipts, orphans any submodule pinned to
  rewritten commits, and cannot scrub host-retained refs (merged PR
  views keep old objects). A rewrite plan must include: tree-identity
  verification before pushing, submodule-pin remapping through the
  rewrite's commit map, a sweep updating recorded hashes with an
  auditable note explaining why they changed, and an honest statement of
  what the rewrite cannot remove. Release artifacts whose evidence cites
  pre-rewrite hashes must be withdrawn or reissued, not left dangling.
- Evidence: a two-repo rewrite (1,232 + 86 commits) executed with
  byte-identical tree verification, 82 remapped submodule pins, a
  17-citation hash sweep with a dated receipt note, withdrawal of three
  releases whose evidence archives cited pre-rewrite hashes, and a fresh
  release issued from the rewritten history.
- Limits: hosts differ in which refs they retain; the "cannot scrub PR
  views" statement is host-specific and should be verified per host.
- Proposed target: references/09-artifact-gc.md or a new subsection in
  references/10-maintenance-harness.md.
- Proposed change: add a history-rewrite checklist tying rewrites to the
  receipt/evidence ledger.

## ADC-CUETOOLS-009: a binary platform split fails open on the third platform

- Status: promoted 2026-08-15 to references/07-adversarial-review.md (dogfeeding targets), with the reviewing skeptic's corrected wording
- Scope: repo-agnostic (any multi-platform codebase)
- Lesson: Guards written as "if not Windows then Linux" (or any
  two-platform else-branch) silently route a third platform down a path
  built on another OS's assumptions. Coarse platform identifiers make
  this worse: .NET reports PlatformID.Unix for both Linux and macOS, so
  a check that reads as "Unix means Linux" is wrong the day a Mac runs
  it. The failure is quiet by construction - the wrong branch often
  half-works (file probes return false, locks no-op, enumerations come
  back empty) - and the worst cases fail open: an exclusion mechanism
  that excludes nobody. The rule: every platform branch names the
  platforms it was actually built and tested for, and unlisted
  platforms fail closed (throw) on operations while staying honest on
  observations (enumerate nothing rather than fabricate). Audit for
  else-branch platform assumptions before targeting a new OS, not
  after.
- Evidence: an audit ahead of a planned macOS port found five sites in
  one rip stack where "not Windows" meant Linux: transport open would
  have died in a confusing DllImport error, tray control and identity
  resolution would have quietly no-opped, drive enumeration could have
  fabricated letters from mounted-volume names, and the cross-process
  drive lease would have acquired with no exclusion at all
  (FileShare modes are not enforced between processes off Windows and
  FileStream.Lock is unsupported on macOS). A prior live incident on
  Linux had already proven the lease fail-open class (two processes
  read one drive concurrently). All five converted to precise guards
  plus PlatformNotSupportedException; existing suites on both
  supported platforms stayed green.
- Limits: the observation/operation split is a judgment call per site;
  a headless service may prefer failing closed even on enumeration.
  Framework-specific identifier coarseness (PlatformID.Unix) is a .NET
  detail; the general rule is the else-branch, not the API.
- Proposed target: references/07-adversarial-review.md (hidden
  assumption checklist) or references/02-architecture-map.md trust
  boundaries.
- Proposed change: add "unlisted platforms fail closed" to the
  adversarial review checklist for platform-split code.

## ADC-CUETOOLS-012: Cleanup steps must not sit behind a fallible command

- Status: ready
- Scope: repo-agnostic
- Lesson: In a shell chain, a cleanup or revert step placed after `&&` is skipped whenever any earlier link fails, and the surviving output still reads like completion; run cleanup unconditionally, then assert the working tree is clean before committing.
- Evidence: a chain of the form `copy A && copy B && remove scratch && restore harness && build` lost its first link on a missing directory, so a scratch test file and a locally modified test harness rode into the commit; every continuous-integration lane went red while local runs stayed green, because the scratch test wrote to a machine-specific path. Same family as two earlier incidents in this repository where a piped verification discarded a non-zero exit status and where a checks query returned a previous commit's results.
- Limits: applies to shell-composed automation with conditional operators; a task runner that always executes teardown, or a language-level `finally`, already provides this.
- Proposed target: references/14-deterministic-verification.md
- Proposed change: add cleanup-and-revert ordering as a third named failure mode alongside discarded exit status and stale check results, with the rule that teardown runs unconditionally and the tree is asserted clean before the artifact is created.
