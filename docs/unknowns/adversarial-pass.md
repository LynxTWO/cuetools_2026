# Unknowns: Adversarial Pass

Current-state refresh: 2026-08-01 (damaged-disc recovery addendum).

## Entries

### GOOD-status SCSI underruns in the wild

- **Area or file:** `Bwg.Scsi/Device.cs:836-852`, `SCSIDrive.cs:1472-1489`.
- **Concern:** the pass-through returns Success on `ScsiStatus == 0` without
  re-reading `DataTransferLength`, so a GOOD-status underrun would fold stale
  bytes from the reused read buffer into the vote as a clean pass. Whether any
  real drive or USB/SATA bridge produces GOOD-status underruns is unknown.
- **Why it matters:** if it occurs, wrong audio enters the secure vote with
  clean-pass weight, and in burst mode goes straight to the published rip.
- **Evidence found so far:** `f.DataTransferLength` is written before the ioctl
  and never re-read; the buffer canary fill runs only under `_debugMessages`
  (`SCSIDrive.cs:1474-1479`); no residual assertion exists anywhere in the
  transport. Code fact verified 2026-08-01; live behavior unknown.
- **Confidence:** verified (code), unknown (live occurrence).
- **Likely owner:** optical/SCSI maintainer.
- **Next best check:** assert `DataTransferLength == requested` after every
  data-in pass-through and log a named counter; run one full H:/K: session and
  retain the counter evidence.
- **Risk level:** high.
- **Status:** in progress. 2026-08-01: the exact-length guard and
  `ShortPayloadTransferCount` counter landed (R112); the payload path now
  fails loudly instead of silently consuming stale bytes. Remaining: one live
  H:/K: session retaining the counter (expected zero) to prove the guard
  passes on real hardware.

### Deep-recovery pass counts vs 8-bit vote accumulators

- **Area or file:** `SCSIDrive.cs:1438-1447`, `SecureSectorVote.cs:33-46`,
  `RecoveryPolicy.cs`.
- **Concern:** `RecoveryPolicy` bounds a window by plateau (8 passes) and time
  (120 s) but not by pass count; past 255 contributing passes the byte lanes
  and `C2Count` wrap, which can flip a vote with a confident margin. Whether
  more than 255 passes fit inside the ceiling on real hardware is unknown; it
  plausibly requires cache-served sub-500 ms re-read passes.
- **Why it matters:** a wrapped lane corrupts every sector in the stuck window,
  including clean ones, and the result publishes as secure.
- **Evidence found so far:** accumulator adds are plain byte/lane adds with no
  saturation; baseline `max_scans` maxes at 128 and was safely under 256;
  `RecoveryPolicyTests.cs` covers only the policy arithmetic. Inferred
  2026-08-01, not personally re-traced end to end.
- **Confidence:** inferred.
- **Likely owner:** optical/SCSI maintainer.
- **Next best check:** cap deep-recovery passes at 255 (or saturate the
  accumulators) and add a max-pass-count telemetry counter to confirm real
  ceilings; a deterministic TestRipper case can prove the wrap today.
- **Risk level:** medium.
- **Status:** resolved 2026-08-01. `RecoveryPolicy.MaxPasses = 252` bounds the
  loop (R107), and the accumulator-capacity guard test pins the arithmetic.
  The wrap is unreachable by construction; the live-ceiling question no
  longer matters.

### Recovery orchestration branches lack in-repo activation tests

- **Area or file:** `SCSIDrive.cs` batch split/decomposition/transition-retry
  orchestration; backlog R55, R57, R58, R59, cache-defeat transition retry.
- **Concern:** the deterministic tests cover the pure classifiers
  (`PayloadReadFailurePolicy`) and a source-string contract, but no test
  drives the orchestration through a seam-injected failing Device, so the
  wiring between classifier verdicts and loop behavior has no automated
  activation evidence.
- **Why it matters:** the branch-activation rule: a passing end-to-end run
  does not exercise an intermittent recovery branch; regressions in the wiring
  would ship silently. The 2026-08-01 addendum's inferred finding that the
  multi-sector 24/00 transition retry is unreachable is exactly the class of
  defect such tests would catch.
- **Evidence found so far:** `ReadCommunicationRetrySourceContractTests.cs`
  asserts source strings; `PayloadReadFailurePolicyTests.cs` tests the pure
  policy; R105 has live H: route evidence; R55/R58/R59 medium-parent and
  rejected-parent pinpoint branches have zero recorded activations.
- **Confidence:** verified (absence of such tests), inferred (unreachability
  claims).
- **Likely owner:** optical/SCSI maintainer.
- **Next best check:** an injectable `IScsiDevice` seam (or subclass hook) that
  scripts exact sense sequences per command, then deterministic tests for the
  R55/R57/R58/R59 routes including the 24/00-during-transition ordering.
- **Risk level:** medium.
- **Status:** open.

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
