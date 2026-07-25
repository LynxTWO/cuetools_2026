# Naming unification (MusicBrainz Phase 1) - SDD progress ledger

Plan: docs/superpowers/plans/2026-07-25-naming-unification-phase1.md
Branch: feature/naming-unification
Base commit: 3f813d7 (HEAD before Task 1)

- Task 1 (NamingContext fields + new tokens): complete (commit 5606a38, 44/44)
  (also de-flaked my ALAC verify test that Task 1's run exposed - separate commit)
- Task 2 (NamingPaths.Split + fuzz): complete (commit bd52174, 50/50)
- Task 3 (CUEMetadata -> NamingContext mapper): complete (commit d7c5a2f, 52/52; Catalog<-LabelNo verified)
- Task 4 (CUESheet.SetExplicitTrackNames hook): complete (commit 658180e, reviewed, 52/52)
- Task 5 (route rip encode naming; drop the stopgap): complete (commit f374458, done by controller)
  plus path-safety fix 8b6502f: field values with '/' or '\' were becoming folders ("AC/DC"); RED->GREEN
- Task 6 (route convert naming): complete (commit 38a7bf9; AppSettings injected, DI verified)
- Task 7 (reconcile NamingViewModel): complete (commit 910c39f) + 2049ba7 removed the same push the
  constructor was doing at startup (subagent flagged it as out-of-scope; controller took it)
- Task 8 (live rip verification): OWNER - needs the drive

Note: Task 5 removes the EngineTrackFilenameFormat stopgap that currently makes encodes work, so a
live rip must be run before trusting the branch (Task 8).

## Whole-branch review (opus) - 4 must-fix findings, all measured

- Finding 1 CRITICAL (multi-disc / multi-segment commit path in Test & Copy): FIXED, commit d0c3dbd.
- Finding 2 IMPORTANT (HTOA named from the polluted engine trackFilenameFormat): FIXED, commit d0c3dbd
  (explicit HTOA name + startup reset of a stale WPF-style trackFilenameFormat).
- Findings 3/4/7/8 (unconditional filesystem safety; non-empty+unique track names; total-path-length
  cap; SetExplicitTrackNames validation + idempotence): code applied, see UNCOMMITTED below.
- Finding 5 (preview shows %releasetype% but output renders it empty), Finding 6 (convert fallback for
  metadata-less sources can overwrite): NOT yet addressed - deferred, still open.
- Finding 9 (Render fuzz for filesystem safety): test written, see UNCOMMITTED below.

## RESOLVED - all nine review findings closed, 86/86 tests green

- Findings 3/4/7/8/9: commit bf3124c (controller reviewed the killed subagent's edits, wrote the
  missing tests; the Render fuzz found no further engine violations over 4000 nasty inputs).
- Box-set guard: commit 3d3195d - 100 and 250 discs produce distinct folders under one album folder.
- Disc padding to the set's width (9 / 99 / 999 / 9999 tiers, boundaries pinned, ordinal sort ==
  disc order over complete sets): commit ad2d855. Ordinary 2-CD releases unchanged.
- Findings 5 (preview claimed a %releasetype% folder output would not write) and 6 (two untagged
  convert sources collided in one "Unknown Artist - Unknown Album" folder): commit c14cbdd.

REMAINING: Task 8 live rip verification (needs the drive), then merge feature/naming-unification.

## Historical note (superseded by the entry above)

The fix subagent for findings 3/4/7/8 was killed mid-task by an API error, after editing code but
before writing any tests. The controller reviewed all five of its edits (NamingEngine MakeFilesystemSafe,
NamingPaths EnsureUniqueTrackNames + CapPathLength, the two call sites, CUESheet hook hardening) and
judged them correct, then wrote the missing tests (NamingPathsTests additions + NamingRenderFuzzTests).

NOT YET BUILT AND NOT YET TESTED - the shell was blocked by a classifier outage at that moment. Do not
trust or merge this working tree until `dotnet build CUETools.Wpf/CUETools.Wpf.csproj` and
`dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj` both pass.
