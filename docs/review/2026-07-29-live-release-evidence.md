# Live release evidence, 2026-07-29

This record closes the selected interactive theme, metadata, optical-image, and
disc-visual checks for R77-R80. Local captures and audio outputs remain under the ignored
`evidence/live-2026-07-29/` tree. They are not release payloads and are not
committed because they contain user media and metadata.

## Interactive theme check

- The committed WPF release artifact was rendered at the host's actual 96 DPI.
- Dark and light captures were inspected at 1590x880 and 1180x740.
- Text, custom controls, switches, cards, artwork, and action buttons remained
  legible in both palettes.
- The compact layout kept every job control reachable. The track evidence table
  used horizontal scrolling for columns that did not fit.
- The first dark artwork-browser capture exposed a default-white DataGrid body
  with unreadable row text. After moving body, cell, selection, grid-line, and
  text colors onto dynamic palette resources, dark and light 1040x700 captures
  showed the complete candidate row and reachable selection controls.

This check does not claim 200 percent DPI or Windows high-contrast coverage.
Those remain separate artwork-browser accessibility checks under R72.

## Optical disc visual

Actual 1180x740 dark and light windows were captured at 96 DPI. The refreshed
model exposes the center hole, mirrored hub, clamp ring, program area, clear
outer rim, and edge thickness. The pickup is below the substrate, and the small
surface spot replaces the prior above-disc pointer. Theme-owned materials keep
the edge visible on both backgrounds.

An offscreen matrix rendered idle, reading, re-reading, unreadable, and tier-zero
fallback states in both themes. The matrix is generated from the production
controls. It is not a second physical damaged-disc run. Contract tests preserve
the existing live bindings, equal-area read radius, re-read back-track and zoom,
recovery ease-out, and unreadable hold.

## Live metadata correction

The H: regression disc returned six metadata candidates. Before the correction,
the one-disc and generic two-disc MusicBrainz entries both scored 145 because
provider-specific track-artist credit spelling prevented the first duplicate
test from matching them.

The corrected duplicate test uses album identity, year, barcode, track count,
and normalized track titles. It does not use provider credit spelling as
physical-disc identity. The live rerun ranked:

- one-disc MusicBrainz candidate: 145, selected;
- generic two-disc MusicBrainz candidate: 142, retained as an alternate.

The published album directory had no multi-disc descriptor or disc subfolder.
Tests retain the protections for named box discs, different barcodes, and
genuinely different track lists.

## Live FLAC image output

The same H: disc was ripped in Paranoid mode with `Image + embedded CUE`.
The committed album contained:

- one FLAC file, 330,738,757 bytes;
- one external cue sheet;
- one EAC-style rip log;
- one AccurateRip/CTDB report;
- one TOC sidecar;
- cover art;
- `rip.verify`;
- the final `.cuetools-complete` marker.

Observed verification:

- AccurateRip confidence: 28;
- CTDB confidence: 241;
- embedded cue tracks: 10;
- external cue tracks: 10;
- independently decoded samples: 146,313,216;
- PCM format: stereo, 16-bit, 44.1 kHz;
- embedded pictures: one, byte-for-byte equal to the 100,222-byte `folder.jpg`;
- receipt assurance: lossless output decoded and compared after metadata
  finalization.

Repair discovery selected the single external cue as the authoritative source.
That cue names the one FLAC image, so a later CTDB repair opens the album image
without guessing among track files.

The clean disc did not require CTDB repair. This run proves image publication
and repair-source binding. It does not replace the separate damaged-disc K:
repair evidence recorded under R71.

## Final software and artifact gates

- modern ripper suite: 22/22 passed, no skips;
- WPF suite: 423/423 passed, no skips;
- WPF/fuzz warning budget: zero emitted warning lines and zero fingerprints;
- self-contained x64 publish: artifact contract passed, including 19 plugin
  registrations and five native-plugin probes;
- local anti-dark-code skill: structure validation passed.

## Test isolation

The live output used a dedicated ignored evidence root. The deliberately
cancelled pre-fix run left one empty multi-disc folder; that exact empty folder
was removed after containment and emptiness checks. The user's application
profile was restored to Dark, Tracks, FLAC, Paranoid, and the normal
Music/CUETools output directory.
