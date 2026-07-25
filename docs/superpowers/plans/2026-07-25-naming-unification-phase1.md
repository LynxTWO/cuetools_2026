# Naming Unification (MusicBrainz buildout Phase 1) - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the rich WPF `NamingEngine` the single authority for real rip AND convert output paths, fed by a `CUEMetadata -> NamingContext` mapper, so the Naming page preview equals what is written to disk and the encode-path bug is fixed at its root (the per-rip `EngineTrackFilenameFormat` stopgap is removed).

**Architecture:** For an encode, the rip/convert layer renders each track's relative path with `NamingEngine.Render`, splits the shared album directory from the per-track remainder, creates the directory tree, and hands the CUETools engine explicit per-track output names through a small new hook - so `CUESheet.Go` writes exactly where `NamingEngine` says instead of re-deriving names from `_config.trackFilenameFormat`.

**Tech Stack:** .NET 8, WPF, MSTest v2. Build via `CUETools.Wpf/CUETools.Wpf.csproj` (the ripper `.csproj` alone fails on the Bwg.Scsi net20 ResGen quirk). Pure unit/fuzz tests in the existing `CUETools.Wpf.Tests` project.

## Global Constraints

- ASCII only in code, comments, and UI copy: no em/en dashes or typographic Unicode. Use " - ", "->", "~".
- Do not regress existing rips. Every field degrades cleanly: an absent token drops from the path (NamingEngine already omits empty segments).
- The Naming page preview MUST equal real output (both use `NamingEngine.Render`).
- Multi-disc sets get a `Disc N[ - Subtitle]/` subfolder (via the existing `NamingEngine.DiscFolder`); the rip/convert layer must create those directories before `Go()` writes.
- Per-track files only (matches current output). No single-image work here.
- Phase 1 uses ONLY metadata `CUEMetadata` already holds. Type/status tokens render empty until phase 3.
- Commit per task. Push is a separate owner action (do not push).
- Build the WPF project after each code task.

## File Structure

- `CUETools.Wpf/Services/NamingEngine.cs` (modify): add `NamingContext` fields (`Label`, `Catalog`, `Barcode`, `Country`, `Genre`, `OriginalYear`, per-track `Isrc`) and the new tokens (`%label%`, `%catalog%`, `%barcode%`, `%country%`, `%genre%`, `%originalyear%`, `%isrc%`, plus derived `%releasetype%` / `%releasestatus%`), and list them in `PaletteFields`.
- `CUETools.Wpf/Services/NamingPaths.cs` (new): pure `Split(...)` that separates the shared album directory from the per-track remainders.
- `CUETools.Wpf/Services/NamingContextMapper.cs` (new): pure `FromMetadata(CUEMetadata, trackIndex, totalTracks) -> NamingContext`.
- `CUETools.Processor/CUESheet.cs` (modify): a `SetExplicitTrackNames(IList<string>)` hook that `GenerateFilenames` honors (append extension, dot-safe), bypassing `trackFilenameFormat`.
- `CUETools.Wpf/Services/RipService.cs` (modify): route encode naming through the new path; remove the `EngineTrackFilenameFormat` stopgap.
- `CUETools.Wpf/Services/ConvertService.cs` (modify): same routing.
- `CUETools.Wpf/ViewModels/NamingViewModel.cs` (modify): drop the now-dead `_config.trackFilenameFormat = _scheme.Template` push.
- `CUETools.Wpf.Tests/` (new test files): tokens, split, mapper (unit + fuzz).

---

### Task 1: New NamingContext fields and tokens

**Files:**
- Modify: `CUETools.Wpf/Services/NamingEngine.cs` (`NamingContext` 12-27, `PaletteFields` 66-70, `BuildVars` 89-114)
- Test: `CUETools.Wpf.Tests/NamingTokenTests.cs` (create)

**Interfaces:**
- Produces: `NamingContext` gains `public string Label = ""; public string Catalog = ""; public string Barcode = ""; public string Country = ""; public string Genre = ""; public string OriginalYear = ""; public string Isrc = "";`. New template tokens `%label%`, `%catalog%`, `%barcode%`, `%country%`, `%genre%`, `%originalyear%`, `%isrc%`, `%releasetype%`, `%releasestatus%` render from a `NamingContext`.

- [ ] **Step 1: Write the failing test**

Create `CUETools.Wpf.Tests/NamingTokenTests.cs`:

```csharp
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class NamingTokenTests
    {
        private static NamingScheme Tpl(string t) => new NamingScheme { Template = t, ReleaseDescriptor = false };

        [TestMethod]
        public void NewTokens_RenderFromContext()
        {
            var c = new NamingContext
            {
                AlbumArtist = "A", Artist = "A", Album = "Alb", Title = "T", TrackNumber = 1,
                Label = "Sub Pop", Catalog = "SP-123", Barcode = "0987654321",
                Country = "US", Genre = "Rock", OriginalYear = "1991", Isrc = "USAB11700001",
            };
            Assert.AreEqual("Sub Pop", NamingEngine.Render(c, Tpl("%label%")));
            Assert.AreEqual("SP-123", NamingEngine.Render(c, Tpl("%catalog%")));
            Assert.AreEqual("0987654321", NamingEngine.Render(c, Tpl("%barcode%")));
            Assert.AreEqual("US", NamingEngine.Render(c, Tpl("%country%")));
            Assert.AreEqual("Rock", NamingEngine.Render(c, Tpl("%genre%")));
            Assert.AreEqual("1991", NamingEngine.Render(c, Tpl("%originalyear%")));
            Assert.AreEqual("USAB11700001", NamingEngine.Render(c, Tpl("%isrc%")));
        }

        [TestMethod]
        public void TypeStatusTokens_EmptyWhenUnset()
        {
            // phase 1: the mapper leaves these blank, so the tokens render empty (no literal token left)
            var c = new NamingContext { Album = "Alb", Title = "T", TrackNumber = 1, PrimaryType = "", ReleaseStatus = "" };
            Assert.AreEqual("", NamingEngine.Render(c, Tpl("%releasetype%")));
            Assert.AreEqual("", NamingEngine.Render(c, Tpl("%releasestatus%")));
        }

        [TestMethod]
        public void ReleaseType_DerivesFromPrimaryAndSecondary()
        {
            var c = new NamingContext { Album = "Alb", Title = "T", TrackNumber = 1,
                PrimaryType = "album", SecondaryTypes = new[] { "live" } };
            Assert.AreEqual("Live Album", NamingEngine.Render(c, Tpl("%releasetype%")));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug -v q -nologo`
Expected: FAIL to compile - `NamingContext` has no `Label`/`Catalog`/etc.

- [ ] **Step 3: Implement**

In `CUETools.Wpf/Services/NamingEngine.cs`, add fields to `NamingContext` (after `ReleaseStatus`, line 26):

```csharp
    public string Label = "";
    public string Catalog = "";        // catalog number (NOT the barcode)
    public string Barcode = "";
    public string Country = "";
    public string Genre = "";
    public string OriginalYear = "";   // release-group first-release year (falls back to Year)
    public string Isrc = "";           // per-track ISRC
```

Add to `PaletteFields` (line 66-70), extending the array:

```csharp
    public static readonly string[] PaletteFields =
    {
        "%albumartist%", "%artist%", "%album%", "%title%", "%tracknumber%", "%year%",
        "%disc%", "%discnumber%", "%totaldiscs%", "%discsubtitle%", "%releasedescriptor%", "%featsuffix%",
        "%label%", "%catalog%", "%barcode%", "%country%", "%genre%", "%originalyear%", "%isrc%",
        "%releasetype%", "%releasestatus%",
    };
```

Add the token values to the dictionary returned by `BuildVars` (after `"featsuffix"`, line 112), plus a small `ReleaseType` helper:

```csharp
            ["label"] = Normalize(c.Label ?? "", s, 80, swapArticles: false),
            ["catalog"] = Normalize(c.Catalog ?? "", s, 40, swapArticles: false),
            ["barcode"] = Normalize(c.Barcode ?? "", s, 40, swapArticles: false),
            ["country"] = Normalize(c.Country ?? "", s, 40, swapArticles: false),
            ["genre"] = Normalize(c.Genre ?? "", s, 40, swapArticles: false),
            ["originalyear"] = (c.OriginalYear ?? "").Length >= 4 ? c.OriginalYear.Substring(0, 4)
                                : (c.Year ?? "").Length >= 4 ? c.Year.Substring(0, 4) : "",
            ["isrc"] = Normalize(c.Isrc ?? "", s, 40, swapArticles: false),
            ["releasetype"] = ReleaseTypeText(c),
            ["releasestatus"] = TitleCase(c.ReleaseStatus ?? ""),
```

Add these helpers to the class:

```csharp
    // "Live Album", "Compilation Album", or just "Album"/"EP"/"Single" - empty when no primary type set
    private static string ReleaseTypeText(NamingContext c)
    {
        string primary = TitleCase(c.PrimaryType ?? "");
        if (primary.Length == 0) return "";
        var sec = c.SecondaryTypes ?? System.Array.Empty<string>();
        foreach (var t in sec)
        {
            string s = (t ?? "").ToLowerInvariant();
            if (s == "live") return "Live " + primary;
            if (s == "compilation") return "Compilation " + primary;
            if (s == "soundtrack") return "Soundtrack";
            if (s == "remix") return "Remix " + primary;
            if (s == "demo") return "Demo " + primary;
        }
        return primary;
    }

    private static string TitleCase(string v)
    {
        v = (v ?? "").Trim();
        if (v.Length == 0) return "";
        if (v == "ep") return "EP";
        return char.ToUpperInvariant(v[0]) + v.Substring(1);
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug --nologo`
Expected: PASS (the 3 new tests plus all existing tests).

- [ ] **Step 5: Commit**

```bash
git add CUETools.Wpf/Services/NamingEngine.cs CUETools.Wpf.Tests/NamingTokenTests.cs
git commit -m "feat(naming): add label/catalog/barcode/country/genre/originalyear/isrc + type/status tokens"
```

---

### Task 2: NamingPaths.Split (shared album dir vs per-track remainder)

**Files:**
- Create: `CUETools.Wpf/Services/NamingPaths.cs`
- Test: `CUETools.Wpf.Tests/NamingPathsTests.cs` (create)

**Interfaces:**
- Produces: `static (string commonDir, string[] remainders) NamingPaths.Split(IReadOnlyList<string> relPaths)`. `relPaths` are `'/'`-separated relative paths without extension (from `NamingEngine.Render`). `commonDir` is the longest shared leading directory (the album folder); each remainder is that track's path with `commonDir` removed. Single-disc: all tracks share the album folder, remainders are bare filenames. Multi-disc: `commonDir` is the album folder, remainders keep the `Disc N/` segment.

Semantics: split each path into segments on `'/'`; the last segment is the filename, the rest is its directory. `commonDir` = the longest common leading run of directory segments across all paths (joined with `'/'`). Each remainder = the path's segments after `commonDir` (joined with `'/'`). If there is only one path, `commonDir` is its directory and the remainder is its filename. Empty input -> `("", [])`.

- [ ] **Step 1: Write the failing tests**

Create `CUETools.Wpf.Tests/NamingPathsTests.cs`:

```csharp
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class NamingPathsTests
    {
        [TestMethod]
        public void SingleDisc_CommonDirIsAlbum_RemaindersAreFilenames()
        {
            var (dir, rem) = NamingPaths.Split(new[] { "Artist - Album/01 - A", "Artist - Album/02 - B" });
            Assert.AreEqual("Artist - Album", dir);
            CollectionAssert.AreEqual(new[] { "01 - A", "02 - B" }, rem);
        }

        [TestMethod]
        public void MultiDisc_CommonDirIsAlbum_RemaindersKeepDiscFolder()
        {
            var (dir, rem) = NamingPaths.Split(new[] { "Alb/Disc 1/01 - A", "Alb/Disc 2/01 - B" });
            Assert.AreEqual("Alb", dir);
            CollectionAssert.AreEqual(new[] { "Disc 1/01 - A", "Disc 2/01 - B" }, rem);
        }

        [TestMethod]
        public void SingleTrack_DirIsAlbum_RemainderIsFilename()
        {
            var (dir, rem) = NamingPaths.Split(new[] { "Alb/01 - Only" });
            Assert.AreEqual("Alb", dir);
            CollectionAssert.AreEqual(new[] { "01 - Only" }, rem);
        }

        [TestMethod]
        public void NoCommonAlbumFolder_CommonDirEmpty()
        {
            var (dir, rem) = NamingPaths.Split(new[] { "A/01", "B/02" });
            Assert.AreEqual("", dir);
            CollectionAssert.AreEqual(new[] { "A/01", "B/02" }, rem);
        }

        [TestMethod]
        public void Empty_ReturnsEmpty()
        {
            var (dir, rem) = NamingPaths.Split(System.Array.Empty<string>());
            Assert.AreEqual("", dir);
            Assert.AreEqual(0, rem.Length);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug -v q -nologo`
Expected: FAIL to compile - `NamingPaths` does not exist.

- [ ] **Step 3: Implement**

Create `CUETools.Wpf/Services/NamingPaths.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace CUETools.Wpf.Services
{
    /// <summary>Splits NamingEngine relative track paths into the shared album directory (where the
    /// .cue/.log/cover go) and each track's remainder under it (the per-track name the engine writes,
    /// which keeps a "Disc N/" segment for multi-disc sets). Pure - no I/O.</summary>
    public static class NamingPaths
    {
        public static (string commonDir, string[] remainders) Split(IReadOnlyList<string> relPaths)
        {
            if (relPaths == null || relPaths.Count == 0) return ("", Array.Empty<string>());

            // directory segments of each path (everything before the last '/')
            var dirs = new List<string[]>();
            var full = new List<string[]>();
            foreach (var p in relPaths)
            {
                var segs = (p ?? "").Split('/');
                full.Add(segs);
                var d = new string[Math.Max(0, segs.Length - 1)];
                Array.Copy(segs, d, d.Length);
                dirs.Add(d);
            }

            // longest common leading run of directory segments
            int common = int.MaxValue;
            foreach (var d in dirs) common = Math.Min(common, d.Length);
            int shared = 0;
            for (int i = 0; i < common; i++)
            {
                string seg = dirs[0][i];
                bool all = true;
                foreach (var d in dirs) if (d[i] != seg) { all = false; break; }
                if (!all) break;
                shared++;
            }

            string commonDir = shared > 0 ? string.Join("/", dirs[0], 0, shared) : "";
            var remainders = new string[full.Count];
            for (int i = 0; i < full.Count; i++)
                remainders[i] = string.Join("/", full[i], shared, full[i].Length - shared);
            return (commonDir, remainders);
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug --nologo`
Expected: PASS.

- [ ] **Step 5: Add a fuzz test**

Append to `CUETools.Wpf.Tests/NamingPathsTests.cs` (inside the class):

```csharp
        [TestMethod]
        public void Fuzz_RecombinesToOriginal()
        {
            var rnd = new System.Random(20260725);
            for (int it = 0; it < 3000; it++)
            {
                int n = 1 + rnd.Next(12);
                var paths = new string[n];
                for (int i = 0; i < n; i++)
                {
                    int depth = rnd.Next(4);
                    var segs = new System.Collections.Generic.List<string>();
                    for (int d = 0; d < depth; d++) segs.Add("d" + rnd.Next(3));
                    segs.Add("f" + i);
                    paths[i] = string.Join("/", segs);
                }
                var (dir, rem) = NamingPaths.Split(paths);
                for (int i = 0; i < n; i++)
                {
                    string recombined = dir.Length > 0 ? dir + "/" + rem[i] : rem[i];
                    Assert.AreEqual(paths[i], recombined, "split must recombine to the original path");
                }
            }
        }
```

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug --nologo`
Expected: PASS (`commonDir + "/" + remainder` always rebuilds the input).

- [ ] **Step 6: Commit**

```bash
git add CUETools.Wpf/Services/NamingPaths.cs CUETools.Wpf.Tests/NamingPathsTests.cs
git commit -m "feat(naming): NamingPaths.Split - shared album dir vs per-track remainder (+ fuzz)"
```

---

### Task 3: CUEMetadata -> NamingContext mapper

**Files:**
- Create: `CUETools.Wpf/Services/NamingContextMapper.cs`
- Test: `CUETools.Wpf.Tests/NamingContextMapperTests.cs` (create)

**Interfaces:**
- Consumes: `CUETools.Processor.CUEMetadata` (fields `Artist`, `Title`, `Year`, `Genre`, `DiscNumber`, `TotalDiscs`, `DiscName`, `Barcode`, `Label`, `LabelNo`, `Country`, `ReleaseDate`; per-track `Tracks[i].Artist`, `Tracks[i].Title`, `Tracks[i].ISRC`), `NamingContext`.
- Produces: `static NamingContext NamingContextMapper.FromMetadata(CUEMetadata m, int trackIndex, int totalTracks)`.

Mapping: `AlbumArtist`/`Artist` <- album `m.Artist`; per-track `Artist` <- `m.Tracks[i].Artist` (falling back to `m.Artist`); `Album` <- `m.Title`; `Title` <- `m.Tracks[i].Title`; `Year` <- `m.Year`; `Genre` <- `m.Genre`; `DiscNumber`/`TotalDiscs` <- parsed `m.DiscNumber`/`m.TotalDiscs` (default 1); `DiscSubtitle` <- `m.DiscName`; `Label` <- `m.Label`; `Catalog` <- `m.LabelNo`; `Barcode` <- `m.Barcode`; `Country` <- `m.Country`; `Isrc` <- `m.Tracks[i].ISRC`; `OriginalYear` <- `m.ReleaseDate` year if present else `m.Year`; `TrackNumber` <- `trackIndex + 1`; `TotalTracks` <- `totalTracks`. Phase-3 fields (`PrimaryType`, `SecondaryTypes`, `ReleaseStatus`) are set to empty/none - no data yet.

- [ ] **Step 1: Write the failing tests**

Create `CUETools.Wpf.Tests/NamingContextMapperTests.cs`:

```csharp
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class NamingContextMapperTests
    {
        private static CUEMetadata TwoTrack()
        {
            var m = new CUEMetadata("id", 2)
            {
                Artist = "Genesis", Title = "Calling All Stations", Year = "1997", Genre = "Rock",
                DiscNumber = "1", TotalDiscs = "1", Barcode = "724385591020",
                Label = "Virgin", LabelNo = "CDV 2850", Country = "GB",
            };
            m.Tracks[0].Title = "Calling All Stations"; m.Tracks[0].Artist = "Genesis"; m.Tracks[0].ISRC = "GBAAA9700001";
            m.Tracks[1].Title = "Congo"; m.Tracks[1].Artist = "Genesis";
            return m;
        }

        [TestMethod]
        public void MapsExistingFields()
        {
            var c = NamingContextMapper.FromMetadata(TwoTrack(), 0, 2);
            Assert.AreEqual("Genesis", c.AlbumArtist);
            Assert.AreEqual("Calling All Stations", c.Album);
            Assert.AreEqual("Calling All Stations", c.Title);
            Assert.AreEqual("1997", c.Year);
            Assert.AreEqual("Rock", c.Genre);
            Assert.AreEqual("Virgin", c.Label);
            Assert.AreEqual("CDV 2850", c.Catalog);       // from LabelNo, not Barcode
            Assert.AreEqual("724385591020", c.Barcode);
            Assert.AreEqual("GB", c.Country);
            Assert.AreEqual("GBAAA9700001", c.Isrc);
            Assert.AreEqual(1, c.TrackNumber);
            Assert.AreEqual(2, c.TotalTracks);
        }

        [TestMethod]
        public void PhaseThreeFieldsAreEmpty()
        {
            var c = NamingContextMapper.FromMetadata(TwoTrack(), 1, 2);
            Assert.AreEqual("", c.PrimaryType);
            Assert.AreEqual("", c.ReleaseStatus);
            Assert.AreEqual(0, c.SecondaryTypes.Count);
            Assert.AreEqual("Congo", c.Title);
        }
    }
}
```

(If the `CUEMetadata` constructor signature differs from `new CUEMetadata("id", 2)`, adjust the test's construction to match the real ctor - inspect `CUEMetadata.cs` - but keep the asserted field values.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug -v q -nologo`
Expected: FAIL - `NamingContextMapper` does not exist.

- [ ] **Step 3: Implement**

Create `CUETools.Wpf/Services/NamingContextMapper.cs`. Inspect `CUEMetadata` for the exact property names/types first (DiscNumber/TotalDiscs are strings; ReleaseDate is a string). Implement:

```csharp
using System;
using CUETools.Processor;

namespace CUETools.Wpf.Services
{
    /// <summary>Builds a NamingContext for one track from the shared CUEMetadata release, using only
    /// fields the model already holds. Type/status (phase 3) are left empty so their tokens render
    /// blank. Pure - no I/O.</summary>
    public static class NamingContextMapper
    {
        public static NamingContext FromMetadata(CUEMetadata m, int trackIndex, int totalTracks)
        {
            string trackArtist = "", trackTitle = "", isrc = "";
            if (m?.Tracks != null && trackIndex >= 0 && trackIndex < m.Tracks.Count)
            {
                trackArtist = m.Tracks[trackIndex].Artist ?? "";
                trackTitle = m.Tracks[trackIndex].Title ?? "";
                isrc = m.Tracks[trackIndex].ISRC ?? "";
            }
            string albumArtist = m?.Artist ?? "";
            return new NamingContext
            {
                AlbumArtist = albumArtist,
                Artist = string.IsNullOrWhiteSpace(trackArtist) ? albumArtist : trackArtist,
                Album = m?.Title ?? "",
                Title = trackTitle,
                Year = m?.Year ?? "",
                Genre = m?.Genre ?? "",
                DiscNumber = ParseInt(m?.DiscNumber, 1),
                TotalDiscs = ParseInt(m?.TotalDiscs, 1),
                DiscSubtitle = m?.DiscName ?? "",
                Label = m?.Label ?? "",
                Catalog = m?.LabelNo ?? "",
                Barcode = m?.Barcode ?? "",
                Country = m?.Country ?? "",
                Isrc = isrc,
                OriginalYear = Year4(m?.ReleaseDate) ?? (m?.Year ?? ""),
                TrackNumber = trackIndex + 1,
                TotalTracks = totalTracks,
                // phase 3 fills these; empty now so %releasetype%/%releasestatus% render blank
                PrimaryType = "",
                ReleaseStatus = "",
                SecondaryTypes = Array.Empty<string>(),
            };
        }

        private static int ParseInt(string s, int dflt) => int.TryParse((s ?? "").Trim(), out int v) && v > 0 ? v : dflt;

        private static string Year4(string date)
        {
            date = (date ?? "").Trim();
            return date.Length >= 4 && int.TryParse(date.Substring(0, 4), out _) ? date.Substring(0, 4) : null;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CUETools.Wpf/Services/NamingContextMapper.cs CUETools.Wpf.Tests/NamingContextMapperTests.cs
git commit -m "feat(naming): CUEMetadata -> NamingContext mapper (existing fields; type/status blank)"
```

---

### Task 4: CUESheet explicit-track-names hook

**Files:**
- Modify: `CUETools.Processor/CUESheet.cs` (fields ~35, `GenerateFilenames` per-track loop 2276-2318)

**Interfaces:**
- Produces: `public void CUESheet.SetExplicitTrackNames(System.Collections.Generic.IList<string> namesNoExt)` - sets the per-track output names (relative to `OutputDir`, WITHOUT extension); `GenerateFilenames` then appends the encoder extension to each verbatim (dot-safe, no `ChangeExtension`) and skips `trackFilenameFormat`.

This is an engine change with no isolated unit test (constructing a `CUESheet` needs a real source); it is verified by the build here and exercised live in Tasks 5/8.

- [ ] **Step 1: Add the field and setter**

In `CUETools.Processor/CUESheet.cs`, near the filename flags (line ~35, alongside `_hasTrackFilenames`):

```csharp
        private bool _useExplicitTrackNames = false;
```

Add the public setter (near the `TrackFilenames` property, ~282):

```csharp
        /// <summary>Provide per-track output names (relative to OutputDir, no extension) computed by an
        /// external namer. GenerateFilenames appends the encoder extension verbatim (dot-safe) and does
        /// not consult trackFilenameFormat. Used by the WPF NamingEngine so preview equals output.</summary>
        public void SetExplicitTrackNames(System.Collections.Generic.IList<string> namesNoExt)
        {
            _trackFilenames.Clear();
            foreach (var n in namesNoExt) _trackFilenames.Add(n);
            _hasTrackFilenames = true;
            _useExplicitTrackNames = true;
        }
```

- [ ] **Step 2: Honor it in GenerateFilenames**

In the per-track loop (line 2276-2318), add a first branch for explicit names. Change the branch head at line 2280 from `if (...)` to include the explicit case first:

```csharp
                if (_useExplicitTrackNames && !htoa)
                {
                    // external namer already produced the relative name; just append the extension
                    // verbatim so a title with a dot (e.g. "No. 9") is not truncated by ChangeExtension
                    TrackFilenames[iTrack] = TrackFilenames[iTrack] + extension;
                }
                else if (_config.keepOriginalFilenames && htoa && HasHTOAFilename)
                {
                    HTOAFilename = Path.ChangeExtension(HTOAFilename, extension);
                }
                else if (_config.keepOriginalFilenames && !htoa && HasTrackFilenames)
                {
                    TrackFilenames[iTrack] = Path.ChangeExtension(TrackFilenames[iTrack], extension);
                }
                else
                {
                    // ... existing format-based block unchanged ...
                }
```

(Leave the existing HTOA/keepOriginal/format branches exactly as they are; only prepend the explicit branch.)

- [ ] **Step 3: Build**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: "Build succeeded. 0 Error(s)". (Building the WPF project compiles the processor too.)

- [ ] **Step 4: Commit**

```bash
git add CUETools.Processor/CUESheet.cs
git commit -m "feat(engine): CUESheet.SetExplicitTrackNames - write to externally-computed track names"
```

---

### Task 5: Route rip encode naming through NamingEngine

**Files:**
- Modify: `CUETools.Wpf/Services/RipService.cs` (encode branch ~250-265; remove the `savedTrackFormat`/`EngineTrackFilenameFormat` stopgap added earlier - the declaration before the `try`, the assignment before `GenerateFilenames`, the restore in `finally`, and the `EngineTrackFilenameFormat` helper)

**Interfaces:**
- Consumes: `NamingContextMapper.FromMetadata`, `NamingEngine.Render`, `NamingPaths.Split`, `CUESheet.SetExplicitTrackNames`, `AppSettings.LoadNamingScheme()` (the user's current scheme).

Design (replace the encode-branch naming setup):
1. Load the scheme: `var scheme = _settings.LoadNamingScheme();`
2. Build the per-track rendered relative paths: for `t` in `0..cue.TrackCount-1`, `ctx = NamingContextMapper.FromMetadata(cue.Metadata, t, cue.TrackCount)`, `rel[t] = NamingEngine.Render(ctx, scheme)`.
3. `var (albumSub, remainders) = NamingPaths.Split(rel);`
4. `outDir = Path.Combine(baseDir, albumSub);` (the album folder; `baseDir` = the user's output base as today). `Directory.CreateDirectory(outDir);`
5. Create each track's directory: for each remainder, `Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(outDir, remainder)))` (covers `Disc N/`).
6. `cue.SetExplicitTrackNames(remainders);`
7. `cue.GenerateFilenames(lossy ? Lossy : Lossless, format, Path.Combine(outDir, "album.cue"));` (unchanged call; the explicit names now drive per-track output; `outDir` still holds the .cue/.log/cover).
8. Remove the `savedTrackFormat` save/restore and the `EngineTrackFilenameFormat` override + helper (superseded; the engine no longer reads `trackFilenameFormat` for these tracks).

Keep the verify branch as-is (it writes no files). If `cue.Metadata` is not populated at this point relative to where naming is computed, compute naming AFTER metadata is applied (it is applied at `CopyMetadata`, before `GenerateFilenames`, so the current ordering holds).

- [ ] **Step 1: Implement the routing** (edit the encode branch per the design above; remove the stopgap).
- [ ] **Step 2: Build**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: "Build succeeded. 0 Error(s)". If a running `CUETools.Wpf.exe` locks DLLs, `taskkill //F //IM CUETools.Wpf.exe` and retry.

- [ ] **Step 3: Run the test suite** (nothing should regress)

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug --nologo`
Expected: PASS (all prior tests; the stopgap's `EngineTrackFilenameFormat` tests are removed since the helper is gone - delete `CUETools.Wpf.Tests/EngineTrackFilenameFormatTests.cs` in this task and note it in the commit).

- [ ] **Step 4: Commit**

```bash
git add CUETools.Wpf/Services/RipService.cs CUETools.Wpf.Tests/EngineTrackFilenameFormatTests.cs
git commit -m "feat(rip): route encode naming through NamingEngine; drop the trackFilenameFormat stopgap"
```

---

### Task 6: Route convert naming through NamingEngine

**Files:**
- Modify: `CUETools.Wpf/Services/ConvertService.cs` (~108-122, the same outDir + GenerateFilenames setup)

**Interfaces:**
- Consumes: same as Task 5.

Apply the identical routing as Task 5 to `ConvertService.Convert`: load the scheme, render per-track relative paths from `cue.Metadata`, `NamingPaths.Split`, set `outDir` to the album folder, create the directory tree, `cue.SetExplicitTrackNames(remainders)`, then `cue.GenerateFilenames(..., Path.Combine(outDir, "album.cue"))`. `ConvertService` gets the user's `AppSettings` the same way `RipService` does (via the constructor - confirm it is injected; if not, inject `AppSettings` following `RipService`'s registration).

- [ ] **Step 1: Implement the routing** (mirror Task 5 in `ConvertService.Convert`).
- [ ] **Step 2: Build**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: "Build succeeded. 0 Error(s)".

- [ ] **Step 3: Commit**

```bash
git add CUETools.Wpf/Services/ConvertService.cs
git commit -m "feat(convert): route convert naming through NamingEngine (same as rip)"
```

---

### Task 7: Reconcile NamingViewModel

**Files:**
- Modify: `CUETools.Wpf/ViewModels/NamingViewModel.cs` (`Apply` ~92-97)

The `Apply()` push `_config.trackFilenameFormat = _scheme.Template` (line 94) was the bug source and is now dead for output (rip/convert inject explicit names). Remove that line; keep saving the scheme and refreshing the preview. The preview already renders with `NamingEngine.Render` (line 106), which is exactly what output now uses - so preview equals output.

- [ ] **Step 1: Remove the dead push**

Change `Apply` to:

```csharp
    private void Apply()
    {
        // Real rip/convert output now renders through NamingEngine (RipService/ConvertService inject
        // explicit names), so the engine's trackFilenameFormat is no longer used for track naming and
        // must not be overwritten with WPF-token syntax the old engine cannot parse.
        _settings.SaveNamingScheme(_scheme);
        Refresh();
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: "Build succeeded. 0 Error(s)".

- [ ] **Step 3: Commit**

```bash
git add CUETools.Wpf/ViewModels/NamingViewModel.cs
git commit -m "refactor(naming): stop pushing the WPF template into the engine trackFilenameFormat (dead)"
```

---

### Task 8: Live verification

**Files:** none (manual; record results in the PR / ledger).

After Tasks 1-7 build green and all unit/fuzz tests pass, verify on the drive:

- [ ] **Single-disc rip:** rip a disc to FLAC; confirm files land at `<base>/<AlbumArtist - Album[descriptor]>/NN - Title.flac` (the rich scheme, not `%albumartist%` literal), the `.cue`/`.log` sit in the album folder, and the Naming page preview for that disc matches the actual paths exactly.
- [ ] **Test & Copy:** run Test & Copy on a clean disc; confirm the Copy read now writes through the same engine (no regression from the removed stopgap) and the `Test & Copy.log` + tracks land correctly.
- [ ] **Convert:** convert a file/folder; confirm the same naming applies.
- [ ] **A title with a dot** (e.g., a track like "No. 9" or "Track 3.14"): confirm the filename keeps the dot and is not truncated (the explicit-names hook appends the extension instead of `ChangeExtension`).
- [ ] Record the outcomes in the PR description.

---

## Self-Review

**Spec coverage (phase 1 rows):** one naming engine for real rip + convert (Tasks 5, 6); `CUEMetadata -> NamingContext` mapper (Task 3); unified vocabulary + new tokens (Task 1); catalog-vs-barcode split handled in the mapper (Task 3, `Catalog <- LabelNo`, `Barcode <- Barcode`); output-path injection + directory creation (Tasks 4, 5, 6); stopgap removed (Task 5); preview equals output (Task 7 + shared engine); multi-disc folders (Task 2 split + Task 5 dir creation); degrade cleanly (empty tokens drop - NamingEngine existing behavior + Task 1 empty type/status). Phases 2-4 are out of scope by design.

**Placeholder scan:** none - Tasks 1-4 carry complete code; Tasks 5-7 are edits to existing methods specified by exact steps + the existing code shown in the map. The one investigate-and-confirm note (Task 3's `CUEMetadata` ctor shape, Task 6's `AppSettings` injection) is a verify-the-signature instruction, not a deferred design decision.

**Type consistency:** `NamingContext` new fields, `NamingPaths.Split(IReadOnlyList<string>) -> (string, string[])`, `NamingContextMapper.FromMetadata(CUEMetadata, int, int) -> NamingContext`, `CUESheet.SetExplicitTrackNames(IList<string>)`, and the RipService/ConvertService call sequence use identical names and signatures across tasks.
