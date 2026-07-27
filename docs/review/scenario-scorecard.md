# Scenario Scorecard

## Current-state addendum - 2026-07-26

The original scorecard below is dated 2026-07-02. Re-scoring against current code:

| Scenario | Current score | Current evidence / residual |
| --- | ---: | --- |
| SC1 malicious CTDB parity | 2 | Structural/CRC gates remain and a live deliberately damaged image was repaired/post-verified without changing the source. CRC32 is still not a signature and CTDB TLS remains external. |
| SC2 malformed FLAC/ALAC | 2 | Both readers are bounded and covered by deterministic malformed-input lanes. |
| SC3 crafted MOTD | 2 | No remote image path remains; bounded strict UTF-8 is fetched over HTTPS only. |
| SC4 reserved/trailing Windows name | 2 | `CleanseString` hardening and collision tests are green. |
| SC5 poisoned plugin | 2 packaged and enrolled / 1 development override | Packaged and explicitly enrolled user plugins use separate exact manifests and are rechecked at load. Registration requires exact encoder/decoder/ripper contract identity, rejecting interface-name lookalikes; compression attributes require the real provider contract, and HDCD requires its complete filter/interface/constructor shape. The local-development environment override remains deliberately unmanifested. Hash enrollment is integrity, not publisher signing. |
| SC6 forged AccurateRip transit | 2 | Both AccurateRip requests use HTTPS without downgrade; corroborative-result semantics remain. |
| SC7 RAR traversal | 2 | Input streams through callbacks and does not extract paths. Official signed UnRAR 7.23 is ABI/runtime-tested in both architectures; a committed RAR5 regression exposed and fixed backward-seek replay against stale EOF and passed 20/20 repeats. |
| SC8 proxy secret at rest | 2 Windows | DPAPI plus atomic migration/write; non-Windows nonempty-secret save fails closed. |
| SC9 untested/tampered release | 2 WPF / 1 classic | Canonical suite selection, artifact contracts (36 WPF paths / 97 classic paths), hashes, SBOM, immutable actions, and local actionlint exist. Classic AnyCPU is green at 53/0; x64 and Win32 are each 2/0 with 59 skipped configuration entries; TTA builds both; the installer is 8/0 and produced a 929,792-byte MSI. TestRipper passes its three-test/zero-skip contract. Frozen classic receipts, final-source H: repeat, and hosted execution remain pending. |

New scenarios added in the 2026-07-26 wave - concurrent publication, repair rollback,
external encoder hangs/truncation and approval races, WMA mismatch, native plugin
preloading/lookalikes, and settings interruption - are covered in
`2026-07-26-autonomous-audit.md`.

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
