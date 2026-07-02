# Decisions Needed

Items surfaced by the anti-dark-code passes that require a human decision before action. The autonomous loop parks them here instead of guessing. Each has enough evidence to decide without re-reading the code.

Vocabulary: `.claude/skills/anti-dark-code/references/00-conventions.md`.

## Open decisions

### D1. AccurateRip: switch lookups to HTTPS?

- **Evidence:** `www.accuraterip.com` answered HTTPS 200 in a 2026-07 probe. Client hardcodes `http://` at `CUETools.AccurateRip\AccurateRip.cs:829,1230`. Payloads are unsigned, so HTTPS is the only in-transit integrity available.
- **Why it needs you:** behavior change to a network path; small risk the server's TLS cert/redirects behave differently for the `/accuraterip/...` bin paths than for the probe.
- **Smallest safe step if yes:** change the two scheme literals, keep an http fallback on TLS failure, verify a real lookup still parses.
- **Risk if left:** low (works today), but rip verdicts stay tamperable in transit.

### D2. CTDB: HTTPS is blocked upstream

- **Evidence:** `db.cuetools.net` failed the TLS handshake in the same probe (`CUEToolsDB.cs:74` hardcodes http).
- **Why it needs you:** the fix is not in this repo; it needs the server operator (cuetools.net) to enable TLS. Decision: raise it upstream, or accept http.
- **Smallest safe step:** file an issue/ask upstream; revisit the client once the server answers TLS.

### D3. unrar.dll upgrade (possible CVE-2022-30333)

- **Evidence:** bundled `unrar.dll` is 6.11 (2022-05-28), which predates the 6.12 fix for CVE-2022-30333 (path traversal on extract). CVE match is inferred from version, not tested. `CUETools.Compression.Rar\Unrar.cs` wraps it.
- **Why it needs you:** replacing a vendored binary; also whether the RAR-input feature ever extracts to attacker-influenced paths needs confirming (tracked as an S4 item).
- **Smallest safe step:** drop in unrar 7.x DLL, re-test the "read from RAR without unpacking" flow.

### D4. SharpZipLib upgrade

- **Evidence:** `ICSharpCode.SharpZipLib.dll` 0.85.5 dates to 2010; live in `CUETools.Compression.Zip`. Predates every modern SharpZipLib security fix.
- **Why it needs you:** dependency replacement; modern SharpZipLib is a NuGet package with API changes.
- **Smallest safe step:** replace the vendored DLL with the current NuGet package, adapt the Zip wrapper, re-test.

### D5. Delete dead projects/binaries (already deferred by you)

- **Evidence:** FlaCuda projects are absent from the solution; `CUDA.NET.dll`, `Freedb.dll`, `MusicBrainz.dll` are referenced by no csproj. You asked to keep them until the initial rollout completes.
- **Why it needs you:** deletion. Revisit when the rollout is done.

### D6. MusicBrainz client replacement

- **Evidence:** `MusicBrainz/` is a mirror of the abandoned musicbrainz-sharp (v1.1-era XML API). MusicBrainz has moved to a JSON API.
- **Why it needs you:** swapping a metadata provider is a feature-level change, not a safe comment/cleanup.
- **Smallest safe step:** scope a replacement against the current MusicBrainz web service + Cover Art Archive (idea 10 in the modernization list).

## Resolved / actioned by the loop

(Autonomous passes append here as they close items.)
