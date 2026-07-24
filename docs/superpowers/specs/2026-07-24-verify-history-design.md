# Verify history - second-source bit-exactness - design

Date: 2026-07-24
Status: approved, pending implementation plan
Scope: CUETools.Wpf (a new VerifyHistoryStore + a shared gzip-JSON helper, wired into RipService /
the verify result; retrofit of the existing JSON stores to gzip).

## Plain English

Today the only independent check that a rip is bit-exact is AccurateRip (and CTDB) - other people's
rips agreeing with yours. That is powerful, but it is silent on any disc AccurateRip has never seen:
a rare pressing, a personal disc, a promo. For those, there is no second opinion.

Verify history is that second opinion, built from your own reads. Every verify and rip records the
disc's per-track checksums locally. When you read a disc you have read before, the app compares the
new checksums against the stored ones and tells you whether the read is consistent with your earlier
one or differs from it. It is the automated, always-on version of the manual Genesis OFF-vs-ON check
we ran to prove deep recovery bit-safe - the app now does that comparison for every disc, forever.

It also directly hardens the riskiest deferred work: the slip re-alignment's one unsafe case was "a
non-AR disc could get silently-shifted audio with only AccurateRip able to catch it." A local verify
record catches exactly that - the re-aligned rip would not match your earlier plain read.

## Decisions (from brainstorming, 2026-07-24)

- Trigger: automatic, always-on. Every read records; a known disc is auto-compared; the verdict is
  surfaced (quiet on match, prominent but NON-blocking on differ).
- Storage: a human-readable `.verify` JSON sidecar in the album folder (tiny, openable, travels with
  the rip) PLUS a gzip-compressed history DB (it grows across discs, so compression pays there).
- Compression scope: retrofit all growing JSON stores to gzip (verify-history, reports, history,
  drive-calibration), each with load-either-format back-compat so existing files still open.

## The record

One `VerifyRecord`, keyed by the disc's AccurateRip TOC id (the standard fingerprint the engine
already computes from the table of contents):

- Per-track checksums the engine already produces for its own checks - captured, never recomputed:
  the AccurateRip CRC (v1 and v2) and the CTDB CRC32.
- Context: AR confidence + total, CTDB confidence + total, drive name, read offset, correction
  quality, deep-recovery on/off, UTC timestamp, ripper version.
- Display: album title and artist (your own local data, for showing you which disc a record is).

Note on cross-drive validity: the AccurateRip CRC is offset-corrected to a canonical position, so the
same audio yields the same AR CRC regardless of drive. The comparison therefore holds even if you
later read the disc on a different drive - identical audio, identical stored CRCs.

## Two stores

- `.verify` sidecar: one `VerifyRecord` as readable, indented JSON, written into the "Artist - Album"
  output folder on a rip. Self-contained proof that travels with the files.
- History DB: `%AppData%\CUETools2026\verify-history.json.gz` - the whole table (disc id -> the last
  few reads of that disc, bounded to 5 records per disc so it cannot grow without limit),
  gzip-compressed.

## The automatic check

In `RipService.Run`, after `cue.Go()` produces the AR/CTDB result:

1. Build the `VerifyRecord` from the engine's per-track AR/CTDB checksums. (Integration note: the
   per-track CRCs live on the cue's AccurateRipVerify / CTDB verify objects after Go(); confirm the
   exact accessors during implementation.)
2. `VerifyHistoryStore.CompareAndUpsert(record)` returns an outcome: `KnownDisc`, `Matches`,
   `PriorReads`, `DiffTrackCount`. If the disc id is already stored, compare per track: a track
   MATCHES when its AccurateRip CRC agrees with the most recent stored read (compare v2, fall back
   to v1 when a stored read predates v2); a DIFFER is any track whose AR CRC disagrees. The CTDB
   CRC32 is stored and shown as corroboration but the AR CRC is the match criterion (it is the
   offset-corrected, canonical per-track checksum).
3. Surface the outcome on `VerifyResult` (new fields) so the result UI shows:
   - unknown disc: "first read - recorded" (quiet),
   - match: "consistent with your N earlier read(s)" (quiet, positive),
   - differ: "DIFFERS from your earlier read on M track(s) - investigate" (prominent, non-blocking).
4. Upsert the record (new disc -> insert; known disc -> append to its bounded history).
5. On a rip (encode), also write the `.verify` sidecar into the output folder.

Mismatch is informational, never blocking - it flags, the user decides. A differ is worth surfacing
loudly because it means two reads of the same disc disagreed, which is exactly the bit-exactness
signal this feature exists to give.

## Compression: one shared helper, retrofit with back-compat

A single static helper (for example `GzJson`) that every store uses:

- `T Load<T>(path)`: if `path` (or `path + ".gz"`) exists, read it; detect gzip by the magic bytes
  (0x1f 0x8b) and decompress if needed, else parse plain. So an old plain-JSON file still loads.
- `void Save<T>(path, obj)`: serialize indented JSON and write gzip-compressed.

Retrofit `DriveCalibrationStore`, the report store, and the history store to load through this helper
(reads either format) and save compressed. First save after upgrade rewrites the old plain file as
gzip; the load path keeps opening either, so no data is lost and no manual migration is needed.

The `.verify` sidecar is the one exception - it stays plain readable JSON (a few KB; compressing it
saves nothing and costs openability).

## Privacy

The DB and the sidecar hold album title/artist - your own local files, your own data. The SHAREABLE
diagnostic log stays ids/numbers only, per the existing rule: a verify-history log line is
`verify.history disc=<id> known=<0|1> matches=<0|1> diffTracks=<n>` - no titles, no paths.

## Non-goals

- Not a replacement for AccurateRip/CTDB - a SECOND, local, offline source alongside them.
- No online sharing (that is CTDB's role).
- Never auto-blocks a rip on mismatch; it informs.
- The `.verify` sidecar is not compressed.

## Follow-ups (out of scope here)

- Use verify history as the AccurateRip-independent gate that lets the deferred slip re-alignment
  ship safely on non-AR discs.
- Optional: a small "history" view listing your verified discs and their consistency.
