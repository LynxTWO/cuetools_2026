# Unknowns: Adversarial Pass

Current-state refresh: 2026-07-28.

## Entries

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
