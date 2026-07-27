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
7. RELEASE RECEIPTS ARE GENERATED EVIDENCE. One orchestrator must prepare dependencies, rebuild,
   create the receipt, collect, and validate under one lease. The receipt must record commands that
   actually ran, resolved tools, exit results, logs, the complete planned source set, every consumed
   native or generated input including ignored files, and hashes of the exact collected bytes. Do not
   accept caller-authored claims or a fresh staging directory as proof of fresh inputs. Scope the
   lease to every shared output across versions and helper entrypoints. Recover or refuse a retained
   intent before cleanup. Validate archive expansion against the pinned archive instead of a partial
   tracked-tree sentinel, hash ignored expanded sources explicitly, and evaluate warning baselines
   from the exact rebuild logs that produced the collected bytes.
8. HARDWARE CALIBRATION IS PART OF THE PROOF. A newly required drive capability is a versioned
   prerequisite for the first Rip, Verify, or Test & Copy that depends on it, not a manual
   optimization. Preserve the conservative high-water for cache sizes: observing cache is positive
   evidence, while a later noisy timing run that misses it cannot un-prove it. Cache eviction must
   complete or fail explicitly. Probe the exact command and one-sector edge geometry that the reader
   consumes, then prove the runtime uses the capability instead of continuing to zero-pad.
9. METADATA AND PROOF TAGS HAVE DIFFERENT PRESERVATION RULES. A repair retains source filenames,
   human metadata, custom fields, artwork, and disc identity. AccurateRip/CTDB confidence and CRC
   tags describe the old payload; after samples change they must be independently recomputed or
   deliberately removed, never copied as if they still proved the repaired bytes.
10. MULTI-DRIVE EVIDENCE MUST NAME ITS DEVICE. A page must not retain drive H:'s identity or
    calibration while Rip operates on K:. Make selection explicit and synchronized, clear stale
    device evidence immediately on selection changes, and lock selectors while a hardware-owning
    operation is active. UI/event listeners are ancillary: marshal them to the UI thread and contain
    their failures so they cannot abort calibration, ripping, or ownership-scope cleanup.
11. PUBLISH PHASE EVIDENCE WHEN THE PHASE COMPLETES. A completed Test CRC must not remain hidden
    throughout Copy or an optional tie-break. Send immutable, semantically named snapshots at each
    completed phase, preserve older roles that the phase did not replace, and keep the final composite
    result distinct from those already-proven intermediate facts.
12. BAD INPUT IS NOT A DEAD DEVICE. At hardware, parser, archive, and protocol boundaries, classify
    subject-data failures separately from transport, readiness, removal, unit-attention, command, and
    hardware failures. Only the subject-data class may become explicitly untrusted evidence for an
    existing retry, quarantine, or repair policy. Test the classifier without the scarce dependency,
    then reproduce beyond the original failure point on the real boundary.
13. CONTROL SUCCESS IS NOT PAYLOAD READINESS. A successful seek, speed change, mode change, flush, or
    reset proves only that command completed. Serialize the transition with the dependent payload,
    honor a measured bounded settle when required, and retry only the exact observed transient while
    the transition is pending. Repeated or unrelated failures remain fatal. Log enough scrubbed
    command shape and transition state to locate intermittent failures without logging payload data.
14. BATCH FAILURE IS NOT ITEM FAILURE. A rejected batch, archive, page, or bulk transfer does not
    prove its members are corrupt. Decompose only where items have independent, trustworthy read
    semantics, and continue only from successful item results. Do not recast command-shape,
    transport, or container failures as damaged-subject evidence. Any required item that still fails
    keeps its exact identity and fatal failure class. Bound and count compatibility fallbacks.

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
