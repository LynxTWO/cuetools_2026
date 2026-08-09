# Code-signing policy

Effective: 2026-07-30. Status updated: 2026-08-08.

This policy applies to Windows releases published from
`LynxTWO/cuetools_2026`. It complements the machine-enforced policy in
`eng/release/signing-policy.json` and the implementation detail in
`docs/security/release-signing.md`.

## Current status

SignPath Foundation did not accept the project's 2026 application. Its feedback
cited the new fork's current project reputation and star count. This is an
eligibility outcome, not evidence of a signed publisher identity. No artifact is
claimed to be SignPath-signed. Until an approved public-trust signing provider is
configured and independently verified, public evaluation builds are GitHub
prereleases whose title, notes, and filenames say **unsigned preview**.

If a future SignPath application is accepted, production release pages will
include the required attribution:

> Free code signing provided by [SignPath.io](https://about.signpath.io/),
> certificate by [SignPath Foundation](https://signpath.org/)

## Release classes

- Stable `v...` tags are production-signing boundaries. Their workflow requires
  an approved code-signing credential, SHA-256 Authenticode signatures, RFC
  3161 timestamps, post-signature verification, regenerated plugin manifests,
  artifact-contract validation, provenance, and SBOMs. A missing or invalid
  signature stops publication.
- Preview tags do not enter the credentialed stable-release workflow. A preview
  may be published only as an explicitly unsigned GitHub prerelease and is not
  presented as production-trusted software.
- Manual release-workflow runs without signing are evidence builds. Their
  `unsigned-evaluation` status makes them ineligible for stable publication.

## Source, review, and approval

GitHub is the canonical source and issue history. Changes from contributors who
do not have commit access are accepted through pull requests and reviewed by a
maintainer. GitHub Actions rebuilds the source with pinned actions and
dependencies, executes the checked warning/test/release gates, and records the
source revision.

`LynxTWO` is currently the project owner, release manager, and primary
committer. A production signing request is a separate manual approval after
source review and successful hosted gates. Additional maintainers and signing
approvers must be documented here before receiving those roles. The project
will not invent an independent reviewer where none exists. A future signing
provider may require another approver or upstream participation before accepting
the project.

Signing keys are not committed to the repository, placed in release archives,
or made available to ordinary build steps. Under any future managed-signing
configuration, the provider controls the certificate and private-key operation
and applies the approved policy to a reproducible build.

## Signing scope

Only publisher-owned PE files selected by the reviewed release contract are
eligible for the CUETools publisher signature. Hash-pinned Microsoft/runtime
files and externally signed or upstream-owned third-party binaries are excluded
so CUETools does not impersonate their publisher or alter reviewed bytes.

The release includes exact file manifests, native-dependency evidence,
third-party notices, SHA-256 provenance, and CycloneDX/SPDX SBOMs. UnRAR's
freeware license and the retained legacy HDCD binary's incomplete source/build
provenance are disclosed. They may require exclusion from a
Foundation-eligible signing profile even though their redistribution status is
documented for the general package.

## Compromise or policy failure

A suspected key, workflow, or artifact compromise halts stable publication.
Maintainers will preserve evidence, remove affected downloads where necessary,
notify the certificate/signing provider, request revocation when appropriate,
publish the affected versions and hashes, correct the build boundary, and
require a clean rebuild before resuming releases.
