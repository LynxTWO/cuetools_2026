# Album Art Discovery and Selection Plan

Status: core implementation, normal dark/light browser capture, and
embedded-output proof are complete. Encoded jobs now wait for a stable artwork
snapshot. High-contrast, 150/200 percent DPI, and the fast start-before-discovery
live repeat remain.
Prepared 2026-07-28 for CUETools 2026.

## 1. Outcome

Replace the current single-result Apple lookup and separate CTDB fallback with one
artwork discovery pipeline. The pipeline will:

- retain the exact selected release identity;
- collect candidates from the metadata response, Cover Art Archive, and licensed
  optional providers;
- rank release correctness before image quality;
- show every usable result in an owner-bound artwork window;
- let the user sort, inspect, and override the automatic choice;
- download and freeze the selected master before Rip or Test & Copy begins; and
- pass the same selected bytes into the existing album publication transaction.

The implemented provider set is CTDB metadata art, Cover Art Archive, and an
off-by-default TheAudioDB fallback using a protected user-supplied API key.
Apple embedding remains disabled unless a separate license permits the use.

Implementation checkpoint, 2026-07-28:

- CTDB provider identity now survives through release selection.
- Exact Cover Art Archive release lookup, MusicBrainz disc-ID and fuzzy-TOC
  fallback, and labeled release-group fallback are implemented.
- Apple Search API artwork is removed from the runtime path.
- The configured proxy, process-wide MusicBrainz throttle, bounded manifest and
  network image reads, redirect host policy, public-host checks, JPEG/PNG network
  header and pixel limits, cancellation generations, and a bounded in-memory
  manifest cache are implemented.
- The resizable artwork browser exposes source, match, dimensions, file size,
  type, approval, match reason, and provider page links with Front/All filtering,
  sortable columns, and explicit automatic, selected, local, and no-cover actions.
- The Rip preview and selector accept one dropped or selected regular JPEG, PNG,
  or BMP. The importer reads it once under 30 MiB and 100-megapixel limits,
  converts PNG/BMP to quality-92 JPEG, and applies the established gamma-space
  Mitchell-Netravali resize when the configured side limit is exceeded.
- TheAudioDB accepts an API key rather than account credentials. It is
  purpose-separated under current-user DPAPI, never logged, disabled by default,
  source-labeled, host-bound, rate-gated, and limited to release-group or validated
  exact artist/album results. Non-front images remain browser-only.
- The processor's hidden artwork fallback is disabled for WPF jobs. Rip and Test
  & Copy capture the selected byte array before work starts.
- Rip and Test & Copy remain disabled while release-bound artwork is loading,
  and their private execution guards enforce the same rule. Verify remains
  available because it publishes no audio.
- The full WPF suite passes 395/395. The WPF and fuzz warning gate emits zero
  warnings, the self-contained x64 artifact contract passes, and live Cover Art
  Archive, MusicBrainz, and TheAudioDB contract probes return HTTP 200.
- The planned disk cache is not part of this checkpoint. The in-memory cache
  avoids repeat calls during a session without creating a persistent privacy or
  corruption boundary.

Live checkpoint, 2026-07-29:

- The first dark-theme browser capture exposed a default-white DataGrid body and
  unreadable row text. Body, cell, selection, grid-line, and text colors now
  resolve through the central dynamic palette.
- Post-fix dark and light 1040x700 captures at the host's actual 96 DPI show the
  complete candidate row and reachable selection controls.
- A real Paranoid image rip embedded exactly one selected cover whose 100,222
  bytes equal the published `folder.jpg`.
- Windows high contrast and 150/200 percent DPI browser captures remain open.
- Closing software gates pass: WPF 417/417, zero WPF/fuzz warning fingerprints,
  and the self-contained x64 artifact contract with five native-plugin probes.

## 2. Current path and defects

### Current path

`RipViewModel` calls `IAlbumArtService.FindHiRes`. `AlbumArtService` tries one
Apple UPC lookup, then a five-result text search, selects the first result, rewrites
the documented 100-pixel URL to an undocumented 3000-pixel form, and downloads it.
The service catches provider, parsing, and decode failures as one null result.

`RipService` replaces `CUEMetadata.AlbumArt` when explicit bytes are available.
When those bytes are null, the processor may fetch CTDB cover art through
`CUESheet.LoadAndResizeAlbumArt`. That second path has different selection,
network, error, and resize behavior.

The selected CTDB metadata entry contains `source`, `id`, and `infourl`, but
`CUEMetadata.FillFromCtdb`, `CUEMetadataEntry`, and `ReleaseMatch` discard the raw
provider identifiers before the WPF art lookup starts. This prevents an exact
Cover Art Archive release lookup even when the metadata response supplied a
MusicBrainz release ID.

### Defects to remove

- The first Apple text result can be a different edition.
- A higher-resolution image can silently replace art tied to the selected release.
- Multiple CTDB images can collapse to no image because the WPF rip path does not
  supply the legacy selection callback.
- The Apple service bypasses the configured proxy.
- Network input size, decoded pixel count, redirect hosts, and cache use are not
  bounded as one reviewed contract.
- The UI cannot explain the match, provider, dimensions, encoded size, or failure.
- A release change can race an earlier async art lookup.
- The exact artwork bytes used by an in-flight job are not represented as an
  immutable selection snapshot.
- Apple documents Search API album art as promotional content tied to store
  promotion. The current download-and-embed use is not covered by that documented
  permission.

## 3. Product rules

1. Correct release and edition outrank resolution and encoded size.
2. An explicit user choice outranks the automatic rank for that selected release.
3. A release-group image is a labeled canonical fallback, never an exact-edition
   result.
4. The automatic result must remain explainable. The UI will show the identity
   evidence and the quality signals that placed it first.
5. Discovery failure must not block a rip. If no safe candidate is available,
   CUETools proceeds without downloaded art and says why.
6. A job uses the immutable selection captured when the user starts it. Later UI
   changes affect only later jobs.
7. Provider terms, credentials, or outages must not be hidden by a generic
   "not found" message.

## 4. Shared data contracts

Add the contracts under `CUETools.Wpf/Services/Artwork/` so provider DTOs do not
leak into `RipViewModel`.

### `ArtworkQuery`

- artist, album, year, barcode, country, label, catalog number, disc number;
- track count, MusicBrainz disc ID, and normalized CD TOC;
- CTDB metadata source, source-specific release ID, and information URL;
- MusicBrainz release ID and release-group ID when known; and
- a monotonically increasing release-selection generation.

The selected metadata path must carry the raw source ID from
`CTDBResponseMeta` through `CUEMetadataEntry` and `ReleaseMatch`. A UUID is treated
as a MusicBrainz ID only when the source contract identifies it as one. The raw
value is never guessed from display text or an information URL.

### `ArtworkCandidate`

- stable candidate ID and provider-specific item ID;
- provider name and source attribution text;
- exact release, exact barcode, release group, strong text, or weak text match;
- the facts that support that match;
- front, back, other, approved, primary, watermark, and provider order flags;
- thumbnail and original references;
- MIME type, pixel width, pixel height, and nullable encoded byte length;
- original or thumbnail status;
- provider availability/error state; and
- deterministic rank key.

Provider DTOs are parsed into this model at the provider boundary. Unknown
dimensions and byte lengths stay null. They are not represented as zero.

### `ArtworkSelectionSnapshot`

- selected candidate identity and provider attribution;
- downloaded master SHA-256, MIME type, dimensions, and encoded byte length;
- resized output SHA-256, JPEG bytes, target side, and quality; and
- release-selection generation.

The snapshot is created before Rip, Verify, or Test & Copy starts. Publication
uses only its resized byte array. A stale generation cannot replace the visible
selection or enter a new job.

### Service boundaries

- `IArtworkProvider.SearchAsync(ArtworkQuery, CancellationToken)` returns candidate
  metadata, not unbounded image bytes.
- `ArtworkDiscoveryService` schedules providers, merges results, ranks them, and
  reports provider-specific status.
- `ArtworkImageLoader` applies redirect, byte, MIME, pixel, cache, and proxy
  policy to thumbnails and selected masters.
- `ArtworkSelectionService` downloads, validates, resizes, hashes, and freezes
  the selected candidate.
- `ArtworkCache` stores bounded manifests and thumbnails. It is not the authority
  for the selected job bytes.

## 5. Provider policy

### CTDB metadata art

Treat art already returned with the selected CTDB metadata result as candidates
without another metadata request. Preserve `primary`, dimensions, source identity,
and original URI. Do not invoke `CUESheet.LoadAndResizeAlbumArt` from the WPF rip
path after migration.

Because CTDB transport is still plain HTTP, CTDB art is not granted exact-release
authority merely because it arrived with metadata. It can rank as metadata-bound
only when its source identity agrees with the selected release. Image bytes still
pass the shared validation and hash path.

### Cover Art Archive

Cover Art Archive is the default network provider.

1. Query `/release/{mbid}` when the selected metadata carries a MusicBrainz
   release ID.
2. If the release ID is unavailable, use the existing MusicBrainz disc ID and TOC
   for one cached MusicBrainz lookup. Select no release silently when the response
   is ambiguous.
3. Query `/release-group/{mbid}` only as a separately labeled canonical fallback.
4. Include front candidates by default. Show other types in the browser behind an
   "All artwork" filter, but never auto-select back, medium, booklet, or other art.
5. Prefer approved front art and provider order after release identity has matched.
6. Use 250 or 500 pixel thumbnails for the browser. Download the original only
   for the selected candidate.

Any direct MusicBrainz request uses a meaningful CUETools version/contact
User-Agent, a process-wide one-request-per-second scheduler, shared caching, finite
timeouts, and cancellation. Cover Art Archive 503 responses are retryable with
bounded backoff even though its current documentation states no fixed rate limit.

### Apple

Do not ship Apple as an embedding provider under the public Search API terms.
Those terms describe album art as promotional content associated with iTunes
store promotion. They do not document downloading that art for independent
embedding into a music library.

The existing Apple provider will be disabled and then removed from the normal art
path. It can return only if CUETools obtains a license that explicitly covers this
use. If restored, it must:

- use documented UPC and search results without relying on an undocumented
  high-resolution URL rewrite;
- expose multiple candidates rather than selecting result zero;
- validate artist, album, year, track count, country, and UPC where returned;
- honor the approximate 20-call-per-minute Search API limit and cache guidance;
- show the required Apple attribution and store link; and
- use the same image validation, proxy, cache, and selection contracts.

### TheAudioDB

TheAudioDB is implemented behind an off-by-default provider setting.

- Prefer release-group lookup by MusicBrainz ID, then artist/album search.
- Treat its images as release-group or text matches unless an exact release
  identifier is proven.
- Label the source on every card and in the selected-image details.
- Accept a user-supplied V1 or premium key, never an account password. Never log
  the key or keyed URL. Hash manifest cache identities so the path key is not
  retained as a cache key.
- Protect a saved key with purpose-separated current-user DPAPI.
- Handle HTTP 429 with bounded retry and a provider status message.

The provider remains off by default. The public terms permit API artwork use but
distinguish app-store publication and require source attribution for paid API use.
The exact CUETools distribution arrangement must be confirmed before the default
changes.

## 6. Automatic ranking

Use a lexicographic rank rather than one blended score. A very large image must
never overcome evidence that it belongs to the wrong edition.

Rank fields, in order:

1. user-selected for the current release;
2. identity tier:
   - exact MusicBrainz release and matching disc or TOC;
   - metadata-bound exact release;
   - exact normalized barcode plus matching artist/album;
   - exact release group, labeled canonical;
   - strong artist/album/year/track-count text match;
   - weak text match, browser-only and never automatic;
3. front image;
4. approved or provider-primary image;
5. no watermark flag;
6. provider confidence within the same identity tier;
7. pixel area;
8. distance from a square aspect ratio;
9. encoded size when known; and
10. stable provider and item IDs as the deterministic tie-break.

Exact byte duplicates are merged after download using SHA-256 and list every
source that supplied them. Manifest duplicates are merged early by normalized,
provider-approved identifiers. A perceptual hash may group visually equivalent
variants for display, but it must not erase distinct editions or make a selection
decision in version 1.

The default provider confidence within an equal identity tier is:

1. Cover Art Archive approved front;
2. selected CTDB metadata primary art;
3. Cover Art Archive unapproved front;
4. licensed Apple exact result, if ever enabled;
5. TheAudioDB MusicBrainz release-group result; and
6. text search results.

This ordering is a tie-break only. Identity evidence remains the first authority.

## 7. Artwork browser window

Replace the passive preview border with a keyboard-focusable button whose
accessible name is "Choose album artwork". Clicking it opens an owner-bound,
resizable `ArtworkBrowserWindow`. Discovery continues without blocking the Rip
page.

### Window structure

- Header: selected release, current automatic choice, refresh, and provider
  progress.
- Toolbar: filter by Front or All artwork; sort by Recommended, Dimensions, File
  size, Source, or Match; ascending/descending toggle.
- Virtualized result list: thumbnail, source badge, exact/canonical/text label,
  dimensions, encoded size or "unknown", format, front/approved state, and a short
  match explanation.
- Details pane: larger preview, full source attribution, provider page link,
  exact rank facts, original dimensions and size, and any warning.
- Footer actions: `Use selected`, `Use automatic`, `No cover`, and `Cancel`.

Sorting by dimensions uses pixel area, then longest side. File-size sorting places
unknown values after known values in both directions. Source sorting is
case-insensitive and stable. Recommended is the automatic lexicographic rank.

### Interaction and layout

- Double-click and Enter use the selected candidate.
- Escape closes without changing the previous selection.
- Focus returns to the cover button.
- The window has a 900 by 600 preferred size, a smaller usable minimum, independent
  horizontal and vertical reachability, and no fixed content width that can clip
  actions.
- Cards and details use dynamic theme resources. Dark, light, high-contrast, 100,
  150, and 200 percent scaling are release capture cases.
- Every image has a text equivalent. Color is not the only indication of source,
  approval, selection, or warning.
- If the selected release changes while the window is open, old work is canceled
  and the window clearly reloads for the new generation.

The Rip page preview shows the chosen source, match class, dimensions, and final
embedded byte size. While discovery is incomplete it remains clickable and shows
provider progress. An artwork failure does not disable Rip.

## 8. Network, image, cache, and privacy boundaries

- Use the app proxy configuration and credential handler for all providers.
- Permit HTTPS requests and redirects only through provider-specific host
  policies. Cover Art Archive redirects require a reviewed Internet Archive host
  policy rather than a general arbitrary-host allowance.
- Limit redirects, JSON bytes, thumbnail bytes, master bytes, decoded pixel count,
  width, height, and provider result count before allocation.
- Version 1 accepts validated JPEG and PNG inputs only. GIF, PDF, HTML, SVG, and
  animated inputs are not decoded for embedding.
- Validate content magic and decoded shape instead of trusting URL extensions or
  `Content-Type`.
- Use finite connect, header, body, and total timeouts. Cancellation is observed
  during streaming reads.
- Store provider manifests and thumbnails under
  `%LocalAppData%\CUETools2026\art-cache`, with a quota, expiry, atomic index
  publication, corruption recovery, and least-recently-used eviction.
- Cache keys are hashes of normalized structured identities. Filenames and logs do
  not contain artist, album, barcode, full query URLs, or API keys.
- Do not cache every original. Cache the selected master only when needed for
  re-resizing, subject to the same quota and expiry.
- Diagnostics may record provider, elapsed-time bucket, status-code class, result
  count, cache hit, selected match tier, dimensions, and byte count. They must not
  record music identity, provider credentials, full URLs, image bytes, or response
  bodies.

Default hard limits must be selected and tested before implementation is marked
complete. Initial review values are 2 MiB JSON, 5 MiB thumbnail, 30 MiB master,
100 megapixels, five redirects, ten candidates per provider, and a 250 MiB cache.

## 9. Failure behavior

Provider states remain separate:

- not searched;
- searching;
- no match;
- unavailable or timed out;
- rate limited;
- blocked by provider configuration or terms;
- invalid response or unsafe image; and
- candidates available.

One provider failure does not erase candidates from another provider. If the
automatic candidate cannot be downloaded, the selector attempts the next eligible
ranked candidate and records the rejected reason without logging private identity.
An explicit user selection that fails is not silently replaced; the UI asks the
user to choose again or return to automatic selection.

No downloaded artwork means no explicit replacement of `CUEMetadata.AlbumArt`.
After the WPF migration is complete, the processor's hidden CTDB fetch is disabled
for this path so the UI and the output cannot disagree about which cover was used.

## 10. Implementation slices

Each slice is one reviewable change with its own tests. No slice may claim the
feature is release-ready by itself.

### Slice A: identity and contract fixtures

- Preserve CTDB `source`, `id`, `infourl`, and art metadata through
  `CUEMetadataEntry` and `ReleaseMatch`.
- Capture bounded fixtures for CTDB metadata, Cover Art Archive release and
  release-group manifests, MusicBrainz disc lookup, provider errors, and unsafe
  image responses.
- Prove the meaning of CTDB source IDs by source. Unknown IDs remain opaque.
- Add pure candidate, rank, and selection contracts.

### Slice B: shared pipeline and CTDB migration

- Add discovery, loader, selection, cache, and provider-status services.
- Move CTDB metadata art into the candidate model.
- Make the loader proxy-aware and bounded.
- Keep the current visible result behavior until the shared path passes.

### Slice C: Cover Art Archive

- Add exact release lookup.
- Add the process-wide MusicBrainz disc-ID scheduler only when an exact release ID
  was not preserved.
- Add release-group fallback with explicit canonical labeling.
- Add live opt-in contract tests separate from deterministic CI.

### Slice D: selector UI

- Add the window, view model, thumbnail virtualization, filters, sorting, details,
  accessible interactions, cancellation generations, and immutable job snapshot.
- Replace the passive Rip preview with the chooser button.
- Remove the legacy hidden WPF CTDB selection path.

### Slice E: provider compliance

- Apple embedding and the undocumented high-resolution URL rewrite are disabled.
- Provider source and attribution links are present in the selector.
- TheAudioDB is available only as an off-by-default provider with a user-supplied
  API key. It does not accept or retain TheAudioDB account credentials.
- TheAudioDB API keys use purpose-separated current-user DPAPI storage. A missing,
  invalid, or unreadable key disables the provider.

### Slice F: release proof

- Run deterministic suites and package gates.
- Run opt-in live Cover Art Archive, MusicBrainz, proxy, outage, and rate-limit
  checks.
- Capture responsive dark/light/high-contrast UI evidence.
- Complete real Rip and Test & Copy runs using an automatic cover and a manual
  override, then independently inspect embedded images and output metadata.

## 11. Verification matrix

### Pure and provider tests

- every identity-tier ordering and quality tie-break;
- barcode normalization, multiple editions, various artists, multi-disc releases,
  missing year, and Unicode normalization;
- stable sort directions and null size/dimension placement;
- manifest and SHA-256 duplicate merging;
- provider parser fixtures, unknown fields, malformed JSON, oversized JSON, 404,
  429, 503, timeout, and cancellation;
- MusicBrainz one-request-per-second scheduling and User-Agent;
- redirect loops, forbidden redirect hosts, MIME mismatch, truncated images,
  decompression bombs, and pixel limits;
- cache hit, expiry, quota, concurrent readers, corrupt index, and atomic recovery;
- secret persistence, proxy authentication, and diagnostic redaction.

### View model and UI tests

- release changes cancel stale results;
- user selection, automatic reset, no-cover choice, and failed explicit download;
- Rip, Test & Copy, and Verify capture a stable selection;
- all filters and sorts preserve the selected item when it remains visible;
- keyboard, focus return, automation names, and non-color state;
- narrow and short windows, theme switching, high contrast, and DPI scaling;
- thumbnail virtualization does not download every original.

### Output and regression tests

- explicit selection replaces processor art exactly once;
- no selection cannot trigger a hidden processor download;
- selected JPEG dimensions, byte count, and SHA-256 agree with embedded track tags;
- track and disc-image modes use the same selected bytes;
- Test & Copy's final output uses the Copy job's frozen selection;
- CTDB repair preserves the original selected art and tags;
- conversion and classic applications retain their current independent art paths;
- the WPF suite, warning gate, artifact contract, full publish, and classic release
  matrix remain green.

Live provider tests are opt-in and cannot be the only evidence for parsing or
ranking. Committed fixtures are bounded, attribution-safe, and contain no API keys
or user music-library paths.

## 12. Release gates and rollback

Release requires:

- no Apple Search API artwork embedded by the production default;
- Cover Art Archive exact-release and canonical-fallback labels proven;
- deterministic ranking tests covering all tiers;
- all network and image bounds tested;
- no secrets or music identity in diagnostic logs or cache names;
- selector accessibility and responsive capture matrix accepted;
- embedded-byte inspection after real Rip and Test & Copy;
- provider outage leaves ripping available; and
- package and warning baselines unchanged or intentionally updated.

Each provider has a persisted enable flag plus a build-time emergency kill switch.
The shared selector can be disabled as one unit while leaving ripping without
downloaded art. Rollback never restores silent Apple embedding or the hidden
two-path selection behavior.

## 13. Observability

Add structured, privacy-safe events for:

- discovery start/end and release generation;
- provider start/end, result count, cache outcome, status class, and duration;
- candidate rejection reason;
- automatic or user selection, match tier, dimensions, and bytes;
- selected-master validation and resize; and
- stale-generation cancellation.

These events stay in the existing local diagnostic log. There is no upload path.

## 14. External decisions and unknowns

No ranking or UI decision is required from the owner. The rules above are the
recommended defaults.

Two external policy facts must be resolved before optional providers can ship
enabled by default:

1. Apple must grant or document a license that covers downloading Search API
   artwork and embedding it in local audio files. Without that grant, Apple is
   removed from the production artwork path.
2. TheAudioDB needs confirmation of the applicable distribution tier and accepted
   attribution placement before CUETools could distribute a shared production key
   or enable it by default. The implemented provider instead requires each user to
   supply an API key and remains disabled until they do.

The core CTDB plus Cover Art Archive path does not wait on either optional
provider. Cover Art Archive itself states that images remain copyrighted and are
used at the user's risk; CUETools must retain source attribution and avoid claiming
ownership or a license over the images.

## 15. Contract references checked 2026-07-28

- Apple iTunes Search API overview and terms:
  https://developer.apple.com/library/archive/documentation/AudioVideo/Conceptual/iTuneSearchAPI/
- Apple lookup examples and documented UPC lookup:
  https://developer.apple.com/library/archive/documentation/AudioVideo/Conceptual/iTuneSearchAPI/LookupExamples.html
- Apple search construction and approximate request limit:
  https://developer.apple.com/library/archive/documentation/AudioVideo/Conceptual/iTuneSearchAPI/Searching.html
- MusicBrainz API, disc-ID lookup, identification, and request rate:
  https://musicbrainz.org/doc/Development/XML_Web_Service/Version_2
- Cover Art Archive API:
  https://musicbrainz.org/doc/Cover_Art_Archive/API
- MusicBrainz release and cover-art correctness guidance:
  https://musicbrainz.org/doc/Cover_Art
- Cover Art Archive policy:
  https://musicbrainz.org/doc/Cover_Art_Archive
- TheAudioDB API, authentication, endpoints, and rate limits:
  https://www.theaudiodb.com/free_music_api
- TheAudioDB terms:
  https://www.theaudiodb.com/docs_terms_of_use.php
