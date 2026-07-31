# Unknowns: Adversarial Pass

Current-state refresh: 2026-07-29.

## Entries

### H normal-read 08/0A communication retry activation

- **Area or file:** `Bwg.Scsi/Device.cs`,
  `CUETools.Ripper.SCSI/SCSIDrive.cs`, R105 hardware evidence.
- **Concern:** the exact retry is deterministic and packaged, but the new build has
  not yet encountered the real H: communication failure.
- **Why it matters:** policy and route tests prove eligibility and containment, not
  that an 80 ms same-command retry recovers this drive when the state recurs.
- **Evidence found so far:** four retained failures use `ReadCdBEh`, 16 sectors,
  transition flags false, and `HardwareError / 08/0A`, at relative sectors 36,000,
  36,576, 192,224, and 241,968. R105 permits one retry only in the top-level normal
  payload loop, uses no failed payload, keeps every retry failure fatal, exposes a
  counter, and passes the full software and artifact gates. Source-bound probes
  crossed all four addresses. A full H: Test & Copy then passed in 846 seconds while
  K: copied concurrently: 11 verified FLAC files, matching AR/CTDB evidence, zero
  failed windows, and final decoded-output verification. Both H: phases recorded
  zero communication retries.
- **Confidence:** high for route containment; unknown for live recovery.
- **Likely owner:** optical/SCSI maintainer.
- **Next best check:** retain the terminal log with
  `read_communication_retries > 0` if the exact hardware state recurs. Do not force
  the drive into a failing state merely to activate the branch.
- **Risk level:** high.
- **Status:** open.

### K damaged-disc dormant-drive wake

- **Area or file:** `CUETools.Ripper.SCSI/SCSIDrive.cs`, R69 hardware evidence.
- **Concern:** The corrected full Test & Copy passed, but the intermittent
  post-wake continuation did not activate during that run.
- **Why it matters:** an end-to-end pass proves the user outcome, not that the
  bounded wake and indeterminate-readiness branch works on this ASUS firmware.
- **Evidence found so far:** the source-bound 2026-07-29 Test pass reached the
  damaged zone and proved all fifteen address/shape commands received their
  local retry. All fifteen still returned exact `IllegalRequest/24/00`.
  Windows then reported no media and a new raw SCSI handle could not open K:.
  The user independently reports that physically reloading the disc restores
  the drive. A later Copy exercised the one-wake policy: `START UNIT` succeeded
  and immediate `TEST UNIT READY` returned exact `24/00`, so CUETools failed
  closed. A later source-bound Test reached 92 percent after 946 seconds and
  proved the 250 ms settle plus one exact readiness retry also returns `24/00`.
  The next bounded build treats only those two results as indeterminate and
  still requires the complete cache eviction as the authoritative proof.
  Commit `5fa2c65` then completed a 2,275-second Test & Copy and a verified
  six-sector CTDB repair, crossing the former Copy failure boundary. That run
  recorded zero wake attempts, readiness retries, indeterminate-readiness
  continuations, command retries, and chunk fallbacks.
- **Confidence:** unknown.
- **Likely owner:** repo owner.
- **Next best check:** if the exact dormant transition recurs, retain the
  positive branch counters and terminal receipt. Otherwise, use only a safe
  deterministic hardware fault-injection method; do not disturb a passing
  drive merely to force the state.
- **Risk level:** medium
- **Status:** open
- **Notes:** 2026-07-29 - the observed end-to-end blocker is cleared. Exact
  hardware branch activation remains unproved.

### TheAudioDB distribution tier and attribution

- **Area or file:** `CUETools.Wpf/Services/AlbumArtService.cs`, app settings,
  artwork browser, and release packaging.
- **Concern:** TheAudioDB documents a user-supplied free V1 key and premium
  options, but the applicable CUETools distribution tier and final attribution
  placement have not been externally approved.
- **Why it matters:** correct API calls do not by themselves establish that a
  particular distribution channel satisfies provider terms.
- **Evidence found so far:** official documentation identifies API-key
  authentication, not username/password authentication. Published terms require
  source attribution and distinguish free and paid publication uses. The provider
  can be implemented as an off-by-default user-key option without bundling a
  shared credential.
- **Confidence:** high for the technical authentication contract; unknown for
  the future distribution/account arrangement.
- **Likely owner:** release maintainer and provider account owner.
- **Next best check:** retain the official terms snapshot used for the release,
  confirm the intended distribution channel, and accept the in-app attribution
  before enabling the provider by default.
- **Risk level:** medium
- **Status:** open

### Provider image metadata completeness

- **Area or file:** artwork provider manifests and the artwork browser.
- **Concern:** providers do not consistently publish original dimensions and
  encoded byte length before the original is downloaded.
- **Why it matters:** the browser should expose facts it can prove without
  turning inspection into an unbounded download of every master.
- **Evidence found so far:** the shared candidate model preserves null values,
  and selected-master validation establishes exact dimensions and byte length.
  Cover Art Archive manifests may omit some facts.
- **Confidence:** high.
- **Likely owner:** WPF maintainer.
- **Next best check:** add bounded metadata enrichment and keep `unknown` when the
  provider or bounded probe cannot establish a value.
- **Risk level:** low
- **Status:** in progress

## Closed items

- **Local-file time-of-check/time-of-use:** closed by design. The importer reads
  one bounded regular file once and retains immutable bytes rather than a path.
- **TheAudioDB username/password storage:** closed by contract. CUETools does not
  request or store TheAudioDB account credentials; it accepts an API key only.
