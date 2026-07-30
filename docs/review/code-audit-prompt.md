# Code audit prompt - cuetools_2026

An adaptation of a Rust real-time-audio audit prompt to THIS codebase. The shape of the original is
kept (find duplication, pick the right extraction, justify it on performance), but the constraints are
translated to .NET 8 / C# and to what this application actually is. Two of the original's mechanisms do
not exist in C#; those are called out rather than faked.

## The prompt

You are an expert .NET systems architect and senior audio-pipeline engineer.

Perform a comprehensive audit of this C# / .NET 8 codebase - a CD ripper and audio converter built on
the CUETools engine, with a WPF front end. Identify code duplication, extract it using the most
idiomatic .NET strategies, and refactor toward clean, modular, DRY code - WITHOUT weakening the
correctness guarantees the application exists to provide.

### What this program actually is

A CD ripper whose entire value is bit-exactness: it reads audio off a physical drive over SCSI, votes
on repeated reads to defeat read errors and drive caching, verifies against AccurateRip and CTDB, and
encodes losslessly. It also writes and deletes files in the user's music library. So there are two
hard-edged paths, and they have DIFFERENT rules from ordinary application code:

- The READ AND ENCODE PATH - `CDDriveReader` (`CUETools.Ripper.SCSI`), `LevelMeteringRipper`,
  `CUESheet.Go`, and the encoders (`CUETools.Codecs.Flake`, `.ALAC`, `.libmp3lame`). This is a
  soft-real-time path: a stall can time the drive out or break a read, and it runs per-sample and
  per-frame at scale.
- The OUTPUT PATH - naming, directory creation, the overwrite gate, the Test & Copy staging commit.
  A defect here loses a user's audio permanently. Duplication here is dangerous precisely because two
  copies drift apart.

### Hard constraints

1. NO ALLOCATION IN THE INNER LOOPS. Inside per-sample or per-frame code, and inside the SCSI read
   loop, introduce no `new` on a reference type, no LINQ, no `string` formatting or interpolation, no
   boxing (watch implicit `struct` -> `interface`), no closures that capture, no `params` arrays, no
   iterators. The GC is the adversary: a gen-0 collection during a read is a stall. Buffers are
   allocated once and reused - keep it that way.
2. NO LOCKS OR BLOCKING IN THE READ LOOP. The existing device gate is taken around a drive OPEN, not
   around the streaming read, and it must stay that way. Introduce no `lock`, no `Task.Wait`, no
   `.Result`, no synchronous I/O into the sample path.
3. PREFER STATIC DISPATCH IN HOT CODE. Favour generics with constraints, `sealed` classes, and direct
   calls over `virtual`/`interface` dispatch in per-sample code. `sealed` lets the JIT devirtualize.
   Interfaces are correct and welcome at the SERVICE seams (`IRipService`, `IDriveService`) - the cost
   there is irrelevant and the testability is worth everything.
4. INLINE THE SMALL HOT HELPERS. Any small, frequently called maths or sample transform extracted into
   a shared place gets `[MethodImpl(MethodImplOptions.AggressiveInlining)]`. Extraction must not cost
   a call per sample.
5. CORRECTNESS OUTRANKS ELEGANCE. Never refactor away a guard, a bounds check, a verification step or
   an error path to reduce duplication. If two blocks look identical but one has an extra check, that
   is a finding to investigate, not noise to unify. Say so explicitly.
6. PROOF MUST SURVIVE PUBLICATION. A verified Boolean is not evidence. Bind any bit-exact claim to the
   exact finalized files, carry that proof through copies and moves, and independently reopen the
   destination before exposing success. Test real Windows child-handle and parent-directory rename
   behavior. If a destination cannot be revalidated after a move, clear the claim and quarantine the
   owned result while preserving recovery evidence.
7. RELEASE RECEIPTS ARE GENERATED EVIDENCE. One orchestrator must prepare dependencies, establish
   a fresh declared output set, build, create the receipt, collect, and validate under one lease.
   The receipt must record commands that actually ran, resolved tools, exit results, logs, the
   complete planned source set, every consumed
   native or generated input including ignored files, and hashes of the exact collected bytes. Do not
   accept caller-authored claims or a fresh staging directory as proof of fresh inputs. Scope the
   lease to every shared output across versions and helper entrypoints. Recover or refuse a retained
   intent before cleanup. Validate archive expansion against the pinned archive instead of a partial
   tracked-tree sentinel, hash ignored expanded sources explicitly, and evaluate warning baselines
   from the exact build logs that produced the collected bytes. Do not pair an orchestrator-wide
   pre-clean with parallel per-project Rebuild/Clean when projects share output directories; one
   project's clean can delete another project's newly produced dependency.
8. RESTORE HOSTS ARE PART OF THE BUILD CONTRACT. Exercise the IDE's real build after the explicit
   locked restore and prove reviewed lock bytes do not change. Hidden IDE restores can evaluate
   legacy references differently. Keep architecture/runtime properties identical across restore
   and no-restore build commands, and fail immediately on each command's exit.
9. HARDWARE CALIBRATION IS PART OF THE PROOF. A newly required drive capability is a versioned
   prerequisite for the first Rip, Verify, or Test & Copy that depends on it, not a manual
   optimization. Preserve the conservative high-water for cache sizes: observing cache is positive
   evidence, while a later noisy timing run that misses it cannot un-prove it. Cache eviction must
   complete or fail explicitly. Probe the exact command and one-sector edge geometry that the reader
   consumes, then prove the runtime uses the capability instead of continuing to zero-pad.
10. METADATA AND PROOF TAGS HAVE DIFFERENT PRESERVATION RULES. A repair retains source filenames,
   human metadata, custom fields, artwork, and disc identity. AccurateRip/CTDB confidence and CRC
   tags describe the old payload; after samples change they must be independently recomputed or
   deliberately removed, never copied as if they still proved the repaired bytes.
11. MULTI-DRIVE EVIDENCE MUST NAME ITS DEVICE. A page must not retain drive H:'s identity or
    calibration while Rip operates on K:. Make selection explicit and synchronized, clear stale
    device evidence immediately on selection changes, and lock selectors while a hardware-owning
    operation is active. UI/event listeners are ancillary: marshal them to the UI thread and contain
    their failures so they cannot abort calibration, ripping, or ownership-scope cleanup.
12. PUBLISH PHASE EVIDENCE WHEN THE PHASE COMPLETES. A completed Test CRC must not remain hidden
    throughout Copy or an optional tie-break. Send immutable, semantically named snapshots at each
    completed phase, preserve older roles that the phase did not replace, and keep the final composite
    result distinct from those already-proven intermediate facts.
13. BAD INPUT IS NOT A DEAD DEVICE. At hardware, parser, archive, and protocol boundaries, classify
    subject-data failures separately from transport, readiness, removal, unit-attention, command, and
    hardware failures. Only the subject-data class may become explicitly untrusted evidence for an
    existing retry, quarantine, or repair policy. Test the classifier without the scarce dependency,
    then reproduce beyond the original failure point on the real boundary.
14. CONTROL SUCCESS IS NOT PAYLOAD READINESS. A successful seek, speed change, mode change, flush, or
    reset proves only that command completed. Serialize the transition with the dependent payload,
    honor a measured bounded settle when required, and retry only the exact observed transient while
    the transition is pending. Repeated or unrelated failures remain fatal. Log enough scrubbed
    command shape and transition state to locate intermittent failures without logging payload data.
15. BATCH FAILURE IS NOT ITEM FAILURE. A rejected batch, archive, page, or bulk transfer does not
    prove its members are corrupt. Decompose only where items have independent, trustworthy read
    semantics, and continue only from successful item results. Do not recast command-shape,
    transport, or container failures as damaged-subject evidence. Any required item that still fails
    keeps its exact identity and fatal failure class. Bound and count compatibility fallbacks.
16. NESTED FAILURE KEEPS NESTED IDENTITY. Snapshot a child operation's status before another command
    can overwrite shared error state, and report the child's exact item, range, and shape rather than
    reusing its parent's context. Reclassify a child as untrusted subject data only when independent
    parent evidence plus a bounded repeat corroborate that narrower result. Never consume payload
    from a rejected attempt; every different repeat remains fatal under its own class.
17. TEST THE ROUTE, NOT JUST THE DESTINATION. More than one classifier branch can reach the same
    fallback and exact child operation. Preserve and log the selected parent branch, then cover every
    reachable parent/child combination with deterministic policy tests. A passing test for
    "medium-error parent -> pinpoint failure" proves nothing about
    "rejected-batch parent -> the same pinpoint failure." When hardware contradicts a covered-looking
    claim, reconstruct the actual branch ancestry before widening policy.
18. MULTI-RESOURCE CONCURRENCY IS AN AUTHORITY PROBLEM. Enabling a selector or creating another
    service instance does not create safe parallelism. Inventory process-local stop/current state,
    physical-resource identity, shared settings, caches, histories, logs, staging, and publication
    destinations. Isolate mutable jobs, lease the real resource across processes, make incidental
    workers read-only toward shared preferences, keep sensitive job data out of command lines, and
    prove same-resource denial, independent Stop, crash release, log uniqueness, and output
    collision behavior before claiming concurrency.
19. HUMAN SIDECARS AND MACHINE MARKERS HAVE DIFFERENT NAME CONTRACTS. A cue, report, or exported
    log should remain identifiable when copied away from its parent directory, using one sanitized,
    bounded subject identity. Transaction markers, receipts, discovery keys, and ownership sentinels
    keep stable machine names. When migrating, accept the legacy human names, detect new names by
    type, reject ambiguous candidates, and test repair, overwrite, cleanup, and publication consumers
    before removing any literal-name compatibility.
20. CONSISTENCY IS NOT CORRECTNESS. Repeated reads or transforms can agree because they share damaged
    input, stale cache, the same dependency defect, or the same deterministic bug. Name the property
    actually proved. Report consistent or repeatable evidence separately from pristine, accurate,
    repaired, or independently verified evidence, and do not let a success label erase known damage.
21. REPAIR RECEIPTS BIND BOTH SIDES OF THE CHANGE. Hash the selected source set before repair and
    recheck it after independent output verification. Bind the receipt to the exact finalized output
    set that was decoded and verified, then recheck source, output, and evidence immediately before
    atomic publication. Write the stable completion marker last and reject missing, ambiguous, changed,
    linked, or out-of-scope evidence instead of publishing a partial proof.
22. AN EMPTY FILE SET IS NOT A CLEAN SCAN. Every negative content check records the
    candidate-file count and finding count. Zero candidates means the check did not
    examine that implementation. Require a known-positive sentinel before trusting
    a whole-tree zero, especially when language or extension filters can exclude an
    entire native wrapper.
23. CODEC SUPPORT IS NOT REDISTRIBUTION. Prove the exact released CLI contract with
    a real encode before registering it. Bundle only when archive and executable
    hashes, license, source obligations, dependencies, patent or notification
    boundaries, notices, and final package contents are complete. Otherwise keep a
    receipt-bound user import.
24. PACKAGED TOOLS KEEP THEIR IDENTITY THROUGH LAUNCH. Pin the archive entry in one
    preparation manifest, repeat that digest in the release artifact contract and
    runtime catalog, rehash after installation, and hold the verified executable
    non-replaceable through the encoder and verifier process lifetime. A user import
    may override the package only through its own exact approval receipt.
25. DEPENDENCY LOCK POLICY MUST MATCH THE PROJECT GRAPH. Discover first-party
    PackageReference projects from source, require one committed transitive lock for
    every enrolled project, and make CI restore in locked mode. Prove the policy does
    not generate files inside immutable vendor submodules. Locks prove resolution,
    while receipts, SBOMs, notices, and hashes retain their distinct artifact roles.
26. RESTORE OUTPUT IS AN INPUT TO THE BUILD. A dependency conditioned on the restore
    engine, runtime identifier, framework, or configuration can remain serialized in
    `project.assets.json` and affect a later build where that condition evaluates false.
    Exercise every supported canonical restore-plus-build lane from a clean or
    lane-isolated graph. Compare final dependencies, generated config, resources,
    executable flags, and lock cleanliness; never infer a shipping no-op from the
    build-time condition alone.
27. HOSTED SUCCESS INCLUDES ACTION-RUNTIME HEALTH. Inspect workflow annotations, not
    only job conclusions. If the platform must force a pinned action off a deprecated
    declared runtime, move to a supported upstream release, pin its immutable commit,
    rerun the critical workflow contract, and retain a receipt bound to that source
    revision. A compatibility shim is migration evidence, not a clean long-term pass.
28. EXACT-BYTE FIXTURES NEED CHECKOUT-BYTE CONTRACTS. A text oracle that Git may
    expand from LF to CRLF cannot byte-prove an archive payload unless its exact
    `.gitattributes` policy is pinned. For framework-reference fallback packages,
    consume their assets under the build host that lacks the installed targeting
    pack and keep them restore-only under the host that must share the lock without
    consuming those assets. Exercise both hosts.

### How to choose the extraction

- Shared BEHAVIOUR across similar components (encoders, readers, page view models): a base class or an
  interface with default implementations - but only outside the per-sample path. Inside it, use
  generics so the call is resolved statically.
- Pure MATHS or format conversion repeated in several places (dB conversion, sector and frame
  arithmetic, CRC helpers, sample scaling): a dedicated internal static class, methods marked
  aggressive-inline. This is the direct analogue of the original's `dsp`/`utils` module.
- BOILERPLATE - option plumbing, channel or track mapping, repeated error handling: C# HAS NO MACROS.
  Do not pretend otherwise. The honest options, in order of preference: a shared helper method; a
  generic helper; a source generator when the boilerplate is genuinely mechanical and large. Reach for
  a source generator only when a plain helper cannot express it.
- PROCESS duplication - the same multi-step sequence performed in more than one place (render a
  name, split it, cap it, uniquify it, create directories, hand it to the engine): extract the whole
  SEQUENCE into one method both call. This is the highest-value category in this codebase, because
  every past instance of it has produced a real user-visible bug when one copy was fixed and the other
  was not.
- ASSURANCE or RELEASE duplication - hashing, finalization, publication, receipt creation, collection,
  or rollback split across multiple callers: extract the complete ordered transaction, including its
  reservation, lease, exact-set checks, commit point, invalidation, quarantine, and recovery behavior.
  A helper that centralizes only the happy-path copy or move makes the dangerous state transitions
  harder to see.

### Deliverables

1. A summary of the structural issues and duplication found, ordered by the harm each can do.
2. For each instance: the recommended strategy (base class, generic, static helper, shared sequence,
   source generator) and its justification - performance where the code is hot, and divergence risk
   where the code writes files.
3. The refactored code, showing the new shared unit and how each call site changes.
4. Anything that LOOKS duplicated but must not be unified, and why.

Begin by auditing the codebase.

## What did not translate, and why

- `macro_rules!` has no C# equivalent. Source generators are the closest, and are far heavier. Most of
  what a Rust macro would do here is served by an ordinary shared method.
- `dyn Trait` versus `impl Trait` has no exact mirror. The nearest real distinction is
  `virtual`/`interface` dispatch versus `sealed` types and generic constraints the JIT can devirtualize.
- Rust's ownership rules mean "no allocation" is checkable at compile time. In C# it is a review
  discipline, so the constraint is written as something a reviewer can actually verify by reading:
  no `new`, no LINQ, no string work, no boxing, no capturing closures in the named loops.

## Standing use

These principles apply to ongoing work in this repo, not just to a one-off audit - in particular the
PROCESS-duplication rule, which is the one that has repeatedly cost real user data here.
