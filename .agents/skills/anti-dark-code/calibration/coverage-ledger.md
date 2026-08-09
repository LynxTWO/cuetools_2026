# CUETools Coverage Calibration

Freshness: reviewed at commit `2a8df3e3` on 2026-08-09.

The detailed ledger is `docs/architecture/coverage-ledger.md`. Residual evidence gaps and their reopen triggers are in `docs/unknowns/coverage-pass.md`. Do not duplicate or silently close those records here.

| Surface | Last verified | Depth | Result | Guard | Invalidated by | Freshness |
|---|---|---|---|---|---|---|
| Shared anti-dark-code core | 2026-08-09 | distribution, universal, installed | v5 valid; 45 tests passed and 8 link-privilege cases skipped on this Windows host | shared 53-test suite and installed manifest | shared VERSION or managed core hash | current |
| CUETools mutation harness contracts | 2026-08-09 | structural and bounded profile review | seven profile contracts calibrated; exact scores remain in `eng/mutation` | `mutation-contract`; mutation workflow | harness, profile, linked source, tests, tool version | current at `2a8df3e3` |
| Existing architecture and audit corpus | 2026-08-09 | index and freshness review | canonical docs retained; no duplicate migration | doc-specific gates and unknowns | paths named in each document | current for routing only |
| Generated root `dotnet test` proposal | 2026-08-09 | command-shape review | rejected for mixed classic, WPF, native, vendor, and isolated profile graph | named test-suite and harness orchestrators | solution or test orchestration redesign | settled |

## Verification Calibration

- Pure naming, history, output-guard, discovery, artwork, and ripper policy decisions use deterministic tests plus scoped mutation evidence.
- Codec, native ABI, release, and artifact claims require exact target tuple and finalized-output evidence.
- Optical reads, cache behavior, drive transitions, and repair branches require real hardware or an explicit injected fault proving activation.
- WPF layout, theme, animation, and allocation claims require deterministic state tests plus rendered or measured runtime evidence.
