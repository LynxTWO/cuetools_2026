# Live release evidence, 2026-07-29

This record closes the selected interactive theme, metadata, optical-image, and
disc-visual checks for R77-R83. Local captures and audio outputs remain under the ignored
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

## Damaged-disc frame-time benchmark

The self-contained tier-2 artifact from commit `039129b` ran a real Paranoid
Test & Copy against the damaged 68:22 disc in K:. The host used an RTX 3060 at
2560x1440 and 69 Hz. The opt-in sampler observed the existing
`CompositionTarget.Rendering` seam with fixed histograms. Its hot loop allocates
zero bytes after warmup. Raw frame, counter, screenshot, and diagnostic receipts
remain in the ignored evidence tree because the captures contain media metadata.

The normal and damaged intervals remained effectively identical:

| State | Frames | Time | p50 | p95 | p99 | Maximum | Frames over 33.33 ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| Normal reading | 87,832 | 1,249.4 s | 14.3 ms | 14.7 ms | 15.0 ms | 46.0 ms | 5 |
| Re-reading / autozoom | 49,984 | 714.9 s | 14.3 ms | 14.7 ms | 15.0 ms | 40.1 ms | 3 |
| Unreadable hold | 1,186 | 16.7 s | 14.3 ms | 14.7 ms | 15.0 ms | 19.4 ms | 0 |

The model callback averaged 0.0135 ms during normal reading and 0.0227 ms during
re-reading. Its re-read maximum was 1.1332 ms. The receipt recorded 29 state
transitions with no overflow. The normal log independently recorded 385
recovery-pass lines across the Test and Copy reads. A live capture shows the
amber marker, zoomed disc, recovery overlay, re-read counter, and reduced drive
speed at the same 60 percent event.

Frame pacing passed. The Copy phase later failed at 92 percent because K:
rejected a one-sector cache-defeat read at
relative sector 246134 with `IllegalRequest 24/00`. No staged directory or album
was published. This is optical R69 evidence, not a rendering failure. The run
also proved that an early Test & Copy error returned to the UI without a terminal
`test&copy failed` diagnostic. R82 adds the missing phase-bound terminal line;
diagnostic failure remains unable to replace the original result.

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

## Final damaged-disc completion and repair

The source-bound build at commit `17d984e` completed an uninterrupted Paranoid
Test & Copy on K: in 2,275 seconds. Both reads were consistent, the Copy crossed
the former 92-percent failure boundary, and the final encoded PCM check passed.
The reader retained three exhausted windows and six suspicious sectors, so the
result correctly required CTDB repair instead of claiming clean verification.

CTDB published a source-preserving sibling with 25/25 source proofs and 25/25
output proofs. Independent decoded-PCM comparison found 86 changed channel
samples in 67 stereo frames across the receipt's six sectors. Only tracks 15,
16, and 20 changed; the other 21 tracks null exactly. The repaired audio matched
AccurateRip at 55/82 and CTDB at 207/234. All 24 audio basenames, descriptive
tags, and stream layouts were preserved. Stale proof tags were intentionally
replaced by `repair.verify` and fresh human-readable repair reports.

All cache wake, readiness, retry, and chunk-fallback counters were zero. This
proves the end-to-end outcome and clears the observed R69 blocker, but it does
not prove activation of the intermittent wake branch.

The run also exposed R83. Test & Copy began 0.711 seconds before asynchronous
artwork discovery completed, so it froze a null cover even though the UI showed
the selected cover moments later. Source and repaired files therefore contain
no pictures. The software fix keeps encoded-job commands disabled until artwork
loading completes and preserves Verify availability. A fast live embed repeat
remains; this damaged-disc run is valid CTDB evidence, not artwork proof.

## Final software and artifact gates

- modern ripper suite: 28/28 passed, no skips;
- WPF suite: 431/431 passed, no skips;
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

## Hosted project, FFmpeg, and release evidence

The following GitHub-hosted runs close the current-source workflow boundary.
Run and artifact claims in this section are bound to the recorded commit rather
than inferred from the branch name.

- [FFmpeg 8 matrix run 30516040154](https://github.com/LynxTWO/cuetools_2026/actions/runs/30516040154)
  completed successfully at commit
  `15e4cc39ae6deb4f2fedf6cf54d9624a6a0a35c7`. Both `x86-windows` and
  `x64-windows` jobs built the pinned FFmpeg 8.1.2 runtime and exercised it
  through FFmpeg.AutoGen 8.1.0 in a matching process. Each passed 16- and
  24-bit, 5,003-frame path and managed-stream decode, nonzero seek replay, EOF
  drain, disposal, and callback-containment checks. Both check runs have zero
  annotations. This proves the deliberately standalone AIFF decoder path, not
  every FFmpeg demuxer or decoder, and FFmpeg remains outside both shipping
  artifact collectors.
- [Classic CI run 30518472651](https://github.com/LynxTWO/cuetools_2026/actions/runs/30518472651)
  completed successfully at commit
  `33c8eea3b8d085d5bb2473a7f6451dce2ba4e294`. Hosted Windows Server 2022
  discovered 22 parity tests (18 passed, four intentional skips), 113 codec
  tests (111 passed, two intentional skips), ten processor tests (nine passed,
  one intentional skip), and 17 ripper tests (all passed). The converted
  classic CUETools and CUEPlayer projects were included in the guarded Visual
  Studio build graph. Native warning emission was zero and the check run has
  zero annotations.
- [WPF CI run 30518472662](https://github.com/LynxTWO/cuetools_2026/actions/runs/30518472662)
  completed successfully at the same commit. It passed 28/28 modern ripper
  tests and 440/440 WPF tests, emitted no native warning lines, and has zero
  annotations.
- [Windows release run 30518479906](https://github.com/LynxTWO/cuetools_2026/actions/runs/30518479906)
  completed successfully at the same commit and has zero annotations. Its
  downloaded `deploy` artifact was independently inspected under
  `J:\TEMP\cuetools-release-final-30518479906`.

The independent release-artifact audit found:

- the classic contract passed with 97 files and 10,742,302 bytes;
- the WPF receipt bound 557 files and 194,127,197 bytes, while its shipping
  contract passed all required paths, 19 plugin registrations, and five native
  probes;
- every provenance-manifest entry matched the downloaded path, length, and
  SHA-256; both receipts reported `source.state=clean`, no root patch, no
  unknown untracked files, and five clean submodules;
- both receipts recorded the validated 423-member Monkey's Audio 13.20
  expansion and closure SHA-256
  `5777ba9a6debcd55565ba49c2e713fdb46a62d81474bc17d394ef17893eeb578`;
- the custom SPDX guard and Microsoft SBOM Tool independently accepted exact
  97/97 classic and 557/557 WPF file closures, including matching final
  sidecars;
- CycloneDX retained nonempty package graphs: 24 components/25 dependency
  nodes for classic and 37 components/38 dependency nodes for WPF;
- `signing-status.json` selected 117 publisher-owned files and correctly
  labeled the manual build `unsigned-evaluation`, with
  `productionRelease=false`.

The last point is an intentional boundary, not a signed-release claim. The
repository policy now refuses an unsigned tag or explicitly signed dispatch,
but a production-signed artifact still requires a public-trust code-signing
certificate and protected `release-signing` environment values.
