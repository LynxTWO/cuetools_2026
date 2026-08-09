# Windows release signing policy

CUETools has one enforceable Windows publisher-signing policy:
`eng/release/signing-policy.json`. A stable `v...` tag build and a manual build whose
`sign_release` input is selected must produce Authenticode-signed release
artifacts. Missing credentials, an unexpected certificate subject, an expired
certificate, a missing code-signing EKU, a failed RFC 3161 timestamp, or an
invalid post-signature artifact contract stops the release.

A normal manual dispatch is an evidence build. It deliberately receives no
signing key and emits `signing-status.json` with
`mode: unsigned-evaluation`; that artifact is not release-eligible.
Tags outside the stable `v...` namespace do not enter the credentialed release
workflow and may only be used for clearly labeled unsigned previews.

## Cryptographic and publication contract

- PE file digest: SHA-256.
- RFC 3161 timestamp digest: SHA-256.
- Timestamp authority: `http://timestamp.digicert.com`.
- Required certificate EKU: Code Signing (`1.3.6.1.5.5.7.3.3`).
- Verification: SignTool default Authenticode policy, every embedded
  signature, and a required timestamp (`verify /pa /all /tw`), followed by an
  independent PowerShell certificate/thumbprint/timestamp check.
- Key handling: the PFX is decoded only into a random temporary directory,
  imported non-exportably into the runner's CurrentUser certificate store,
  deleted before signing begins, and removed from the store in `finally`.
- Scope: the exact publisher-owned PE set selected from the classic and WPF
  artifact contracts. Hash-pinned upstream executables and Microsoft/runtime
  files are excluded so CUETools neither changes their reviewed bytes nor
  presents itself as their original publisher.
- Order: build and validate; sign; regenerate plugin hash manifests; revalidate
  both artifacts; then generate provenance and SBOMs from the final signed
  bytes.

The current contracts select 117 publisher-built executable, managed,
mixed-mode, native, and satellite files across both artifacts. Policy tests
fail if either profile loses its minimum coverage, selects a hash-pinned
upstream file, weakens a digest, omits plugin-manifest regeneration, or moves
signing after provenance/SBOM generation.

## Current hosted unsigned evidence

[Manual release run 30849431011](https://github.com/LynxTWO/cuetools_2026/actions/runs/30849431011)
completed successfully at commit
`b0d0864b6512e72d196ee373e37325fa77461318` with zero check-run annotations.
The downloaded status document reports:

```json
{
  "schema": "cuetools.signing-status.v1",
  "policyId": "cuetools-windows-authenticode-v1",
  "mode": "unsigned-evaluation",
  "productionRelease": false,
  "selectedFileCount": 117
}
```

This proves policy selection, honest unsigned labeling, and downstream
artifact/provenance/SBOM ordering on the hosted runner. It is not publisher
identity evidence and cannot substitute for the first production-signed tag.

## GitHub configuration

Create a GitHub environment named `release-signing`. Restrict it to release
tags, require an independent reviewer, prevent self-review, and disallow
administrative bypass where the repository plan supports those controls. Add:

- environment secret `CUETOOLS_SIGNING_PFX_BASE64`: base64 of the public-trust
  Windows code-signing PFX;
- environment secret `CUETOOLS_SIGNING_PFX_PASSWORD`: its password;
- environment variable `CUETOOLS_SIGNING_SUBJECT_PATTERN`: a narrow .NET
  regular expression matching the intended certificate subject.

Do not place the PFX, password, base64, subject value, or private-key metadata
in the repository. Rotate the certificate before expiration and immediately
after suspected disclosure. Revoke the affected certificate before issuing a
replacement after confirmed private-key compromise.

The 2026 SignPath Foundation application was not accepted at the fork's current
reputation level, so no approved public-trust signing identity is available.
Acquiring a future provider and configuring those protected values remains an
external prerequisite for a stable release. The repository does not allow that
missing prerequisite to degrade silently into an unsigned stable tag.

## Local and hosted checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File eng\release\Test-SigningPolicy.ps1

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File eng\release\Invoke-ArtifactSigning.ps1 -PlanOnly

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File eng\release\Test-ArtifactSigning.ps1
```

The first check needs only the repository. `-PlanOnly` additionally requires
both built artifact directories. The last check uses an ephemeral self-signed
publisher to exercise real signing, a live RFC 3161 timestamp, private-key
import/removal, and the required public-trust refusal; its SignTool trust error
is expected and the harness itself must return success. A production invocation
is owned by `release-windows.yml`; avoid passing secrets on an interactive
command line.
