# MusicBrainz Replacement — Scope and Go/No-Go

Decision D6 / remediation R7. Written 2026-07-02 for the CUETools 2026 fork. Vocabulary: `.claude/skills/anti-dark-code/references/00-conventions.md`.

## Headline finding: there is no live MusicBrainz client to replace

The premise behind D6 ("replace the abandoned musicbrainz-sharp client with a modern MusicBrainz v2 JSON client") does not match the current code. CUETools **does not directly query MusicBrainz at all** anymore:

- The `MusicBrainz/` project (the musicbrainz-sharp mirror) is **not built**: no `.csproj` has a `ProjectReference` to `MusicBrainz\MusicBrainz.csproj`, the sln lists only `ThirdParty\MusicBrainz.dll` as a solution-items binary (not a build project), and the `MusicBrainz.dll` binary is referenced by no project's HintPath. It is dead code.
- The only first-party consumer, CUERipper, has its MusicBrainz query **commented out** (`frmCUERipper.cs:893-918`: `ReleaseQueryParameters` / `Release.Query` / `MusicBrainzService` are all commented). CUERipper's `.csproj` references only a stray `Properties\DataSources\MusicBrainz.Release.datasource` designer file, not code.
- Live metadata comes from **CTDB's server-side metadata proxy** (`cueSheet.CTDB.Metadata`, `frmCUERipper.cs:884`; `CUESheet.LookupAlbumInfo:832` adds CTDB entries at line 902) plus a **Freedb/gnudb** fallback (lines 920+, and `LookupAlbumInfo` freedb path at 907-962). db.cuetools.net aggregates MusicBrainz (and other sources) server-side and returns it through the normal CTDB lookup.
- `CDImage.MusicBrainzTOC` / `CDImage.MusicBrainzId` (`CDImage.cs:388,400`) are just disc-ID string computations from the TOC. They are needed regardless (CTDB and any MB lookup use them) and do not depend on the dead library.

So "replacing the client" would not restore a broken feature — it would **reintroduce a direct-to-MusicBrainz path that the project deliberately disabled** in favor of CTDB's proxy.

## The metadata seam and data shape (for either option)

`CUESheet.LookupAlbumInfo(useCache, useCUE, useCTDB, CTDBMetadataSearch)` returns `List<CUEMetadataEntry>`, each wrapping a `CUEMetadata` tagged with a `source` string (`local`, `cue`, `tags`, CTDB `meta.source`, `freedb`). That `source` string is the existing provider seam — a new provider would add entries with its own source tag.

`CUEMetadata` fields a provider must populate (`CUEMetadata.cs`): `Artist`, `Title`, `Year`, `ReleaseDate`, `Genre`, `DiscNumber`/`TotalDiscs`/`DiscName`, `Label`/`LabelNo`, `Country`, `Barcode`, `Comment`, `Tracks` (`List<CUETrackMetadata>`: per-track `Artist`, `Title`, `ISRC`, `Comment`), and `AlbumArt` (`List<CTDB.CTDBResponseMetaImage>`). Track count must match the disc TOC.

## If a direct MusicBrainz v2 provider is wanted (reference)

- **Disc lookup:** `GET https://musicbrainz.org/ws/2/discid/{discid}?inc=recordings+artist-credits+labels+release-groups&fmt=json`. The `{discid}` is MusicBrainz's SHA-1 disc ID, already computed by `CDImage.MusicBrainzId`. Fuzzy TOC fallback: `.../ws/2/discid/-?toc={toc}&fmt=json`.
- **Cover art:** `GET https://coverartarchive.org/release/{mbid}/front` (302s to the image). Map into `CTDBResponseMetaImage`.
- **Rate limit:** ~1 request/second, throttled per client. Must send a descriptive `User-Agent` (e.g. `CUETools/2.2.x ( contact-url )`); MusicBrainz blocks generic/absent UAs.
- **JSON:** use `System.Text.Json` (already in the modern-.NET direction) or Newtonsoft (already a dependency of Codecs). No XML.
- **Placement:** a new managed provider (e.g. `CUETools.Metadata.MusicBrainz`, netstandard2.0) that returns `CUEMetadata` entries with `source = "musicbrainz"`, invoked from `LookupAlbumInfo` behind a config flag. The dead `MusicBrainz/` project would still be deleted (see D5); a modern client shares nothing with the musicbrainz-sharp XML code.

## Go / No-Go — STOPPED for your decision

I am **not** implementing this autonomously, because the decision is now a product/architecture choice, not a mechanical replacement, and it fails the "is this the right thing to build" gate even though I am confident I could build it:

- **Option A — Do nothing to the client; delete the dead library.** CTDB's proxy already delivers MusicBrainz metadata; this is the modern path the project chose. Retire the `MusicBrainz/` project + `MusicBrainz.dll` under D5. Lowest risk. Recommended unless you specifically want a direct path.
- **Option B — Add a new direct MusicBrainz v2 provider** behind the metadata seam as a fallback/alternative to CTDB (useful if CTDB is down, or for richer/fresher data). This is a **new feature**, not a replacement, and reintroduces a direct external dependency the project moved away from. I can build and verify it against the live MB API on request.
- **Option C — Leave everything as-is**, including the dead code, for now.

**Recommendation:** Option A (rely on CTDB's proxy, delete the dead library), unless you want direct MusicBrainz for resilience/richness, in which case Option B and I'll implement + verify it. Either way, the dead `MusicBrainz/` library should be removed (folds into D5).

**What I need from you:** which option. That's the genuine decision this scope surfaces.
