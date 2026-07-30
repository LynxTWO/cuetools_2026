# Privacy policy

Effective: 2026-07-30

This policy covers the applications and release artifacts published from
`LynxTWO/cuetools_2026`, including CUETools 2026, classic CUETools, CUERipper,
and CUEPlayer. This independently maintained fork does not operate the external
verification, metadata, artwork, or streaming services described below.

## Summary

CUETools does not include a first-party advertising, behavioral-analytics, or
automatic crash-reporting client. Audio files and raw sector data are processed
locally unless the user deliberately starts an Icecast stream. Verification,
repair, metadata, artwork, update-message, and optional contribution features
make network requests needed to provide those features.

Local diagnostic logs are never uploaded automatically. A user decides whether
to share a log, report, CUE sheet, rip log, or audio file.

## Data stored on the computer

CUETools 2026 stores its profile under `%AppData%\CUETools2026`, including:

- `settings.txt`: preferences, output and naming configuration, selected
  codecs, and protected credential blobs;
- `verify-history.json.gz`: disc identifiers, per-track checksums, verification
  confidence, drive/read information, output assurance, titles and artists, and
  timestamps used to compare repeat reads;
- `drive-calibration.json`: drive identity and measured offset, cache, overread,
  and speed behavior;
- `logs\cuetools-*.log`: local operational and error diagnostics;
- `encoders\`: external encoder executables that the user explicitly imports,
  plus local approval records tied to their hashes.

Classic applications store separate profiles under the Windows roaming
application-data folders used by `CUE Tools` and `CUERipper`. CUEPlayer also
stores its own settings and playlists. Output folders may contain audio, CUE,
TOC, checksums, artwork, rip/verification logs, repair receipts, and release
evidence selected by the user.

Proxy, TheAudioDB, and Icecast secrets are protected for the current Windows
user with Windows DPAPI where supported. DPAPI is protection at rest, not a
defense against software already running as that user. There is no plaintext
fallback on unsupported DPAPI platforms.

Deleting these application-data folders and user-selected output removes the
local records. Close every CUETools process before deleting an active profile.

## Network services and data sent

The exact requests depend on enabled features:

- **AccurateRip (HTTPS):** disc layout identifiers are used to download
  checksum data and the drive-offset database.
- **CUETools Database / CTDB (HTTP):** verification, repair, and metadata
  queries send disc layout/identifiers and a user agent that may contain the
  operating-system and drive description. The service does not currently offer
  a usable TLS endpoint known to this client, so these requests are not
  encrypted in transit.
- **CTDB contribution (HTTP, off by default):** if the user enables
  `Submit to CTDB`, CUETools may send disc layout, whole-disc and track
  checksums, recovery parity/syndrome data, confidence and read quality, drive
  name, barcode, artist, title, and a pseudonymous device identifier derived by
  hashing local machine identifiers. CUERipper asks before submission when
  `Ask before submitting` is enabled. Ripping, verifying, or repairing a disc
  does not itself enable contribution.
- **gnudb/freedb-compatible metadata (HTTP):** disc layout identifiers and
  metadata queries are sent when that legacy provider is used. A manual
  metadata submission can include artist, album, year, genre, track titles,
  extended fields, disc ID, and the configured freedb user/domain values.
- **MusicBrainz and Cover Art Archive (HTTPS):** disc ID or fuzzy disc layout,
  MusicBrainz release/release-group identifiers, and artwork requests are sent
  for metadata and cover discovery.
- **TheAudioDB (HTTPS, disabled until configured):** the user-provided API key
  appears in the API request path. Searches may include MusicBrainz identifiers
  or artist and album text. The key is not a CUETools account password.
- **Project message of the day (HTTPS):** the classic application may request a
  bounded text message.
- **Icecast (user-configured):** CUEPlayer sends the selected audio stream,
  track metadata, and source credentials to the server chosen by the user.
  HTTPS is the default. Plain HTTP requires an explicit opt-in warning.
- **External codec and metadata links:** when the user chooses a website or
  download link, CUETools opens it in the system browser; normal browser and
  website privacy rules then apply.

The operators of those services receive normal connection information such as
the source IP address and may retain data under their own policies. CUETools
does not control their retention, access, or deletion practices.

## Diagnostics and telemetry

The local CUETools 2026 diagnostic log records runtime version, phases, counts,
timings, drive/rip structure, verification state, paths after redaction, and
exception details. Drive models, disc identifiers, file paths, and music
metadata can still be identifying when a user shares a log. Review diagnostic
material before posting it publicly.

The repository's GitHub Actions workflows opt out of .NET CLI and Microsoft
Testing Platform telemetry. Those build-time tools are not shipped as a
CUETools runtime telemetry client.

## Changes and questions

Material privacy changes will update this file and its effective date. Public,
non-sensitive questions can be opened in the repository's
[issue tracker](https://github.com/LynxTWO/cuetools_2026/issues). Do not post
passwords, API keys, protected credential blobs, private file paths, or
unredacted diagnostics in a public issue.
