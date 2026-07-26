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
