# Verify History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every disc a second, local, independent bit-exactness proof - each verify/rip records the disc's per-track AccurateRip/CTDB checksums, and a disc read before is auto-compared and the verdict surfaced.

**Architecture:** A shared gzip-JSON helper (load either plain or gzip, save gzip) backs a new `VerifyHistoryStore` keyed by the disc's AccurateRip TOC id. `RipService.Run` builds a `VerifyRecord` from `cue.ArVerify` after `cue.Go()`, compares/upserts it, surfaces the outcome on `VerifyResult`, and writes a readable `.verify` sidecar on a rip. The existing JSON stores are retrofitted onto the same helper.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, System.Text.Json, System.IO.Compression (GZipStream), MSTest v2.

## Global Constraints

- No em dashes / en dashes / typographic Unicode in code, comments, or UI copy. ASCII only: `" - "`, `->`, `~`, `<=`, `...`.
- The shareable diagnostic log stays ids/numbers only: `verify.history disc=<id> known=<0|1> matches=<0|1> diffTracks=<n>`. No titles, no paths. (The DB and the `.verify` sidecar may hold title/artist - they are the user's own local files.)
- The `.verify` sidecar stays plain readable JSON. It is NOT gzip-compressed.
- Match criterion between two reads of a disc is the AccurateRip CRC per track: compare `CRCV2`, fall back to `CRC(v1)` when a stored read has no v2. CTDB CRC32 is stored/shown as corroboration only.
- History DB is bounded to 5 records per disc.
- Build via `dotnet build CUETools.Wpf/CUETools.Wpf.csproj` (the ripper .csproj alone fails on the Bwg.Scsi net20 ResGen quirk). Run WPF-side unit tests via `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj`.
- Commit after each task. Push is a separate owner action (not part of tasks).

## File Structure

- `CUETools.Wpf/Services/GzJson.cs` (new) - shared gzip-JSON load-either / save-gzip helper.
- `CUETools.Wpf/Accuracy/VerifyHistory.cs` (new) - `VerifyRecord`, `TrackCrc`, `VerifyOutcome`, `VerifyHistoryStore`.
- `CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj` (new) - MSTest, refs CUETools.Wpf. Home for GzJson + VerifyHistoryStore tests.
- `CUETools.Wpf/Services/RipService.cs` - build record after Go(), compare/upsert, VerifyResult fields, `.verify` sidecar, log line.
- `CUETools.Wpf/App.xaml.cs` - register `VerifyHistoryStore` singleton.
- `CUETools.Wpf/ViewModels/RipViewModel.cs`, `Views/RipView.xaml` - surface the verdict.
- `CUETools.Wpf/Accuracy/DriveCalibration.cs`, `Services/HistoryStore.cs`, `Services/ReportStore.cs` - retrofit to GzJson.

---

### Task 1: GzJson helper + test project

**Files:**
- Create: `CUETools.Wpf/Services/GzJson.cs`
- Create: `CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj`
- Create: `CUETools.Wpf.Tests/GzJsonTests.cs`

**Interfaces:**
- Produces: `static class GzJson` with `T Load<T>(string path)` (returns default(T) if missing/unreadable; reads gzip if the file starts with 0x1f 0x8b, else plain JSON) and `void Save<T>(string path, T value)` (writes indented JSON, gzip-compressed; best-effort, creates the directory).

- [ ] **Step 1: Create the test project.** `CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.6.1" />
    <PackageReference Include="MSTest.TestFramework" Version="3.6.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CUETools.Wpf\CUETools.Wpf.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing tests.** `CUETools.Wpf.Tests/GzJsonTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class GzJsonTests
    {
        private string Temp() => Path.Combine(Path.GetTempPath(), "gzjson-" + System.Guid.NewGuid().ToString("N") + ".json.gz");

        [TestMethod]
        public void RoundTrips()
        {
            string p = Temp();
            var data = new List<int> { 1, 2, 3 };
            GzJson.Save(p, data);
            var back = GzJson.Load<List<int>>(p);
            CollectionAssert.AreEqual(data, back);
            File.Delete(p);
        }

        [TestMethod]
        public void SavedFileIsGzip()
        {
            string p = Temp();
            GzJson.Save(p, new List<int> { 9 });
            var bytes = File.ReadAllBytes(p);
            Assert.IsTrue(bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b, "must be gzip");
            File.Delete(p);
        }

        [TestMethod]
        public void LoadsExistingPlainJson()
        {
            string p = Path.Combine(Path.GetTempPath(), "plain-" + System.Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(p, "[4,5,6]");   // an old, uncompressed file
            var back = GzJson.Load<List<int>>(p);
            CollectionAssert.AreEqual(new List<int> { 4, 5, 6 }, back);
            File.Delete(p);
        }

        [TestMethod]
        public void MissingReturnsDefault()
        {
            Assert.IsNull(GzJson.Load<List<int>>(Path.Combine(Path.GetTempPath(), "nope-" + System.Guid.NewGuid().ToString("N") + ".json")));
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail.**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -v q`
Expected: FAIL - `GzJson` does not exist.

- [ ] **Step 4: Implement GzJson.** `CUETools.Wpf/Services/GzJson.cs`:

```csharp
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CUETools.Wpf.Services
{
    /// <summary>Shared JSON persistence that saves gzip-compressed and loads either format. Detects a
    /// gzip file by its magic bytes (0x1f 0x8b), so an existing plain-JSON file still opens - the next
    /// Save rewrites it compressed. Pure I/O, no UI - unit-tested with no drive.</summary>
    public static class GzJson
    {
        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { WriteIndented = true };

        public static T Load<T>(string path)
        {
            try
            {
                if (!File.Exists(path)) return default;
                byte[] raw = File.ReadAllBytes(path);
                if (raw.Length == 0) return default;
                string json;
                if (raw.Length >= 2 && raw[0] == 0x1f && raw[1] == 0x8b)
                {
                    using var ms = new MemoryStream(raw);
                    using var gz = new GZipStream(ms, CompressionMode.Decompress);
                    using var sr = new StreamReader(gz, Encoding.UTF8);
                    json = sr.ReadToEnd();
                }
                else
                {
                    json = Encoding.UTF8.GetString(raw);
                }
                return JsonSerializer.Deserialize<T>(json);
            }
            catch { return default; }
        }

        public static void Save<T>(string path, T value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string json = JsonSerializer.Serialize(value, Opts);
                using var fs = File.Create(path);
                using var gz = new GZipStream(fs, CompressionLevel.Optimal);
                using var sw = new StreamWriter(gz, new UTF8Encoding(false));
                sw.Write(json);
            }
            catch { /* best-effort persistence */ }
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass.**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -v q`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit.**

```bash
git add CUETools.Wpf/Services/GzJson.cs CUETools.Wpf.Tests
git commit -m "feat(verify-history): shared gzip-JSON helper (load-either, save-gzip) + tests"
```

---

### Task 2: VerifyRecord + VerifyHistoryStore + tests

**Files:**
- Create: `CUETools.Wpf/Accuracy/VerifyHistory.cs`
- Create: `CUETools.Wpf.Tests/VerifyHistoryStoreTests.cs`

**Interfaces:**
- Consumes: `GzJson` (Task 1).
- Produces: `TrackCrc { uint ArV1; uint ArV2; uint Crc32; }`; `VerifyRecord { string DiscId; TrackCrc[] Tracks; int ArConfidence, ArTotal, CtdbConfidence, CtdbTotal; string Drive; int ReadOffset; int CorrectionQuality; bool DeepRecovery; string Title, Artist; System.DateTime Utc; string RipperVersion; }`; `VerifyOutcome { bool KnownDisc; bool Matches; int PriorReads; int DiffTrackCount; }`; `VerifyHistoryStore` with `VerifyOutcome CompareAndUpsert(VerifyRecord r)` and (for the sidecar) `static string ToJson(VerifyRecord r)`. The match is per-track AR CRC: a track matches when `ArV2` equals the newest stored read's `ArV2` (or `ArV1` when either side's `ArV2` is 0).

- [ ] **Step 1: Write the failing tests.** `CUETools.Wpf.Tests/VerifyHistoryStoreTests.cs`:

```csharp
using System.IO;
using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class VerifyHistoryStoreTests
    {
        private static VerifyRecord Rec(string disc, params uint[] v2)
        {
            var t = new TrackCrc[v2.Length];
            for (int i = 0; i < v2.Length; i++) t[i] = new TrackCrc { ArV1 = v2[i] ^ 0x1u, ArV2 = v2[i], Crc32 = v2[i] };
            return new VerifyRecord { DiscId = disc, Tracks = t, Drive = "TEST", Utc = System.DateTime.UtcNow };
        }
        private VerifyHistoryStore NewStore() => new VerifyHistoryStore(Path.Combine(Path.GetTempPath(), "vh-" + System.Guid.NewGuid().ToString("N") + ".json.gz"));

        [TestMethod]
        public void FirstReadIsUnknown()
        {
            var o = NewStore().CompareAndUpsert(Rec("D1", 10, 20, 30));
            Assert.IsFalse(o.KnownDisc);
            Assert.AreEqual(0, o.PriorReads);
        }

        [TestMethod]
        public void SecondIdenticalReadMatches()
        {
            var s = NewStore();
            s.CompareAndUpsert(Rec("D1", 10, 20, 30));
            var o = s.CompareAndUpsert(Rec("D1", 10, 20, 30));
            Assert.IsTrue(o.KnownDisc);
            Assert.IsTrue(o.Matches);
            Assert.AreEqual(0, o.DiffTrackCount);
            Assert.AreEqual(1, o.PriorReads);
        }

        [TestMethod]
        public void DifferingReadFlagsTracks()
        {
            var s = NewStore();
            s.CompareAndUpsert(Rec("D1", 10, 20, 30));
            var o = s.CompareAndUpsert(Rec("D1", 10, 99, 30));   // track 2 differs
            Assert.IsTrue(o.KnownDisc);
            Assert.IsFalse(o.Matches);
            Assert.AreEqual(1, o.DiffTrackCount);
        }

        [TestMethod]
        public void PersistsAcrossInstances()
        {
            string path = Path.Combine(Path.GetTempPath(), "vh-" + System.Guid.NewGuid().ToString("N") + ".json.gz");
            new VerifyHistoryStore(path).CompareAndUpsert(Rec("D1", 10, 20, 30));
            var o = new VerifyHistoryStore(path).CompareAndUpsert(Rec("D1", 10, 20, 30));
            Assert.IsTrue(o.Matches);
            File.Delete(path);
        }

        [TestMethod]
        public void BoundedToFivePerDisc()
        {
            var s = NewStore();
            for (int i = 0; i < 8; i++) s.CompareAndUpsert(Rec("D1", (uint)i, 20, 30));
            // 8 reads in; still known and PriorReads never exceeds the 5-record bound
            var o = s.CompareAndUpsert(Rec("D1", 7, 20, 30));
            Assert.IsTrue(o.PriorReads <= 5);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail.**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj --filter VerifyHistoryStoreTests -v q`
Expected: FAIL - types do not exist.

- [ ] **Step 3: Implement VerifyHistory.** `CUETools.Wpf/Accuracy/VerifyHistory.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Accuracy
{
    /// <summary>One track's checksums as read. ArV2 is the match criterion (offset-corrected AR CRC),
    /// ArV1 the fallback for reads predating v2, Crc32 corroboration.</summary>
    public sealed class TrackCrc
    {
        public uint ArV1 { get; set; }
        public uint ArV2 { get; set; }
        public uint Crc32 { get; set; }
    }

    /// <summary>One read of one disc: its identity, the per-track checksums, and the context of the read.
    /// Title/Artist are for the user's own display; the shareable log never carries them.</summary>
    public sealed class VerifyRecord
    {
        public string DiscId { get; set; } = "";
        public TrackCrc[] Tracks { get; set; } = Array.Empty<TrackCrc>();
        public int ArConfidence { get; set; }
        public int ArTotal { get; set; }
        public int CtdbConfidence { get; set; }
        public int CtdbTotal { get; set; }
        public string Drive { get; set; } = "";
        public int ReadOffset { get; set; }
        public int CorrectionQuality { get; set; }
        public bool DeepRecovery { get; set; }
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public DateTime Utc { get; set; }
        public string RipperVersion { get; set; } = "";
    }

    /// <summary>Result of comparing a new read against this disc's stored history.</summary>
    public sealed class VerifyOutcome
    {
        public bool KnownDisc { get; set; }
        public bool Matches { get; set; }
        public int PriorReads { get; set; }
        public int DiffTrackCount { get; set; }
    }

    /// <summary>Your own local verify history: disc id -> up to 5 recent reads, gzip-persisted. On each
    /// read it compares per-track AccurateRip CRCs against the newest stored read (a second, offline,
    /// AccurateRip-independent bit-exactness check) and appends the record. Pure I/O, no drive.</summary>
    public sealed class VerifyHistoryStore
    {
        private const int MaxPerDisc = 5;
        private readonly string _path;

        public VerifyHistoryStore(string path = null)
        {
            _path = path ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CUETools2026", "verify-history.json.gz");
        }

        public VerifyOutcome CompareAndUpsert(VerifyRecord r)
        {
            var all = GzJson.Load<Dictionary<string, List<VerifyRecord>>>(_path)
                      ?? new Dictionary<string, List<VerifyRecord>>();
            string key = (r.DiscId ?? "").Trim();
            all.TryGetValue(key, out var reads);
            reads ??= new List<VerifyRecord>();

            var outcome = new VerifyOutcome { KnownDisc = reads.Count > 0, PriorReads = reads.Count };
            if (outcome.KnownDisc)
            {
                var prev = reads[reads.Count - 1];   // newest stored read
                int diff = 0;
                int n = Math.Min(prev.Tracks.Length, r.Tracks.Length);
                for (int i = 0; i < n; i++)
                    if (!SameTrack(prev.Tracks[i], r.Tracks[i])) diff++;
                diff += Math.Abs(prev.Tracks.Length - r.Tracks.Length);   // track-count change counts as diff
                outcome.DiffTrackCount = diff;
                outcome.Matches = diff == 0;
            }

            reads.Add(r);
            if (reads.Count > MaxPerDisc) reads.RemoveRange(0, reads.Count - MaxPerDisc);
            all[key] = reads;
            GzJson.Save(_path, all);
            return outcome;
        }

        // A track matches on the AccurateRip CRC: prefer v2, fall back to v1 when either side lacks v2.
        private static bool SameTrack(TrackCrc a, TrackCrc b)
        {
            if (a.ArV2 != 0 && b.ArV2 != 0) return a.ArV2 == b.ArV2;
            return a.ArV1 == b.ArV1;
        }

        public static string ToJson(VerifyRecord r) =>
            JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass.**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj --filter VerifyHistoryStoreTests -v q`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit.**

```bash
git add CUETools.Wpf/Accuracy/VerifyHistory.cs CUETools.Wpf.Tests/VerifyHistoryStoreTests.cs
git commit -m "feat(verify-history): VerifyRecord + VerifyHistoryStore (compare/upsert, AR-CRC match) + tests"
```

---

### Task 3: Wire into RipService + VerifyResult fields + sidecar + DI

**Files:**
- Modify: `CUETools.Wpf/Services/RipService.cs` (VerifyResult class ~line 12; ctor ~line 60; the return ~line 353)
- Modify: `CUETools.Wpf/App.xaml.cs` (register the store, ~line 49)

**Interfaces:**
- Consumes: `VerifyHistoryStore`, `VerifyRecord`, `TrackCrc`, `VerifyOutcome` (Task 2).
- Produces: `VerifyResult.HistoryKnown` (bool), `.HistoryMatches` (bool), `.HistoryPriorReads` (int), `.HistoryDiffTracks` (int).

- [ ] **Step 1: Register the store in DI.** In `App.xaml.cs`, next to the DriveCalibrationStore registration (line 49):

```csharp
        services.AddSingleton<CUETools.Wpf.Accuracy.VerifyHistoryStore>(_ => new CUETools.Wpf.Accuracy.VerifyHistoryStore());
```

- [ ] **Step 2: Add the fields to VerifyResult.** In `RipService.cs`, inside `VerifyResult` (after `FileCount`, ~line 23):

```csharp
    /// <summary>Local verify-history outcome (second-source bit-exactness): whether this disc was read
    /// before, whether the read matched, how many prior reads, and how many tracks differed.</summary>
    public bool HistoryKnown { get; init; }
    public bool HistoryMatches { get; init; }
    public int HistoryPriorReads { get; init; }
    public int HistoryDiffTracks { get; init; }
```

- [ ] **Step 3: Inject the store.** In `RipService`, add the field + ctor param (alongside `_calStore`):

```csharp
    private readonly CUETools.Wpf.Accuracy.VerifyHistoryStore _history;
```
and add `CUETools.Wpf.Accuracy.VerifyHistoryStore history` to the ctor parameter list and `_history = history;` to its body. (DI resolves it from Step 1.)

- [ ] **Step 4: Build the record, compare, surface, write the sidecar.** In `RipService.Run`, replace the `return new VerifyResult { ... }` block (RipService.cs ~line 353-366) with:

```csharp
            // Verify history: capture the per-track AccurateRip CRCs this read produced (deterministic
            // in the bytes) and compare against our own earlier reads of this disc - a second, offline,
            // AccurateRip-independent bit-exactness check.
            var vh = new CUETools.Wpf.Accuracy.VerifyOutcome();
            try
            {
                var tracks = new CUETools.Wpf.Accuracy.TrackCrc[n];
                for (int t = 0; t < n; t++)
                {
                    uint v1 = 0, v2 = 0, c32 = 0;
                    try { v1 = cue.ArVerify.CRC(t); } catch { }
                    try { v2 = cue.ArVerify.CRCV2(t); } catch { }
                    try { c32 = cue.ArVerify.CRC32(t); } catch { }
                    tracks[t] = new CUETools.Wpf.Accuracy.TrackCrc { ArV1 = v1, ArV2 = v2, Crc32 = c32 };
                }
                var record = new CUETools.Wpf.Accuracy.VerifyRecord
                {
                    DiscId = cue.TOC.TOCID ?? "",
                    Tracks = tracks,
                    ArConfidence = arConf, ArTotal = arTotal,
                    CtdbConfidence = ctConf, CtdbTotal = ctTotal,
                    Drive = (reader.ARName ?? "").Trim(),
                    ReadOffset = offset,
                    CorrectionQuality = cq,
                    DeepRecovery = _settings.DeepRecovery,
                    Title = cue.Metadata?.Title ?? "",
                    Artist = cue.Metadata?.Artist ?? "",
                    Utc = DateTime.UtcNow,
                    RipperVersion = "2026.1.0",
                };
                vh = _history.CompareAndUpsert(record);
                _log.Info("verify.history", $"disc={record.DiscId} known={(vh.KnownDisc ? 1 : 0)} matches={(vh.Matches ? 1 : 0)} diffTracks={vh.DiffTrackCount}");
                if (encode && Directory.Exists(outDir))
                {
                    try { File.WriteAllText(Path.Combine(outDir, "rip.verify"), CUETools.Wpf.Accuracy.VerifyHistoryStore.ToJson(record)); }
                    catch (Exception ex) { _log.Warn("verify.history", "sidecar write failed: " + ex.GetType().Name); }
                }
            }
            catch (Exception ex) { _log.Warn("verify.history", "record build failed: " + ex.GetType().Name); }

            return new VerifyResult
            {
                Ok = true,
                Status = status,
                ArConfidence = arConf,
                ArTotal = arTotal,
                CtdbConfidence = ctConf,
                CtdbTotal = ctTotal,
                Accurate = arConf > 0,
                OutputDir = outDir,
                FileCount = files,
                ArPerTrack = arpt,
                CtdbPerTrack = ctpt,
                HistoryKnown = vh.KnownDisc,
                HistoryMatches = vh.Matches,
                HistoryPriorReads = vh.PriorReads,
                HistoryDiffTracks = vh.DiffTrackCount,
            };
```

(Integration note: `reader`, `offset`, `cq`, `outDir`, `n`, `arConf`, `arTotal`, `ctConf`, `ctTotal`, `arpt`, `ctpt` are all already in scope at this point in `Run`. `cue.Metadata.Title/Artist` are the CUEMetadata display fields; if the exact property names differ, use the CUEMetadata's title/artist accessors - they are read-only display strings.)

- [ ] **Step 5: Build.**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: `Build succeeded. 0 Error(s)`. (If `cue.Metadata.Title`/`.Artist` do not resolve, check the CUEMetadata type for its title/artist properties and adjust - this is the only name that was not pre-traced.)

- [ ] **Step 6: Commit.**

```bash
git add CUETools.Wpf/Services/RipService.cs CUETools.Wpf/App.xaml.cs
git commit -m "feat(verify-history): record + compare per-track CRCs after Go(), surface on VerifyResult, write .verify sidecar"
```

---

### Task 4: Surface the verdict in the result UI

**Files:**
- Modify: `CUETools.Wpf/ViewModels/RipViewModel.cs` (where the VerifyResult is consumed into result properties)
- Modify: `CUETools.Wpf/Views/RipView.xaml` (the rip-complete result panel)

**Interfaces:**
- Consumes: `VerifyResult.HistoryKnown/HistoryMatches/HistoryPriorReads/HistoryDiffTracks` (Task 3).
- Produces: `RipViewModel.HistoryText` (string), `RipViewModel.HistoryIsWarning` (bool).

- [ ] **Step 1: Add the properties.** In `RipViewModel`, add backing fields and:

```csharp
    private string _historyText = "";
    public string HistoryText { get => _historyText; private set => Set(ref _historyText, value); }
    private bool _historyIsWarning;
    public bool HistoryIsWarning { get => _historyIsWarning; private set => Set(ref _historyIsWarning, value); }
```

- [ ] **Step 2: Set them where the VerifyResult is applied.** Find where the RipViewModel consumes a `VerifyResult` (the rip-complete handler that sets the AR/CTDB result text), and add:

```csharp
        if (!result.HistoryKnown)
        { HistoryText = "First read of this disc - recorded to your verify history."; HistoryIsWarning = false; }
        else if (result.HistoryMatches)
        { HistoryText = $"Consistent with your {result.HistoryPriorReads} earlier read(s) - bytes match."; HistoryIsWarning = false; }
        else
        { HistoryText = $"DIFFERS from your earlier read on {result.HistoryDiffTracks} track(s) - investigate."; HistoryIsWarning = true; }
```

- [ ] **Step 3: Show it in the result panel.** In `RipView.xaml`, in the rip-complete Border (the one bound to the result), add a TextBlock:

```xml
        <TextBlock Text="{Binding HistoryText}" Margin="0,6,0,0" TextWrapping="Wrap" FontSize="11.5"
                   Foreground="{Binding HistoryIsWarning, Converter={StaticResource BoolToBrush}}"
                   Visibility="{Binding HistoryText, Converter={StaticResource NonEmptyVis}}"/>
```

(If `BoolToBrush` / `NonEmptyVis` converters do not already exist in the resources, use the existing muted/accent brushes directly and a `BoolVis`-style converter that is already defined - match the converters the sibling result TextBlocks use.)

- [ ] **Step 4: Build.**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit.**

```bash
git add CUETools.Wpf/ViewModels/RipViewModel.cs CUETools.Wpf/Views/RipView.xaml
git commit -m "feat(verify-history): surface the consistency verdict in the rip result panel"
```

---

### Task 5: Retrofit existing JSON stores to GzJson

**Files:**
- Modify: `CUETools.Wpf/Accuracy/DriveCalibration.cs` (DriveCalibrationStore Load/Save)
- Modify: `CUETools.Wpf/Services/HistoryStore.cs` (Read/Write)
- Modify: `CUETools.Wpf/Services/ReportStore.cs` (its load/save)

**Interfaces:**
- Consumes: `GzJson` (Task 1).

- [ ] **Step 1: Retrofit DriveCalibrationStore.** In `DriveCalibration.cs`, replace the `Load()` body and the `Save` write with GzJson (keep the same `_path`, but the file is now written gzip; the load reads either):

```csharp
    private Dictionary<string, DriveCalibration> Load()
        => GzJson.Load<Dictionary<string, DriveCalibration>>(_path) ?? new Dictionary<string, DriveCalibration>();
```
and in `Save`, replace the `File.WriteAllText(...JsonSerializer.Serialize...)` line with:

```csharp
        GzJson.Save(_path, all);
```
Add `using CUETools.Wpf.Services;` at the top. Leave the `_path` as `drive-calibration.json` (GzJson writes gzip content into that name; Load detects it either way, so old plain files still open).

- [ ] **Step 2: Retrofit HistoryStore.** In `HistoryStore.cs`, replace `Write()` and `Read(path)` internals:

```csharp
    private void Write()
    {
        try { CUETools.Wpf.Services.GzJson.Save(_path, _rows); }
        catch (Exception ex) { _log.Warn("history", "history write failed: " + ex.GetType().Name); }
    }

    private List<Row> Read(string path)
    {
        var rows = CUETools.Wpf.Services.GzJson.Load<List<Row>>(path);
        if (rows != null) return rows;
        return new List<Row>();
    }
```
(Keep the existing corrupt-file `.bak` handling if present - GzJson.Load returns null on a bad file, so the Read fallback returns an empty list; if the original preserved a `.bak`, keep that branch by checking `File.Exists(path)` before returning empty.)

- [ ] **Step 3: Retrofit ReportStore.** Open `CUETools.Wpf/Services/ReportStore.cs`, find its JSON load and save (same `File.ReadAllText`/`File.WriteAllText` + `JsonSerializer` pattern), and replace them with `CUETools.Wpf.Services.GzJson.Load<...>(path)` and `CUETools.Wpf.Services.GzJson.Save(path, value)`, matching the store's element type. Keep the `_path` unchanged.

- [ ] **Step 4: Build.**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Manual back-compat check.** Confirm by inspection: each retrofitted `Load` goes through `GzJson.Load`, which reads a pre-existing plain-JSON file (magic-byte check) so no user data is lost; the first `Save` after upgrade rewrites it gzip.

- [ ] **Step 6: Commit.**

```bash
git add CUETools.Wpf/Accuracy/DriveCalibration.cs CUETools.Wpf/Services/HistoryStore.cs CUETools.Wpf/Services/ReportStore.cs
git commit -m "refactor(stores): retrofit calibration/history/report JSON stores to gzip via GzJson (load-either back-compat)"
```

---

## Notes for the implementer

- The only names not pre-traced are `cue.Metadata.Title`/`.Artist` (Task 3) and the exact converter resources in RipView (Task 4); both are flagged inline with a fallback. Everything else (`cue.TOC.TOCID`, `cue.ArVerify.CRC/CRCV2/CRC32`, the VerifyResult return site, the store patterns) is confirmed in the spec.
- WPF-side unit tests run via `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj`. The main build is always `dotnet build CUETools.Wpf/CUETools.Wpf.csproj` (never the ripper .csproj alone).
- Privacy: only the `verify.history` log line is constrained to ids/numbers; the DB and the `rip.verify` sidecar legitimately hold title/artist (the user's own files).
- Build risk (Task 1): `CUETools.Wpf.Tests` references `CUETools.Wpf`, which is a WPF app (OutputType WinExe). A ProjectReference to a WinExe normally works and gives the assembly's public types, but if the test project fails to build against it, the fallback is to move the two pure classes (`GzJson`, and the `VerifyHistory` types) into a small non-WPF class library (`CUETools.Wpf.Core`, netstandard2.0/net8.0) that both `CUETools.Wpf` and the test project reference. Prefer the direct reference; only extract if it does not build.
