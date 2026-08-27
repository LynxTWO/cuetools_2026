# K: salvage Test & Copy receipts, 2026-08-27

Two full salvage Test passes of the damaged 24-track disc on the ASUS BW-16D1HT in K:,
run with the modern WPF client on the day CTDB moved to TLS (#85). The first run found a
regression in that change; the second run, on the fixed build (#86), completed its Test pass
and went on to Copy. Both runs retained the recovery-route counters that two hardware-gated
unknowns were waiting for.

## Host and method

| Item | Value |
| --- | --- |
| Machine | DESKTOP-D084LOM, AMD Ryzen 9 5950X 16-Core, Windows 11 Pro 10.0.26220 |
| Drive | ASUS BW-16D1HT, firmware 3.11, SATA, letter K:, read offset +6, calibrated (caches re-reads, flush 786,432 B) |
| Disc | the 24-track pin-holed disc (24 tracks, lead-out 307,668), the same disc as the R69 and R118 evidence |
| Build, run 1 | master `233d9164` plus #85 (`0ef18bc1`), Release, net8.0-windows |
| Build, run 2 | master `ea6d6702` plus #86 (`bb579103`), Release, net8.0-windows |
| Mode | Test & Copy, salvage capture (Burst quality, C2 pointers on, read speed pinned to the 704 kB/s minimum), cache defeat forced |
| Driver | the real app, started from `CUETools.Wpf\bin\Release\net8.0-windows`, "Read disc" and "Test & Copy" invoked through UI Automation |
| Logs | `%APPDATA%\CUETools2026\logs\cuetools-20260827-171354-915-p39500-*.log` (run 1) and `cuetools-20260827-180138-581-p2372-*.log` (run 2); structural logs, no names or paths |

## Run 1: the Test pass completed, then the CTDB contact failed

The Test pass read for **1,628 seconds** and gave up on four windows (60, 85, 87, and 93
percent; 1, 3, 4, and 1 unresolved sectors), with 320 concealed frames. Then `CUESheet.Go`
made the post-read CTDB contact through `CUEToolsDB.ContactDB(bool, bool, ...)` and the whole
Test & Copy failed:

```text
17:48:48.933 ERROR rip  failed after 1628s
    UriFormatException: Invalid URI: The hostname could not be parsed.
   at CUETools.CTDB.CUEToolsDB.ContactDB(String server, ...) in CUEToolsDB.cs:line 96
   at CUETools.CTDB.CUEToolsDB.ContactDB(Boolean ctdb, Boolean fuzzy, ...) in CUEToolsDB.cs:line 80
   at CUETools.Processor.CUESheet.Go() in CUESheet.cs:line 2927
17:48:48.935 WARN  rip  test&copy failed after 1630s phase=test
```

Cause, verified in source: the repeat-lookup overload re-derived the host as
`urlbase.Substring(7)`, the length of `http://`. After #85 the base is `https://`, so it
produced `https:///db.cue.tools/lookup2.php`. The disc-read path never hit this because it
always calls the overload that takes a server name. #86 binds the endpoint once
(`UseServer`), builds every request from that binding (`LookupUrl`), and pins both in a test
that was red before the fix and green after.

Counters at failure (the Test pass was complete, so these are a full-session receipt):

```text
control_transition_retries=0 read_communication_retries=0 cache_defeat_retries=0
cache_defeat_chunk_fallbacks=0 cache_defeat_wakes=0 cache_defeat_wake_readiness_retries=0
cache_defeat_wake_readiness_indeterminate=0 payload_batch_fallbacks=0 pinpoint_retries=0
corroborated_unreadable_pinpoints=0 drive_reported_timeout_pinpoints=0
drive_reported_timeout_batches=0 window_budget_stops=0 concealed_frames=320
extended_timeout_reads=30123 short_payload_transfers=0 given_up_windows=4
cache_defeat_unresponsive_signature=False payload_rejection_storm_fatal=0
```

## Run 2: the fixed build completes the Test pass and contacts CTDB over TLS

The Test pass read for **2,023 seconds**, gave up on six windows (61, 63, 85, 87, 93, and 94
percent; 1,504, 1,681, 2, 4, 366, and 1 unresolved sectors), and then the same CTDB contact
that failed in run 1 returned a real response:

```text
18:35:35.181 INFO  rip  done mode=verify elapsed=2023s ar_conf=0/82 ctdb_conf=0/237 accurate=False
  files=0 c2_mode=3 cache_defeat_bytes=786432 output_verify=0
  ... concealed_frames=2941020 extended_timeout_reads=36792 short_payload_transfers=0
  given_up_windows=6 cache_defeat_unresponsive_signature=False payload_rejection_storm_fatal=0
  reread_windows=8 reread_peak=15 failed_windows=6
  status=AR: rip not accurate (0/82), CTDB: could not be verified, ripper found 3558 suspicious sectors
18:35:35.203 INFO  rip  start mode=encode format=flac ...
```

`ctdb_conf=0/237` is the receipt: the database answered with 237 total confidence for this
TOC and none of it matched a salvage read of a damaged disc, which is the expected result.
The transport was TLS only. Earlier the same day, with the app driven through a disc read,
`Get-NetTCPConnection` for the app process showed the CTDB host (52.0.101.202) on port 443
and never on port 80.

Every wake, readiness, communication-retry, chunk-fallback, and short-transfer counter was
zero again. All other counters are in the log line above.

## Copy phase (run 2)

The Copy pass read for **1,946 seconds**, gave up on six windows (61, 63, 85, 87, 93, and 94
percent; 1,504, 1,681, 2, 3, 1,108, and 1 unresolved sectors), encoded 24 FLAC files with the
final-output verification performed, and contacted CTDB over TLS a second time:

```text
19:08:01.723 INFO  rip  done mode=encode elapsed=1946s ar_conf=0/82 ctdb_conf=0/237 accurate=False
  files=24 c2_mode=3 cache_defeat_bytes=786432 output_verify=1
  control_transition_retries=0 read_communication_retries=0 cache_defeat_retries=0
  cache_defeat_chunk_fallbacks=0 cache_defeat_wakes=0 cache_defeat_wake_readiness_retries=0
  cache_defeat_wake_readiness_indeterminate=0 payload_batch_fallbacks=0 pinpoint_retries=0
  corroborated_unreadable_pinpoints=0 drive_reported_timeout_pinpoints=0
  drive_reported_timeout_batches=0 window_budget_stops=0 concealed_frames=2580529
  extended_timeout_reads=35253 short_payload_transfers=0 given_up_windows=6
  cache_defeat_unresponsive_signature=False payload_rejection_storm_fatal=0
  reread_windows=7 reread_peak=15 failed_windows=6
  status=AR: rip not accurate (0/82), CTDB: could not be verified, ripper found 4299 suspicious sectors
```

Test and Copy disagreed inside the damaged zones (2,941,020 against 2,580,529 concealed
frames), so at 19:08:01 the job started its third, tie-break read. Its verdict is recorded
below.

### Tie-break read and verdict

The third read took **1,822 seconds**, gave up on five windows (63, 85, 87, 93, and 94
percent; 1,681, 2, 3, 1,168, and 2 unresolved sectors), and again recorded zero for every
wake, readiness, communication-retry, chunk-fallback, and short-transfer counter
(`extended_timeout_reads=33030`, `concealed_frames=1849423`, `given_up_windows=5`). Three
reads then went to the verdict:

```text
19:38:24.298 INFO  rip.slip  read-vs-read offsets: read1->read2=no-constant-offset-within-4096;
  read1->read3=no-constant-offset-within-4096; read2->read3=no-constant-offset-within-4096
19:38:24.298 INFO  rip       testcopy disc=... reads=3 passed=0 heldTracks=3
19:38:24.299 INFO  rip       test&copy done elapsed=5792s reads=3 outcome=held
```

**Outcome: Held.** Three independent reads disagree on three tracks, the slip correlator
finds no constant offset between any pair (so the disagreement is not a realignable shift),
and the job keeps the completed Copy in the explicit Held state: not published as verified,
not deleted. That is the documented Test & Copy contract for a disc whose damaged zones the
drive cannot read consistently, and it is the first time the whole three-read path has run
end to end on this disc with CTDB contacted over TLS after each read. Total wall clock
5,792 seconds.

On disk after the app was closed normally: the Test & Copy workspace
`<TEMP>\cuetc\.cuetools-testcopy-28d72750f3874b588be6f6cc69a3c178\` still exists, with its
ownership marker (`CUETOOLS_TESTCOPY_STAGE_V1`), a `copy` tree (24 FLAC files plus cue, log,
and artwork, 394,185,972 bytes) and a `third` tree (24 FLAC files, 394,201,290 bytes). The
configured output root `Music\CUETools 2026` received nothing, which is the point: a held
result is preserved, not published.

## What the two runs answer

| Unknown | Answer |
| --- | --- |
| GOOD-status SCSI underruns in the wild | Four complete K: reads retained `short_payload_transfers=0` across 30,123, 36,792, 35,253, and 33,030 extended-timeout reads plus every ordinary read. The R112 guard passes on this SATA drive with no false positives. Resolved for K:; H: has not run since the guard landed. |
| K damaged-disc dormant-drive wake | Did not recur. Zero wake attempts, readiness retries, indeterminate continuations, and chunk fallbacks on all four reads, `cache_defeat_unresponsive_signature=False`. The unknown stays open under its own rule: nothing forces a passing drive into the state. |
| Test & Copy Held state on real damage | Exercised end to end: three reads, three held tracks, no realignable slip, Copy retained and not published. |
| CTDB plaintext transport (D2) | The rip path itself now completes a lookup over TLS. Parity downloads remain plaintext at `p.cuetools.net`, behind the repair CRC and syndrome gate, and are the remaining upstream ask on issue #1. |

## Observations, not findings

- The two Test passes disagree about how bad the disc is: run 1 gave up on four windows with
  1 to 4 unresolved sectors each (320 concealed frames), run 2 on six windows including two
  with 1,504 and 1,681 unresolved sectors (2,941,020 concealed frames, 3,558 suspicious
  sectors). Same disc, same drive, 40 minutes apart, both at the pinned 4x minimum. Salvage
  mode counts and conceals what the vote cannot confirm, which is its contract; the variation
  is a property of the medium and is recorded here as measured.
- Seven `drive K: already owned by another CUETools job` warnings appeared in run 1's log
  while only one CUETools process existed. That is the tray watcher losing a same-process
  lease race; the lease is correct and the message is wrong. Filed as R126.
- A network failure at the end of a 27-minute Test pass discards the pass. That is
  pre-existing behaviour (`CUESheet.Go` lets the CTDB exception propagate) and outside this
  change; noted for a later decision on whether a failed CTDB contact should degrade to
  "could not be verified" instead of failing the job.
