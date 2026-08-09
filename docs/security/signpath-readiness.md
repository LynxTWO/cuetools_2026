# SignPath Foundation readiness

Current-state review: 2026-08-08.

This is an evidence ledger, not a claim that SignPath Foundation accepted
CUETools 2026. The 2026 application was declined because this new fork did not
yet meet the program's project-reputation threshold. No release is represented
as Foundation-signed.

## Readiness matrix

| Condition | Evidence | State |
| --- | --- | --- |
| Public source and maintained build | Public GitHub fork, pinned Actions, source receipts, exact artifact manifests, warning gates, test gates, provenance, and SBOM generation | ready |
| Released in the form to be signed | The first two public fork releases are explicitly unsigned Windows prereleases built by the checked release path with source receipts and SBOMs | ready as unsigned evidence; not signing eligibility |
| Functionality documented | Root README identifies this maintained fork, applications, capabilities, downloads, upstream, and build path | ready |
| Privacy policy and user control | Root privacy policy maps local data and every inspected network service; CTDB contribution now defaults off and is consent-gated | ready for portable distribution |
| Code signing policy | Root policy defines roles, stable/preview boundaries, scope, approval, and incident response and includes the required attribution | ready |
| Manual approval and protected signing | Stable tags fail closed without signing; the GitHub environment exists, but no approved public-trust provider or credential is configured | external/blocked |
| OSI-approved licensing for all components | First-party code is GPL-2.0-or-later and most dependencies are open source; the package also contains UnRAR freeware and a legacy HDCD decoder under non-OSI custom terms | blocked for the current complete package |
| Modified-fork rule | This repository visibly uses GitHub's fork relationship | partial |
| Upstream normally publishes signed builds | The audited upstream v2.2.6 Windows ZIP had 81 unsigned PE files. Its four valid signatures belonged to Microsoft/Json.NET/WinRAR third parties, not upstream CUETools binaries | blocked unless SignPath grants an exception or upstream begins signing |
| Verifiable reputation | Upstream CUETools has long-standing public usage and community documentation; this fork is new and must distinguish upstream reputation from its own | application declined at current fork reputation/star level |
| Consistent signed-file metadata | Current combined classic/modern products intentionally use multiple product identities and versions | a SignPath artifact profile must be narrowed or metadata normalized |

## Package decision

The existing complete package must not be represented as Foundation-eligible.
UnRAR and the retained legacy HDCD binaries are useful, documented, hash-bound
components, but their licenses do not satisfy SignPath's stated OSI-only rule.
A future Foundation profile should either:

1. exclude those components while keeping them available as separately
   installed optional plugins;
2. replace them with behavior-compatible, source-built, OSI-licensed
   implementations; or
3. obtain an explicit written eligibility decision from SignPath.

Excluding a binary from the publisher-signature selection alone does not resolve
the broader “no proprietary component” condition for a signed package.

## Conditions before any future application

- Establish enough independently verifiable adoption and project reputation to
  satisfy the program's current eligibility threshold.
- Recheck the current program rules rather than assuming the 2026 criteria are
  unchanged.
- Confirm whether a future eligible artifact must omit UnRAR and the legacy HDCD
  plugin or may publish them separately as optional, unsigned plugins.
- Confirm reviewer and signing-approver requirements for a single-maintainer
  project before supplying any signing credential.

Until a public-trust provider accepts the project and the resulting signature is
independently verified, only clearly labeled unsigned previews may be published.
