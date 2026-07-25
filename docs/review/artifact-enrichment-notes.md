# Rip artifact enrichment notes (observed 2026-07-25)

Source: a real Test & Copy run of Genesis - ...Calling All Stations... (11 tracks, ASUS BW-16D1HT,
offset 6). Files inspected: album.cue, album.accurip, album.log, rip.verify, Test & Copy.log.
Status: observations + the phase that should act on each. No code change here.

## What the artifacts already carry (verified, keep)

- album.accurip is the richest artifact: per-track AccurateRip CRC + V2 with confidence, the full
  offset sweep (-670, -658, -6, +6), CTDB per-track status, peak level, and BOTH CRC32 variants
  (plain and `[W/O NULL]`). Also carries the AccurateRip ID and the CTDB TOCID.
- album.cue carries per-track ISRC (all 11 present), CATALOG (the UPC/barcode - correct per the CUE
  spec, which defines CATALOG as the media catalog/UPC), PERFORMER/TITLE, DATE, DISCNUMBER/TOTALDISCS,
  and the AccurateRip id as a REM.
- album.log (the EAC-style rip log) carries the TOC table, per-track Copy CRC, peak, quality, and the
  AccurateRip confidence per track.
- rip.verify carries the per-track ArV1/ArV2/Crc32 triple plus AR/CTDB confidence+total, drive,
  offset, quality, deep-recovery flag, title/artist, UTC, ripper version.
- Test & Copy.log carries the per-read CRC32 per track, which reads agreed, the source read, the
  overall verdict, and AR/CTDB status.

## Gaps worth filling, by phase

Phase 2 (model expansion + richer tags):
- rip.verify has no per-track ISRC even though album.cue already has all of them. Add ISRC to the
  VerifyRecord track entries so the sidecar is self-contained proof.
- rip.verify has no AccurateRip ID and no CTDB TOCID as distinct fields. `DiscId` currently holds the
  CTDB TOCID; add the AccurateRip ID (album.accurip shows `001a13f7-00e4af3a-af0fef0b`) as its own
  field so a record can be cross-checked against either database.
- album.cue lacks REM GENRE, REM LABEL / catalog number (the label's catno, distinct from CATALOG =
  barcode), and the full release date (only DATE year is written). All three already exist in
  CUEMetadata (Genre, Label, LabelNo, ReleaseDate) and are simply not emitted.
- Consider recording the `[W/O NULL]` CRC32 in rip.verify: album.accurip already computes it, and it
  is the value other tools compare against when null samples are excluded.

Phase 3 (MusicBrainz client):
- No artifact carries a MusicBrainz release id. Once the client lands, write the release MBID (and
  release-group MBID) into rip.verify and as a REM in album.cue, so a rip can be linked back to the
  exact release it was named from.
- Verify whether the CTDB TOCID equals the MusicBrainz disc ID. The observed TOCID
  (`sJybyy5TDbTIH.TlpV7fxiMKWm0-`) is already in MusicBrainz disc-id shape (28 chars, base64 with the
  `._-` alphabet). If they are the same value, the client can reuse it instead of recomputing, and the
  disc-id unit test should assert that equality on this disc. Measure before assuming.

## Naming patterns observed in one folder (historical, not a current bug)

The same folder contained two patterns, from rips taken at different times:

- `01 - Calling All Stations.mp3` - written by the current build: the rip-path fix derives
  `%tracknumber% - %title%` from the archival template.
- `01. Calling All Stations.flac` - the cuetools engine's own default `%tracknumber%. %title%`
  (CUEConfig.cs:107), i.e. what output looked like before that fix, when nothing overrode the default.

So this is pre-fix vs post-fix output in one folder, NOT two schemes racing on the current build:
inferred from the two format defaults, pending confirmation by a fresh FLAC rip (expected
`01 - Calling All Stations.flac`). Phase 1 makes NamingEngine the single authority so the scheme comes
from the user's template for every format. Nothing migrates already-written files.
