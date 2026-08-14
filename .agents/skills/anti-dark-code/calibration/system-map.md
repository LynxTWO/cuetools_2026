# CUETools System Map Calibration

Freshness: reviewed at commit `2a8df3e3` on 2026-08-09; routing diff-refreshed at `6036896b` on 2026-08-14 (post history rewrite - all pre-rewrite hashes changed).

The canonical detailed maps remain:

- `docs/architecture/system-map.md` for runtime units, entrypoints, stores, native boundaries, plugins, and external systems
- `docs/architecture/repo-slices.md` for bounded ownership slices
- `docs/review/cuetools-capability-inventory.md` and `docs/review/codec-audit.md` for codec and feature reachability
- `docs/review/r12-release-audit.md` for build, package, and release topology

This calibration records routing and authority rather than copying those documents.

| Unit | Purpose and entrypoints | Primary evidence | Boundary |
|---|---|---|---|
| CUETools 2026 WPF | Rip, Verify and Repair, Convert, settings, queue, report, multi-drive sessions | `CUETools.Wpf`; `CUETools.Wpf.Tests` | UI is observational; operations own state and devices |
| CUETools.App.Core (new since 2026-08-12) | Platform-neutral application core: verify, convert, queue, settings, encoder catalog, album art, calibration, and the full rip service stack plus their view models | `CUETools.App.Core`; consumed by `CUETools.Wpf` and by the external Linux head (LynxTWO/cuetools-linux pins this repo as a submodule) | no WPF or Windows-only APIs; platform behavior enters only through `Services/Platform` seams; WMA joins the output-verification evaluator by name-identity gate |
| Classic CUETools and CUEPlayer | Legacy desktop and CLI compatibility | `CUETools.sln`; classic release manifest | .NET Framework, C++/CLI, installer, and binary compatibility |
| Processor, CD image, ripper, and SCSI | Audio pipeline, secure reads, evidence, repair orchestration; Linux SG_IO transport behind the same SendCommand funnel (`Bwg.Scsi/LinuxSg.cs`); drive-identity-scoped quirk policies | `CUETools.Processor`; `CUETools.CDImage`; `CUETools.Ripper*`; `Bwg.Scsi` (neutral net8.0 flavors) | hardware, cache, transport, CTDB, AccurateRip; per-drive carve-outs bound to exact vendor/product/firmware |
| Codec plugins and external encoders | Managed, native, plugin, and process-backed transforms | codec projects; plugin discovery; release encoder manifests | ABI, process finalization, licensing, provenance |
| Metadata and artwork providers | Release selection, tags, artwork discovery and ranking | WPF services and tests; artwork plan | network, provider policy, credentials, bounded image input |
| Build, CI, mutation, and release | Deterministic tests, warning policy, mutation profiles, artifacts, signing, SBOMs | `eng/ci`; `eng/mutation`; `eng/release`; `.github/workflows` | toolchains, hosted runners, signing control plane |

## Rule Authority

| Contract | Authority | Guard |
|---|---|---|
| Repo operating and release policy | `CLAUDE.md` | CI and release policy tests |
| Test discovery and skip floors | `eng/ci/test-suites.json` | `eng/ci/Invoke-TestSuites.ps1` |
| Mutation scope and floors | `eng/mutation/profiles.json` | mutation harness contract and workflow |
| Release artifact membership | `eng/release/*.manifest.json` | ArtifactValidator and release tests |
| External encoder redistribution | `eng/release/external-command-encoders.json` | preparation, notices, runtime resolution, artifact validation |
| Native dependency identity | `eng/release/native-dependencies.json` | preparation and inventory tests |

## Freshness Triggers

Reopen the detailed map when project references, plugin copy/discovery rules, native loader names, entrypoints, session ownership, provider credentials, manifest membership, workflows, or external dependency inventories change.
