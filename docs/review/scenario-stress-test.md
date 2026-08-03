# Scenario Stress-Test

## Damaged-disc recovery addendum - 2026-08-01

The 2026-08-01 adversarial pass found this document contained no scenarios for
the R55-R105 optical recovery machinery. This addendum adds the scenarios that
wave exposed, in the state the R106-R115 remediation left them. Score key
matches the original pass (0 missed / 1 partial / 2 covered); the score given
is the CURRENT state after remediation.

### SR1. Deep recovery converges late, or never, on a scratched disc - score 2

A stuck window extends past the baseline pass cap. Before R106, a
never-converged sector was reported clean (the give-up sentinel compared an
exact pass count that the extension overshot) and a late-converged sector was
reported failed. Closed by engine-maintained give-up state
(`FailedSectorAccounting`, one-shot `WindowGivenUpSectors` verdict) with
legacy-sentinel equivalence proven by test.

### SR2. StopOnUnrecoverable during the final pass of a recoverable window - score 2

The old mid-pass latch aborted jobs whose final pass would still converge and
made the deep-recovery extension unreachable. Closed by R110: the stop keys on
the engine's post-classification verdict; source contracts pin both the VM
latch and the service forwarding.

### SR3. Damaged disc, Test & Copy held, then the user presses eject - score 2

Before R108 the only completed encoded Copy was silently deleted by eject,
a multi-poll phantom tray event, or a Stop during the confirming read (which
StopOnUnrecoverable could issue automatically). Closed: stop routes hold, tray
routes park keyed to the disc, and only a different disc, a new job, or an
explicit Discard frees the staging. Residual (R116): a held result does not
survive app exit.

### SR4. Damaged-consistent result read as clean verification - score 2

The certificate headline, badge, and history rows said "Verified" while the
log said CONSISTENT, and committed AR/CTDB numbers could come from a read
other than the committed one. Closed by R109 damage-aware labeling and
committed-read evidence binding, including a database match over damaged
media staying consistent.

### SR5. Multi-sector 24/00 during a speed or cache transition - score 2

Decomposition consumed the transition rejection as media evidence before the
one-shot transition retry could see it, and a transition state could mark good
sectors untrusted. Closed by R114: decomposition refuses while a transition is
pending; the repeat after the retry stays fatal. Deterministic policy tests
cover both orderings; live activation evidence remains open (R116 seam).

### SR6. Drive completes a READ CD with GOOD status but fewer bytes - score 1

Stale bytes from the reused buffer would have entered the vote as a clean
pass. R112 rejects the underrun fatally with a named counter; score 1 because
live behavior on real bridges is unobserved (open unknown; the counter is the
instrument).

### SR7. Classic CUERipper secure rip on a caching drive - score 1

The classic path still has no cache defeat or calibration gate (decision D8
pending). R113 closed the honesty half: Test/Copy CRCs are compared, the
hardcoded cache/C2 log lines are truthful, mismatches escalate the completion
dialog, and persisted Paranoid survives restart. Score 1 until D8 resolves
the gate itself.

### SR8. CTDB offers two recoverable pressings, the wrong one first - score 2

Repair applied the first server-ordered variant regardless of confidence.
Closed by R111 confidence-ranked selection with a stable tie-break.

## Current-state addendum - 2026-07-26

The scenarios below are the preserved 2026-07-02 pass. SC2, SC3, SC4, SC6, and SC8
are now closed by bounded decoders, HTTPS text-only MOTD, filename hardening,
AccurateRip HTTPS, and DPAPI/atomic settings respectively. SC5 is closed for
manifest-bound packaged plugins and separately manifest-bound per-user enrollment.
The deliberately unmanifested local-development override remains a trust boundary,
and neither hash manifest is a substitute for publisher signing.

The follow-up pass added and exercised repo-specific failure scenarios for
cross-process album publication, repair staging/verification, external encoder
timeouts and truncated output, WMA lossless mismatches, plugin identity preloading,
settings-write interruption, native finalization failure, and artifact omissions.
Later live runs added WMA Lossless, FLACCL/RTX 3060, H:/K: optical reads, an H:
full FLAC rip and two-read Test & Copy, CTDB repair, and Icecast 2.5.0 evidence.
K: later supplied a real damaged 24-track rip whose post-rip six-sector CTDB repair
published a verified sibling while the original file-set hash remained unchanged.
Results and residual external checks are in `2026-07-26-autonomous-audit.md`.
TestRipper's former private captures and stale copied vote algorithm have also been
replaced by SDK net47 tests against the production `SecureSectorVote` helper; its
canonical run passed 3/3 with zero skips.

Pass 08, 2026-07-02. Synthetic abuse/failure scenarios that fit CUETools' actual shape, used to test whether the current map and comments would hold. Score key: 0 = the current understanding misses it, 1 = partly covered, 2 = clearly covered. Scorecard in `scenario-scorecard.md`.

## Scenarios

### SC1. Malicious CTDB parity blob tries to rewrite audio

Plausible: parity is fetched over unauthenticated HTTP. An on-path attacker returns a crafted parity file to corrupt a "repaired" rip.
- **Holds?** Yes. `AccurateRip\CDRepair.cs VerifyParity` accepts a fix only when the corrections reproduce the local rip's CRC (residual == 0). A forged blob fails the gate -> `canRecover = false`. Commented at the gate (S5).
- **Residual gap:** CRC32 is not collision-resistant; an adversary fully controlling parity is bounded by RS structure + 32-bit check, not a signature. Adequate vs corruption, noted for a future threat-model pass. **Score 2.**

### SC2. Malformed FLAC/ALAC file triggers an out-of-bounds read

Plausible: users open arbitrary lossless files; decoders parse untrusted bitstreams in `unsafe` code.
- **Holds?** Partly. `BitReader.fill()` has no bounds check (documented S6, high-risk unknown). Whether callers pad/size the buffer to bound the overrun is untraced. A crafted truncated frame is a real crash candidate. **Score 1.**

### SC3. Crafted MOTD image exploits GDI+

Plausible: `frmCUETools` fetches `motd.jpg` over plain HTTP and renders via `Image.FromStream`. MITM or a compromised cue.tools serves a malformed JPEG.
- **Holds?** Partly. The trust boundary is commented (S9) and gated behind `checkForUpdates` (once/day), but the code path exists and hands remote bytes to a native decoder over HTTP. D1-style HTTPS is not yet applied to MOTD. **Score 1.**

### SC4. Metadata with a Windows reserved name or trailing dot

Plausible: gnudb/MusicBrainz return an album titled `NUL` or `CON`, or with trailing dots.
- **Holds?** Partly. `CleanseString` strips path separators and `:` (traversal blocked, verified S3) but not reserved device names or trailing dots. Result is a failed/surprising write, not a traversal. Logged in critical-paths. **Score 1.**

### SC5. Poisoned plugin DLL in the plugins folder

Plausible: a `CUETools.Evil.dll` dropped into `<exe>\plugins` is loaded and instantiated unsigned, in-process.
- **Holds?** Partly. Documented as a trust boundary (S3). Mitigation (folder write-perms) is assumed for the Program Files install but unverified for the portable/zip layout. No signature check. **Score 1.**

### SC6. MITM forges AccurateRip confidence over plain HTTP

Plausible: attacker on the network returns a dBAR response claiming a bad rip is "accurately ripped."
- **Holds?** Yes, by design intent. AR/CTDB results are treated as corroboration of locally computed CRCs, not independent proof (commented S1). D1 (HTTPS for AR) is approved and will close the transit gap. **Score 2.**

### SC7. RAR with path-traversal filenames (`../../evil`)

Plausible: a malicious RAR used as input.
- **Holds?** Yes. The input path streams via `Unrar.Test()` + DataAvailable callback; it never extracts to a filesystem path, so traversal filenames have nowhere to land (verified S4, pass 07). Official signed UnRAR 7.23 now passes the production path in both architectures. A committed RAR5 fixture additionally exposed and fixed backward-seek replay against stale EOF, then passed 20/20 repeats. **Score 2.**

### SC8. Proxy password read from the profile at rest

Plausible: malware or another user reads the CUETools profile file.
- **Holds?** Partly. `ProxyPassword` is stored plaintext (F1, logging audit). Not logged, but readable on disk. Remediation queued. **Score 1.**

### SC9. A tampered release ships because tests never ran

Plausible (historical): before this session, CI built but ran no tests, so a regression in parity/codec math could ship.
- **Holds now?** Yes for enrolled local coverage. TestParity/TestCodecs are `dotnet test`-gated in both CI and release workflows (S13/S14), TestProcessor fixtures are restored, and the synthetic production-vote TestRipper fixture passes 3/3 with zero skips. Hosted execution, frozen classic artifact receipts, and real optical failure injection remain. **Score 2 for covered logic, 1 for hosted/hardware evidence.**

## Themes

1. The **integrity story is strong** (CRC repair gate, corroborative verification, no extract-to-disk) - SC1/SC6/SC7 score 2.
2. The **untrusted-parser story is the weak axis** - SC2 (BitReader OOB) and SC3 (GDI+ MOTD) are the real exposure, both on attacker-controlled bytes. These are the top candidates for the fuzzing/hardening work (modernization idea 9).
3. **Data-at-rest and reserved-name edges** (SC4, SC8) are low-severity robustness/hardening gaps, tracked.
