# cuetools_2026 project instructions

Fork of cuetools.net being reviewed and modernized. The anti-dark-code review artifacts live
in `docs/review/` (system map, coverage ledger, remediation backlog, findings). Read
`docs/review/decisions-needed.md` before starting work that needs owner approval.

The LAME v4 encoder work lives in its own repository (LynxTWO/lame_v4); only plan and
progress documents for it belong here, under `docs/review/`.

## Build and test

- .NET Framework 4.7 solution built with VS 2022 Build Tools plus the net47 reference
  assembly package; no full Visual Studio on this machine. MSTest v2 for the migrated test
  projects. See `docs/review/` for the recorded green baseline and per-project quirks
  (CUEControls resgen, AccurateRip net20 flavor).
- Prefer `dotnet build` per project; some legacy GUI projects only build under full MSBuild
  and are deferred.

## Writing rules for all human-facing text

Docs here follow the same voice as the LAME v4 writing guide (`docs/writing-guide.md` in the
lame_v4 repository). The hard rules:

- No em dashes, no en dashes, no typographic Unicode (arrows, checkmarks, unicode minus,
  curly quotes). Use ASCII forms: " - ", "->", "x", "~", "<=", "...".
- Plain-English claim first, then the receipt (the measurement, file, or commit that backs
  it), then engineer detail. Tables for clustered numbers with a lead-in sentence.
- Short sentences, one claim per paragraph, bold only for outcomes and numbers that matter.
- Precise status verbs (measured, verified, inferred, unknown, rejected, pending); never
  upgrade an inferred claim to verified in prose.
- Never alter numbers, flags, commit hashes, paths, or commands when editing prose.
- Do not call the non-engineer reader a "layman"; use "plain English" or "normal reader".
