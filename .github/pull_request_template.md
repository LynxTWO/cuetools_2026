# Pull Request

<!--
Keep it short. Honest is required. Fill in what applies; "n/a" with a one-line reason is fine.
This repo carries an anti-dark-code map under docs/ - update it when you change what it describes.
-->

## What changed

<!-- Plain English, 1-3 sentences. -->

## Why

<!-- The reason, not the mechanism. Link an issue if one exists. -->

## Protected areas touched?

<!-- These need explicit reviewer approval before merge. Check any this PR touches and say how it was reviewed:
- [ ] CTDB/AccurateRip parity repair or the CRC self-check gate (CUETools.AccurateRip\CDRepair.cs VerifyParity)
- [ ] Plugin loading (CUETools.Processor\CUEProcessorPlugins.cs)
- [ ] EAC plugin or its installer (CUETools.CTDB.EACPlugin*)
- [ ] Credential handling / config at rest (ProxyPassword, Icecast password)
- [ ] CI or release workflow, collect_files, submodule patches, version source
- [ ] Secure-ripping vote / C2 handling (CUETools.Ripper.SCSI\SCSIDrive.cs)
Otherwise: "No protected area touched." -->

## Docs updated

<!-- Check what you touched. If the change alters what a doc describes, update the doc in the same PR. -->

- [ ] `docs/architecture/system-map.md` / `coverage-ledger.md` / `repo-slices.md`
- [ ] `docs/security/logging-audit.md`
- [ ] `docs/review/*` (adversarial, scenario, remediation backlog, decisions-needed)
- [ ] Relevant `docs/unknowns/*` entry
- [ ] README / inline comments on a touched critical path
- [ ] Not applicable (one-line reason): 

## Tests / checks run

<!-- Name what ran. TestParity and TestCodecs are the CI gate; both must stay green.
"dotnet test on TestParity + TestCodecs green locally" beats "tests pass". -->

## Sensitive-data self-check

<!-- Confirm none of these were added to code, logs, comments, tests, docs, screenshots, or commit text:
proxy/Icecast/RAR passwords, the raw machine identifiers behind the CTDB UUID, tokens, or real user data. -->

- [ ] Confirmed. No sensitive data added to any surface above.

## Protected comment / boundary continuity

<!-- If this PR removed or rewrote a comment that captured a trust boundary, invariant, failure mode, or the
CRC/repair/plugin/SCSI-vote rationale, either preserve it near the surviving code or explain the removal here. -->

## Follow-up

<!-- Anything left on the floor; link issues or the remediation backlog. -->
