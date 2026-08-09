# CUETools 2026

CUETools 2026 is an independently maintained Windows fork of
[CUETools](https://github.com/gchudov/cuetools.net) focused on trustworthy CD
ripping, verification and repair, lossless conversion, and auditable release
engineering. It preserves disc layout, gaps, CUE-sheet semantics, metadata, and
audio while making the evidence behind a “verified” result visible.

This repository contains both the modern, self-contained **CUETools 2026**
desktop application and the classic CUETools/CUERipper applications. It is not
the official upstream repository or website.

## Downloads and release trust

Public builds for this fork are published on the
[GitHub Releases page](https://github.com/LynxTWO/cuetools_2026/releases).
Prereleases are evaluation builds and are explicitly labeled **unsigned**.
Stable `v...` tags are production-signing boundaries: the release workflow
fails closed if its approved signing provider is unavailable or any required
signature cannot be validated and timestamped.

Read the [code-signing policy](CODE_SIGNING_POLICY.md), [privacy policy](PRIVACY.md),
and the detailed [release-signing implementation](docs/security/release-signing.md)
before redistributing a build. SignPath Foundation did not accept the project's
2026 application because this new fork did not yet meet its project-reputation
threshold. No SignPath signature is claimed. Until an approved public-trust
signing provider is configured and independently verified, this fork publishes
only clearly labeled unsigned previews; stable tags continue to fail closed.

## Capabilities

- Secure and paranoid optical-disc reads, Test & Copy, AccurateRip and CTDB
  verification, and source-preserving CTDB repair.
- Track or single-image output with embedded CUE support.
- WAV, FLAC, APE, ALAC, TTA, WavPack, WMA, MP3, AAC-family, Opus, Vorbis,
  Musepack, OptimFROG, and external-encoder integration, subject to the codec
  and platform documented by each release.
- MusicBrainz and Cover Art Archive discovery, optional TheAudioDB integration,
  local drag-and-drop artwork, bounded image decoding, and reproducible JPEG
  preparation.
- Exact release manifests, native-dependency provenance, SPDX and CycloneDX
  SBOMs, warning budgets, and release evidence.

Audio processed through the classic conversion engine is normally CD PCM
(16-bit, 44.1 kHz stereo). Format availability and verification guarantees are
reported by the active encoder rather than inferred from a filename.

## Privacy and network use

The distributed applications do not contain a first-party analytics or
automatic crash-reporting client. Verification and metadata features contact
external services and some legacy services still use plaintext HTTP. CTDB
contribution is off by default and requires an explicit preference; its prompt
discloses the data and transport before submission. See [PRIVACY.md](PRIVACY.md)
for the complete data map and controls.

## Building

Clone this fork and initialize its submodules:

```powershell
git clone https://github.com/LynxTWO/cuetools_2026.git
cd cuetools_2026
git submodule update --init --recursive
powershell -NoProfile -ExecutionPolicy Bypass -File eng/ci/Prepare-VendorSources.ps1
```

Build with Visual Studio 2022 or newer. The full classic solution requires the
.NET Framework 4.7 targeting pack, .NET desktop development, v143 C++ tools,
C++/CLI support, a Windows SDK, and Microsoft Visual Studio Installer Projects.
The modern application requires the .NET 8 SDK. Open `CUETools.sln` for the
classic solution or use the checked scripts under `eng/ci` and `eng/release` for
the same gated paths used by GitHub Actions.

Third-party codec packages should be enrolled with the release archive's
`Install-CUEToolsPlugin.ps1`; see
[Installing a user plugin](docs/plugin-installation.md). External executables
remain user-controlled and are preferred when the user explicitly imports and
approves them.

## Uninstalling

For a portable ZIP, close CUETools and delete the extracted application folder.
To remove per-user state as well, delete `%AppData%\CUETools2026` after reviewing
or exporting any settings, verification history, calibration, logs, and imported
encoders you want to keep. Classic profiles use `%AppData%\CUE Tools` and
`%AppData%\CUERipper`; CUEPlayer has separate settings and playlists. An
installer-based build can be removed through Windows **Installed apps**.

## License and upstream

First-party CUETools code is distributed under GPL-2.0-or-later. Bundled
third-party components retain their own licenses and notices; consult
[License.txt](License.txt), each release's `THIRD-PARTY-NOTICES`, SBOMs, and
native-dependency evidence. CUETools was created and is maintained upstream by
Grigory Chudov and its contributors; this fork's changes do not imply upstream
endorsement.
