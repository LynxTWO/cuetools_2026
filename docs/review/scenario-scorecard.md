# Scenario Scorecard

Pass 08, 2026-07-02. Companion to `scenario-stress-test.md`. Score: 0 = missed, 1 = partly covered, 2 = clearly covered.

## Per-scenario

| Scenario | Score | The gap, and what would raise it |
| --- | --- | --- |
| SC1 malicious CTDB parity | 2 | Future threat-model note: CRC32 is not collision-resistant; a signed parity manifest would make it 2+. |
| SC2 malformed FLAC -> OOB read | 1 | Trace decoder buffer sizing/padding; add a bounded fill or a guaranteed-padding contract; fuzz. |
| SC3 crafted MOTD image -> GDI+ | 1 | Move MOTD to HTTPS; consider dropping the remote image or sandboxing the decode. |
| SC4 reserved name / trailing dot | 1 | Add reserved-name + trailing-dot handling to `CleanseString`. |
| SC5 poisoned plugin DLL | 1 | Verify plugins-folder write perms in the shipped layout; consider signature/allowlist. |
| SC6 MITM forges AR confidence | 2 | D1 (AR -> HTTPS, approved) closes the transit gap. |
| SC7 RAR path traversal | 2 | Not reachable (Test-based in-memory read). Keep it that way if extraction is ever added. |
| SC8 proxy password at rest | 1 | Store via DPAPI / Credential Manager, or document the exposure. |
| SC9 tampered release, no tests | 2 (covered math) / 1 (engine, ripper) | Synthetic fixtures for TestProcessor; rework TestRipper to synthetic dumps. |

## Capability rows

| Capability | Score | Note |
| --- | --- | --- |
| Repo-fit detection | 2 | Scenarios grounded in real code paths, not generic web-app. |
| Hidden-entrypoint capture | 2 | EAC COM plugin, plugin folder, CI/release, MOTD all mapped. |
| Control-plane capture | 2 | CI/release workflows now test-gated; version-source split noted. |
| Approval safety | 2 | Protected areas (repair gate, plugin loader, EAC, credentials, release) named; approved decisions routed through smallest-safe-edit. |
| Integrity of verification/repair | 2 | CRC gate verified; corroborative-not-proof stance documented. |
| Untrusted-parser safety | 1 | BitReader OOB + GDI+ MOTD are the open exposure. |
| Evidence discipline | 2 | Unverified items (OOB exploitability, plugin-folder perms) marked, not asserted. |
| Coverage honesty | 2 | God-classes marked commented-at-choke-points, not fully read; test blockers stated. |

## Lowest scores -> highest-value next work

1. **Untrusted-parser hardening (SC2, SC3):** the BitReader bounds check and the MOTD HTTPS/removal are the two findings that most improve real safety. Both feed the remediation backlog and the fuzzing modernization idea.
2. **Test depth (SC9):** synthetic fixtures unblock the engine and ripper suites.
