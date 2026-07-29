# Unknowns: Adversarial Pass

Current-state refresh: 2026-07-29.

## Entries

### K damaged-disc dormant-drive wake

- **Area or file:** `CUETools.Ripper.SCSI/SCSIDrive.cs`, R69 hardware evidence.
- **Concern:** The post-wake readiness settle/retry has not yet crossed the
  firmware state that rejects every cache-defeat command and then rejects the
  two bounded readiness CDBs.
- **Why it matters:** software policy tests cannot prove that bounded settling
  and a complete post-wake eviction after `START UNIT` revive this ASUS firmware
  without a physical reload.
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
  still requires the complete cache eviction as the authoritative proof. Ripper
  tests pass 28/28, WPF tests pass 430/430, the legacy builds and warning gate
  pass, and the production artifact contract passes.
- **Confidence:** unknown.
- **Likely owner:** repo owner.
- **Next best check:** physically reload the damaged disc in K:, then run full
  Test & Copy from the source-bound published build through the dormant
  transition and prove the post-wake complete eviction succeeds or returns its
  next exact failure receipt.
- **Risk level:** high
- **Status:** blocked
- **Notes:** 2026-07-29 - the exact production build is ready; blocked only on
  physically reloading K: after the last failed run.

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
