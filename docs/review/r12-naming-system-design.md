# CUETools 2026 naming system - design

Status: approved 2026-07-23 (three core decisions confirmed by the owner). This is the record for
the dedicated naming editor that replaces the single flat "track filename template" text box.

## Plain-English goal

Give the app a real filename-and-folder scheme system: a template plus a set of clean-up rules,
a preset picker, and a LIVE PREVIEW that updates from canned examples AND from the actual disc in
the tray. The out-of-box default reproduces the owner's MusicBrainz Picard scheme, adapted to
CD ripping (the vinyl/cassette/hi-res/multichannel branches of the Picard script are dropped -
a CD rip is 16-bit/44.1 stereo, so those never fire).

## The three approved decisions

1. **Distilled rules + templates**, not a Picard scripting interpreter. CUETools already has a
   `%variable%` template engine (`General.ReplaceMultiple`) with a CD-appropriate variable set.
   We reuse it and add the derived pieces the Picard script computes, as toggle-able rules. This
   is ~95% of the Picard output with none of the interpreter fragility.
2. **The owner's archival scheme is the default**, offered alongside a few simpler presets.
3. **A dedicated "Naming" page** under Setup in the nav (next to Settings).

## Architecture

Two units, each independently testable:

### NamingEngine (portable, UI-free)

Input: a `NamingContext` (the metadata fields for one track - album artist, album, track artist,
title, year, disc number, total discs, disc subtitle, track number, total tracks, primary and
secondary release types) and a `NamingScheme` (the template string + the rule flags). Output: a
relative path (`/` = folder separator), each segment cleansed of illegal characters.

Derived template variables the engine computes from the rules (this is where the Picard intent
lives):

- `%albumartist%` / `%artist%` / `%album%` / `%title%` - normalized copies: featuring-credit
  stripped from the album artist, separators unified, leading article handled, illegal chars
  removed, whitespace collapsed, trailing dots/spaces trimmed, length-capped.
- `%featsuffix%` - the guest-artist extraction: when the track artist carries featured performers
  the album artist does not, append ` (feat. X & Y)`. The crown jewel of the owner's script.
- `%releasedescriptor%` - the bracketed suffix built from the release: ` (year)`, `[Live]`,
  `[OST]`, `[EP]`, `[Single]`, `[Compilation]`, `[Remix]`, `[Demo]`, `[N-CD Set]`, `[Promo]`,
  `[Bootleg]`. CD-only: the vinyl/cassette/hi-res/channel branches are omitted by design.
- `%disc%` - expands to `Disc N/` (with optional ` - Subtitle`) only when total discs > 1.
- `%tracknumber%` - zero-padded to 2.

### Rules (the toggles), each reproducing one Picard behavior

- Extract featured artists -> `(feat. X)`
- Unify collaboration separators (` X `, ` vs `, ` meets `, ` + `, ` with `, `;`, `|`, ...) -> ` & `
- Handle leading articles (The, A, An, Die, Der, Das, Le, La, Les, El, Los, Las, Il, ...)
- Strip illegal filename characters (`" * ? < > |`), `:` -> ` - `
- Release descriptor suffix (the bracket/paren block above)

Collapse-whitespace and trim-trailing-dots are always on (they are never wrong for a filename).

### NamingViewModel + NamingView (the editor page)

- Template text box + a field palette (click to insert a `%variable%`).
- The rule toggles.
- A preset picker: Simple, Artist - Album (year), Archival (default = the owner's scheme).
- A LIVE PREVIEW panel: 3-4 canned example albums (single artist, various-artists compilation,
  multi-disc set, a track with a guest) rendered through the engine, PLUS - when a disc is loaded
  on the Rip page - the real tracks of that disc rendered through the same engine.

## Persistence and wiring

- The template + rule flags persist in the app settings (a compact serialized `NamingScheme`).
- On apply, the chosen template is written to the engine's `trackFilenameFormat` so the real rip
  and convert output use it; the derived variables are resolved by the engine before hand-off, or
  registered as additional substitution variables where the engine substitutes.

## Metadata-source note

The variables are populated from whichever source filled the release: MusicBrainz/CTDB (richest -
release types, disc subtitles, label, country), freedb (title/artist/year only), or CD-Text (disc
and track titles only - see the CD-Text item). A rule referencing a field the source did not
provide degrades cleanly (the descriptor simply omits that bracket).

## Out of scope (by the distilled-rules decision)

- No `$if/$set/$rreplace/$foreach` interpreter.
- No vinyl/cassette/hi-res/multichannel descriptors (not CD).
- A future advanced "custom script" escape hatch is possible but not built now.
