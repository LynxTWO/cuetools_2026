# MusicBrainz naming + metadata buildout - design

Date: 2026-07-25
Status: approved, ready for phased implementation plans
Scope: CUETools.Processor (CUEMetadata, CUETrackMetadata, CUESheet naming + tagging, CUEConfig),
CUETools.Wpf (NamingEngine, NamingViewModel, RipService, ConvertService, DriveService release
lookup + ranking, a new MusicBrainz client), and a new metadata-carrying path from the lookup to
naming and tags. One design; built as four shippable phases, each with its own implementation plan.

## Plain English

The app has two naming engines that do not agree. The rich one (WPF NamingEngine, with the Picard-
distilled rules: featured-artist extraction, article swap, separator unify, multi-disc folders, a
release descriptor) is preview-only. Real rip and convert output goes through an older engine
(CUESheet.GenerateFilenames + General.ReplaceMultiple) whose token vocabulary is a smaller, differently
spelled set (`%album artist%` with a space, `%catalog%` wired to the barcode, no `%discsubtitle%`).
The Naming page pushes the rich template into the poor engine, so unknown tokens stay literal and the
encode writes into a folder that does not exist - the bug seen in Test & Copy's first real encode.

This buildout makes the rich engine the single naming authority for real output, expands the metadata
model to the full MusicBrainz CD shape, adds a direct MusicBrainz client as a first-class release
source, writes the richer tags TagLib already supports, and lets the user submit the disc's TOC back
to MusicBrainz. The result: accurate names and tags from the best available data, and a rip that
round-trips cleanly in Picard.

## Goals

- One naming engine and one token vocabulary for real rip AND convert output; the Naming page preview
  equals what is written to disk.
- The full MusicBrainz CD model available for naming templates and written to tags: album artist vs
  track artist, disc number/total/subtitle, release date + original year, label/catalog/barcode,
  release group, primary type + secondary types, status, media, ISRC, MBIDs, ASIN.
- A direct MusicBrainz client as a high-quality, first-class candidate in the existing ranked picker,
  degrading cleanly to CTDB/freedb/CD-Text when MusicBrainz is unreachable or has no match.
- A "Submit disc ID to MusicBrainz" action that contributes the TOC (no login).
- No regression to existing rips; every field degrades cleanly when absent.

## Non-goals

- Full MusicBrainz editing via OAuth (submitting new releases/edits through the edit API). Out of
  scope; the submit action is the disc-ID attach URL only.
- Non-CD MusicBrainz entities (vinyl, digital media) beyond what a CD release carries.
- Replacing CTDB's parity verification or freedb coverage; MusicBrainz is added alongside them.

## Architecture (the unified pipeline)

1. Disc read queries the existing sources (CTDB proxy, freedb, CD-Text) and, new, a direct
   MusicBrainz client keyed on the MusicBrainz disc ID computed from the TOC.
2. Every candidate release enters the existing ranked picker (DriveService BuildMatch / SourceRank);
   MusicBrainz candidates carry the full model and rank high, but any source can be picked.
3. The picked release fills an expanded CUEMetadata (the one shared model).
4. For rip and convert, one naming engine (NamingEngine.Render, fed by a CUEMetadata -> NamingContext
   mapper) computes the real output path; the rip/convert layer creates the directory tree and hands
   the engine explicit per-track output paths.
5. The tag pass writes the full tag set from CUEMetadata.
6. A Submit disc ID action opens MusicBrainz's attach-TOC page for the matched release.

## 1. Expanded metadata model

CUEMetadata (CUETools.Processor/CUEMetadata.cs) gains release-level fields, all additive with XML
attributes so older cached files and .cue tags still deserialize (missing -> default empty):

- `ReleaseMbid`, `ReleaseGroupMbid`, `MbDiscId`
- `AlbumArtistMbid` (the MBID for the existing album-level `Artist`; there is no need for a new
  album-artist string - `CUEMetadata.Artist` already is the album artist, and per-track artists live in
  `Tracks[i].Artist`)
- `Asin`
- `PrimaryType` (album | single | ep | broadcast | other)
- `SecondaryTypes` (list: live, compilation, soundtrack, remix, demo, dj-mix, mixtape/street, ...)
- `ReleaseStatus` (official | promo | bootleg | pseudo-release)
- `Media` (e.g. "CD")

CUETrackMetadata (CUETools.Processor/CUETrackMetadata.cs) gains `RecordingMbid` (ISRC already exists).

Fix the naming footgun: the `%catalog%` engine token is wired to `Metadata.Barcode`
(CUESheet.cs:2239). Point `%catalog%` at the catalog number (`LabelNo`) and add a distinct `%barcode%`
token for the barcode.

## 2. One naming engine, real output

NamingEngine.Render (CUETools.Wpf/Services/NamingEngine.cs) becomes the single authority for real rip
and convert output paths.

- A `CUEMetadata -> NamingContext` mapper fills the rich NamingContext (including the new type/status
  fields, so the release descriptor works from real data, not just canned examples). Today NamingContext
  is built only from RipViewModel display strings and canned Examples; the mapper is new.
- The token vocabulary unifies on the WPF spellings plus new tokens fed by the richer model:
  `%label%`, `%catalog%`, `%barcode%`, `%country%`, `%genre%`, `%releasetype%`, `%releasestatus%`,
  `%isrc%` (per track), `%originalyear%`, alongside the existing `%albumartist%`, `%artist%`, `%album%`,
  `%title%`, `%year%`, `%tracknumber%`, `%discnumber%`, `%totaldiscs%`, `%discsubtitle%`, and the
  derived `%disc%` / `%releasedescriptor%` / `%featsuffix%`. PaletteFields lists the full set.
- Output-path injection: the rip/convert layer calls Render to get each track's relative path (folder
  + filename), creates the full directory tree, then hands the engine explicit per-track output paths
  so it writes exactly there (via the engine's explicit-track-filenames hook; the plan pins the exact
  mechanism - the existing TrackFilenames path or a small new setter). This bypasses the engine's own
  trackFilenameFormat, kills the token-mismatch bug at its root, and applies identically to Convert.
- The per-rip defensive `EngineTrackFilenameFormat` strip added to RipService is removed (superseded).
- The Naming page keeps editing the NamingScheme and previewing via NamingEngine; the preview now
  equals real output because both use the same engine.

## 3. Direct MusicBrainz client

A new small service (project `CUETools.MusicBrainz` or a WPF-side service) using HttpClient against
`https://musicbrainz.org/ws/2`, JSON (`fmt=json`).

- MusicBrainz disc ID: computed from the TOC per MusicBrainz's documented algorithm (SHA-1 over first
  track, last track, and the lead-out + per-track offsets as MusicBrainz specifies, base64-encoded with
  MusicBrainz's `._-` alphabet). This is a different hash from the AccurateRip and CTDB ids and must be
  implemented exactly; it is unit-tested against MusicBrainz's published test vectors.
- Exact lookup: `GET /ws/2/discid/{discid}?inc=artist-credits+labels+recordings+release-groups+isrcs+genres&fmt=json`.
- Fuzzy fallback when the disc ID is unknown: `GET /ws/2/discid/-?toc={toc}&inc=...&fmt=json` (the
  `-` disc id with a `toc` parameter does a TOC-based fuzzy match), so near-matches still surface.
- Etiquette: anonymous reads, a 1-request-per-second throttle (a >=1100 ms minimum interval), and a
  required descriptive `User-Agent` (e.g. `CUETools2026/<ver> ( https://github.com/LynxTWO/cuetools_2026 )`).
- Integration: parsed releases become candidates in the existing DriveService ranking as a new source
  (`musicbrainz-direct`), populate the expanded CUEMetadata fields, and rank as a high-quality source.
- Also copy the release id / info url the CTDB proxy already returns but currently drops
  (CUEMetadata.FillFromCtdb, CUEMetadata.cs:268), so even a CTDB-sourced pick carries an MBID when the
  proxy provided one.

## 4. Rich tags

Extend the tag pass in CUESheet.Go (the single-file block ~2866 and the per-track block ~2952) to write
what TagLib already supports but the code never sets, sourced from the expanded CUEMetadata:

- Per-track `ISRC` (TagLib.Tag.ISRC) - captured today in CUETrackMetadata.ISRC and the .cue, never
  written to the audio file.
- MusicBrainz ids: `MusicBrainzReleaseId`, `MusicBrainzReleaseGroupId`, `MusicBrainzArtistId` /
  `MusicBrainzReleaseArtistId`, `MusicBrainzTrackId` (recording), `MusicBrainzDiscId`.
- `MusicBrainzReleaseType` (primary + secondary), `MusicBrainzReleaseStatus`,
  `MusicBrainzReleaseCountry` (already written), `AmazonId` (ASIN).
- The corrected `Publisher`/`CatalogNo`/barcode wiring.

Existing behaviour (album/artist/genre/disc/year/date/cover/AR+CTDB verification tags) is unchanged.

## 5. Submit disc ID to MusicBrainz

A "Submit disc ID to MusicBrainz" action (Drive & Read or the Rip page) builds the attach-TOC URL
`https://musicbrainz.org/cdtoc/attach?toc={toc}&tracks={n}&id={discid}` (toc = the MusicBrainz TOC
string: first track, last track, lead-out, per-track offsets) and opens it in the default browser. No
login; the user completes the attach on the MusicBrainz site. When a release MBID is known, the URL can
target that release directly.

## Build order (one design, four shippable phases; each gets its own implementation plan)

1. Naming unification. Make NamingEngine.Render drive real rip and convert output; add the
   CUEMetadata -> NamingContext mapper; unify the vocabulary; create directory trees; remove the
   defensive strip. Fixes the encode bug for good using data we already have. Release descriptor
   degrades where type/status are absent (filled in phase 3).
2. Model expansion + richer tags. Add the CUEMetadata / CUETrackMetadata fields; fix the
   `%catalog%`/barcode wiring; copy the CTDB proxy's dropped id/infourl; write ISRC + label + catalog +
   country + (where present) MBID tags.
3. Direct MusicBrainz client. Disc-ID computation + lookup + full-model parse + ranking integration +
   populate the new fields + write the MBID/type/status/ASIN tags.
4. Submit disc ID. The attach-TOC URL + button.

Rationale: the bug fix ships first and stands alone; the model is in place before MusicBrainz fills it;
submit is a thin capstone.

## Testing

- Naming engine + mapper: unit tests for the CUEMetadata -> NamingContext mapping, each new token,
  multi-disc folder paths, and the "preview equals output" invariant; fuzz the path renderer for
  filesystem-safety (no empty segments, no illegal chars, no path escapes), extending the existing
  MSTest suite.
- MusicBrainz disc ID: unit-tested against MusicBrainz's published disc-ID test vectors (a known TOC
  must produce the exact published id).
- MusicBrainz parsing: fixture JSON responses (a single-disc release, a multi-disc box set, a various-
  artists soundtrack) parsed into the model with HTTP mocked; the throttle and User-Agent asserted.
- Tags: write then read back with TagLib, asserting ISRC / MBIDs / type / status / ASIN / catalog.
- Ranking: a MusicBrainz candidate ranks as intended among CTDB/freedb/CD-Text.

## Error handling, degradation, privacy

- MusicBrainz unreachable, rate-limited, timed out, or no-match: log it and fall back to the existing
  sources; never block a rip on MusicBrainz.
- Every field degrades cleanly: an absent token is omitted from the name (NamingEngine already does
  this) and the tag is simply not written.
- The disc-ID computation must match MusicBrainz exactly; a mismatch would silently return no results,
  so the published-vector test is a hard gate.
- Privacy: the shareable diagnostic log stays ids and numbers only; MBIDs and the disc ID are ids (not
  personal) and may appear, but album/artist/track titles and paths remain scrubbed as today.

## Decisions resolved during brainstorming

- All three pieces are one design, built in four phases (owner choice).
- MusicBrainz surface: read the full model + submit the disc ID via the attach URL; no OAuth editing.
- MusicBrainz is a first-class ranked candidate alongside CTDB/freedb/CD-Text, not an enrichment layer
  or an exclusive primary.
- The model is extended in CUEMetadata (the shared engine model) because the tag pass reads it.
- The rich WPF NamingEngine is the single naming authority for real output.
