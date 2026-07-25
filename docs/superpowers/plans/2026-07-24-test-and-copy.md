# Test & Copy secure rip mode - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Test & Copy" rip action that reads the disc twice (a third time on a mismatch), commits only tracks two independent reads agree on bit-for-bit, holds the rest for a user decision, and writes a Test & Copy log - a locally-generated proof for discs AccurateRip has never seen.

**Architecture:** Each read is the existing `RipService.Run(...)` (a Verify pass for the Test read; Encode passes to temp staging folders for the Copy and third reads). A new pure `TestAndCopyResolver` compares the per-read checksum records and decides, per track, whether two reads agree and which staged read's file to commit. A new `RipService.RunTestAndCopy(...)` orchestrates the reads, forces cache defeat for genuine independence (auto-calibrating first when needed), assembles/commits or holds, and writes the log. The Rip page gets a third action button and a HELD result card.

**Tech Stack:** .NET 8, WPF, MSTest v2. Build via `CUETools.Wpf/CUETools.Wpf.csproj` (the ripper `.csproj` alone fails on the Bwg.Scsi net20 ResGen quirk). Tests in the existing `CUETools.Wpf.Tests` project.

## Global Constraints

- ASCII only in code, comments, and UI copy: no em/en dashes, no typographic Unicode. Use " - ", "->", "~", "<=", "...".
- The shareable DiagnosticLog line carries ids and numbers only, never titles/artists/paths: `testcopy disc=<id> reads=<n> passed=<0|1> heldTracks=<m>`.
- Never write an unverified file into the output folder without an explicit user choice (the "Accept anyway" button).
- Cache defeat is forced ON for EVERY read on a caching drive, regardless of the Deep recovery toggle. Independence is not optional.
- Per-track files only in v1. Single-file image + cue output is v2 (separate plan).
- Reuse the existing per-track compare (`VerifyHistoryStore` AR-CRC comparison). Do NOT switch it to raw CRC32 - that breaks verify-history's cross-drive matching.
- Commit per task. Push is a separate owner action (do not push).
- Build the WPF project after each code task; a bad XAML brush/binding or a compile error surfaces there.

## File Structure

- `CUETools.Wpf/Accuracy/VerifyHistory.cs` (modify): expose the per-track AR-CRC comparison as `public static bool SameAudio(TrackCrc, TrackCrc)`; `SameTrack` delegates to it.
- `CUETools.Wpf/Accuracy/TestAndCopyResolver.cs` (new): pure resolver + result types (`TestCopyOutcome`, `TrackVerdict`, `TestCopyResult`).
- `CUETools.Wpf/Accuracy/TestAndCopyLog.cs` (new): pure `Format(...)` that renders the human-readable Test & Copy log text.
- `CUETools.Wpf/Services/RipService.cs` (modify): add `stageOnly` + `forceCacheDefeat` to `Run`; return the built `VerifyRecord` + `FailedWindows` on `VerifyResult`; add `RunTestAndCopy(...)` and the hold follow-ups `CommitCopyReadAnyway(...)` / `DiscardStaging(...)`.
- `CUETools.Wpf/ViewModels/RipViewModel.cs` (modify): `TestCopyCommand`, `RunTestCopyAsync`, HELD state + `AcceptCopyAnywayCommand` / `DiscardHeldCommand`, result surfacing.
- `CUETools.Wpf/Views/RipView.xaml` (modify): the "Test & Copy" button; the HELD result card with three buttons.
- `CUETools.Wpf.Tests/TestAndCopyResolverTests.cs` (new), `CUETools.Wpf.Tests/TestAndCopyResolverFuzzTests.cs` (new), `CUETools.Wpf.Tests/TestAndCopyLogTests.cs` (new).

---

### Task 1: Reusable per-track comparison

**Files:**
- Modify: `CUETools.Wpf/Accuracy/VerifyHistory.cs:94-99`
- Test: `CUETools.Wpf.Tests/TestAndCopyResolverTests.cs` (create; used again in Task 2)

**Interfaces:**
- Produces: `public static bool VerifyHistoryStore.SameAudio(TrackCrc a, TrackCrc b)` - true when two reads' audio matches on the AccurateRip CRC (v2, falling back to v1); null-tolerant.

- [ ] **Step 1: Write the failing test**

Create `CUETools.Wpf.Tests/TestAndCopyResolverTests.cs`:

```csharp
using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class TestAndCopyResolverTests
    {
        private static TrackCrc T(uint v2, uint v1 = 0, uint c32 = 0) =>
            new TrackCrc { ArV2 = v2, ArV1 = v1, Crc32 = c32 };

        [TestMethod]
        public void SameAudio_MatchesOnV2()
        {
            Assert.IsTrue(VerifyHistoryStore.SameAudio(T(10), T(10)));
            Assert.IsFalse(VerifyHistoryStore.SameAudio(T(10), T(11)));
        }

        [TestMethod]
        public void SameAudio_FallsBackToV1WhenV2Absent()
        {
            Assert.IsTrue(VerifyHistoryStore.SameAudio(T(0, 5), T(0, 5)));
            Assert.IsFalse(VerifyHistoryStore.SameAudio(T(0, 5), T(0, 6)));
        }

        [TestMethod]
        public void SameAudio_NullIsNeverEqual()
        {
            Assert.IsFalse(VerifyHistoryStore.SameAudio(null, T(10)));
            Assert.IsFalse(VerifyHistoryStore.SameAudio(T(10), null));
            Assert.IsFalse(VerifyHistoryStore.SameAudio(null, null));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug -v q -nologo`
Expected: FAIL to compile - `SameAudio` does not exist.

- [ ] **Step 3: Implement**

In `CUETools.Wpf/Accuracy/VerifyHistory.cs`, replace the private `SameTrack` (lines 94-99) with a public, null-tolerant `SameAudio` and a thin delegate so `CompareAndUpsert` is unchanged:

```csharp
        // A track matches on the AccurateRip CRC: prefer v2, fall back to v1 when either side lacks v2.
        // Offset-corrected, so it holds across drives (verify-history's cross-drive case) and, for the
        // same-drive same-offset reads Test & Copy performs, equals CRC32 bit-identity. Do not switch
        // this to raw CRC32 - that would break cross-drive matching. Null-tolerant for corrupt history.
        public static bool SameAudio(TrackCrc a, TrackCrc b)
        {
            if (a == null || b == null) return false;
            if (a.ArV2 != 0 && b.ArV2 != 0) return a.ArV2 == b.ArV2;
            return a.ArV1 == b.ArV1;
        }

        private static bool SameTrack(TrackCrc a, TrackCrc b) => SameAudio(a, b);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug --nologo`
Expected: PASS (the three new tests plus all existing verify-history/gzjson tests still green).

- [ ] **Step 5: Commit**

```bash
git add CUETools.Wpf/Accuracy/VerifyHistory.cs CUETools.Wpf.Tests/TestAndCopyResolverTests.cs
git commit -m "refactor(verify): expose per-track AR-CRC compare as public SameAudio (null-tolerant)"
```

---

### Task 2: TestAndCopyResolver (pure)

**Files:**
- Create: `CUETools.Wpf/Accuracy/TestAndCopyResolver.cs`
- Test: `CUETools.Wpf.Tests/TestAndCopyResolverTests.cs` (add to it)

**Interfaces:**
- Consumes: `VerifyRecord` (has `TrackCrc[] Tracks`), `VerifyHistoryStore.SameAudio` (Task 1).
- Produces:
  - `enum TestCopyOutcome { Passed, Held }`
  - `class TrackVerdict { int TrackIndex; bool Agreed; int SourceReadIndex; int[] AgreeingReads; }`
  - `class TestCopyResult { TestCopyOutcome Outcome; int ReadsUsed; TrackVerdict[] Tracks; int[] HeldTracks; }`
  - `static TestCopyResult TestAndCopyResolver.Resolve(IReadOnlyList<VerifyRecord> reads, IReadOnlyList<bool> staged)`

Semantics: for each track, find any two reads whose audio agrees (`SameAudio`). If found, the track is `Agreed`; `SourceReadIndex` is the SMALLEST staged read index that participates in an agreeing pair (so the Copy read at index 1 wins over the third read at index 2), and `AgreeingReads` is that pair `{min,max}`. If no pair agrees, the track is held (`SourceReadIndex = -1`) and its index goes in `HeldTracks`. `Outcome` is `Passed` only when `HeldTracks` is empty. Track count is the max `Tracks.Length` across reads; a read missing a track index simply cannot form a pair there.

- [ ] **Step 1: Write the failing tests**

Add to `CUETools.Wpf.Tests/TestAndCopyResolverTests.cs` (inside the class):

```csharp
        private static VerifyRecord Read(params uint[] v2)
        {
            var t = new TrackCrc[v2.Length];
            for (int i = 0; i < v2.Length; i++) t[i] = new TrackCrc { ArV2 = v2[i], Crc32 = v2[i] };
            return new VerifyRecord { Tracks = t };
        }
        // staging flags: Test read is not staged (index 0); Copy/third reads are staged.
        private static bool[] Staged(int n) { var s = new bool[n]; for (int i = 1; i < n; i++) s[i] = true; return s; }

        [TestMethod]
        public void TwoReadsAgree_PassesAndSourcesTheCopyRead()
        {
            var reads = new[] { Read(10, 20, 30), Read(10, 20, 30) };
            var r = TestAndCopyResolver.Resolve(reads, Staged(2));
            Assert.AreEqual(TestCopyOutcome.Passed, r.Outcome);
            Assert.AreEqual(0, r.HeldTracks.Length);
            foreach (var v in r.Tracks) { Assert.IsTrue(v.Agreed); Assert.AreEqual(1, v.SourceReadIndex); }
        }

        [TestMethod]
        public void TwoReadsDiffer_Holds()
        {
            var reads = new[] { Read(10, 20, 30), Read(10, 99, 30) }; // track 2 differs
            var r = TestAndCopyResolver.Resolve(reads, Staged(2));
            Assert.AreEqual(TestCopyOutcome.Held, r.Outcome);
            CollectionAssert.AreEqual(new[] { 1 }, r.HeldTracks);
            Assert.IsFalse(r.Tracks[1].Agreed);
            Assert.AreEqual(-1, r.Tracks[1].SourceReadIndex);
        }

        [TestMethod]
        public void ThirdReadResolvesAMismatch_SourcesTheAgreeingStagedRead()
        {
            // track 2: Test(20) != Copy(99); third read(20) agrees with Test -> source must be read 2
            var reads = new[] { Read(10, 20, 30), Read(10, 99, 30), Read(10, 20, 30) };
            var r = TestAndCopyResolver.Resolve(reads, Staged(3));
            Assert.AreEqual(TestCopyOutcome.Passed, r.Outcome);
            Assert.AreEqual(1, r.Tracks[0].SourceReadIndex);   // all three agree -> Copy (1) preferred
            Assert.AreEqual(2, r.Tracks[1].SourceReadIndex);   // only Test+third agree -> third (2)
            CollectionAssert.AreEqual(new[] { 0, 2 }, r.Tracks[1].AgreeingReads);
        }

        [TestMethod]
        public void ThirdReadStillDisagrees_HoldsThatTrack()
        {
            // track 1: all three different -> held; track 2: Copy+third agree -> committed from Copy
            var reads = new[] { Read(1, 50), Read(2, 50), Read(3, 50) };
            var r = TestAndCopyResolver.Resolve(reads, Staged(3));
            Assert.AreEqual(TestCopyOutcome.Held, r.Outcome);
            CollectionAssert.AreEqual(new[] { 0 }, r.HeldTracks);
            Assert.AreEqual(1, r.Tracks[1].SourceReadIndex);
        }

        [TestMethod]
        public void CopyDisagreesButTestAndThirdAgree_SourcesThird()
        {
            var reads = new[] { Read(7), Read(8), Read(7) };  // Copy(8) is the odd one out
            var r = TestAndCopyResolver.Resolve(reads, Staged(3));
            Assert.AreEqual(TestCopyOutcome.Passed, r.Outcome);
            Assert.AreEqual(2, r.Tracks[0].SourceReadIndex);
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug -v q -nologo`
Expected: FAIL to compile - `TestAndCopyResolver`, `TestCopyOutcome` do not exist.

- [ ] **Step 3: Implement**

Create `CUETools.Wpf/Accuracy/TestAndCopyResolver.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace CUETools.Wpf.Accuracy
{
    public enum TestCopyOutcome { Passed, Held }

    /// <summary>Per-track verdict: whether two reads agreed, which read's file to commit (a staged
    /// read, or -1 when held), and the agreeing pair (for the log).</summary>
    public sealed class TrackVerdict
    {
        public int TrackIndex { get; set; }
        public bool Agreed { get; set; }
        public int SourceReadIndex { get; set; } = -1;
        public int[] AgreeingReads { get; set; } = Array.Empty<int>();
    }

    public sealed class TestCopyResult
    {
        public TestCopyOutcome Outcome { get; set; }
        public int ReadsUsed { get; set; }
        public TrackVerdict[] Tracks { get; set; } = Array.Empty<TrackVerdict>();
        public int[] HeldTracks { get; set; } = Array.Empty<int>();
    }

    /// <summary>Pure Test & Copy resolver: given the per-read checksum records and which reads are
    /// staged (have audio on disk), decide per track whether two reads agree bit-for-bit and which
    /// staged read's file to commit. No hardware, no I/O - fully unit-testable.</summary>
    public static class TestAndCopyResolver
    {
        public static TestCopyResult Resolve(IReadOnlyList<VerifyRecord> reads, IReadOnlyList<bool> staged)
        {
            if (reads == null) throw new ArgumentNullException(nameof(reads));
            if (staged == null) throw new ArgumentNullException(nameof(staged));
            if (staged.Count != reads.Count)
                throw new ArgumentException("one staged flag per read is required", nameof(staged));

            int trackCount = 0;
            foreach (var r in reads) trackCount = Math.Max(trackCount, r?.Tracks?.Length ?? 0);

            var verdicts = new TrackVerdict[trackCount];
            var held = new List<int>();

            for (int t = 0; t < trackCount; t++)
            {
                var v = new TrackVerdict { TrackIndex = t };
                // smallest staged read that agrees with some other read on this track
                for (int i = 0; i < reads.Count && !v.Agreed; i++)
                {
                    if (!staged[i]) continue;
                    var ti = Track(reads[i], t);
                    if (ti == null) continue;
                    for (int j = 0; j < reads.Count; j++)
                    {
                        if (j == i) continue;
                        var tj = Track(reads[j], t);
                        if (tj == null) continue;
                        if (VerifyHistoryStore.SameAudio(ti, tj))
                        {
                            v.Agreed = true;
                            v.SourceReadIndex = i;
                            v.AgreeingReads = new[] { Math.Min(i, j), Math.Max(i, j) };
                            break;
                        }
                    }
                }
                if (!v.Agreed) held.Add(t);
                verdicts[t] = v;
            }

            return new TestCopyResult
            {
                Outcome = held.Count == 0 ? TestCopyOutcome.Passed : TestCopyOutcome.Held,
                ReadsUsed = reads.Count,
                Tracks = verdicts,
                HeldTracks = held.ToArray(),
            };
        }

        private static TrackCrc Track(VerifyRecord r, int t)
        {
            var arr = r?.Tracks;
            return (arr != null && t >= 0 && t < arr.Length) ? arr[t] : null;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug --nologo`
Expected: PASS (all Task 2 tests plus everything from Task 1).

- [ ] **Step 5: Commit**

```bash
git add CUETools.Wpf/Accuracy/TestAndCopyResolver.cs CUETools.Wpf.Tests/TestAndCopyResolverTests.cs
git commit -m "feat(verify): TestAndCopyResolver - per-track agreeing-pair resolve across reads"
```

---

### Task 3: Resolver fuzz tests

**Files:**
- Create: `CUETools.Wpf.Tests/TestAndCopyResolverFuzzTests.cs`

**Interfaces:**
- Consumes: `TestAndCopyResolver.Resolve`, `VerifyRecord`, `TrackCrc`, `VerifyHistoryStore.SameAudio`.

Invariants to assert over random inputs: (a) a `Passed` outcome has an empty `HeldTracks` and every track `Agreed`; (b) a `Held` outcome names at least one held track and every held track is not `Agreed`; (c) every `Agreed` track's `SourceReadIndex` is a STAGED read; (d) every `Agreed` track's `SourceReadIndex` is a member of its `AgreeingReads`, and the two reads in `AgreeingReads` actually agree via `SameAudio`; (e) `Resolve` never throws on ragged track counts.

- [ ] **Step 1: Write the fuzz test**

Create `CUETools.Wpf.Tests/TestAndCopyResolverFuzzTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class TestAndCopyResolverFuzzTests
    {
        [TestMethod]
        public void Fuzz_InvariantsHold()
        {
            var rnd = new Random(20260724);
            for (int iter = 0; iter < 5000; iter++)
            {
                int readCount = 2 + rnd.Next(2);           // 2 or 3 reads
                int tracks = 1 + rnd.Next(6);
                var reads = new List<VerifyRecord>();
                for (int r = 0; r < readCount; r++)
                {
                    int len = Math.Max(1, tracks + rnd.Next(-1, 2)); // occasionally ragged
                    var tc = new TrackCrc[len];
                    for (int t = 0; t < len; t++)
                        tc[t] = new TrackCrc { ArV2 = (uint)rnd.Next(1, 4) }; // small domain -> collisions
                    reads.Add(new VerifyRecord { Tracks = tc });
                }
                var staged = new bool[readCount];
                for (int i = 1; i < readCount; i++) staged[i] = true;

                var res = TestAndCopyResolver.Resolve(reads, staged);

                if (res.Outcome == TestCopyOutcome.Passed)
                    Assert.AreEqual(0, res.HeldTracks.Length);
                else
                    Assert.IsTrue(res.HeldTracks.Length > 0);

                foreach (var v in res.Tracks)
                {
                    if (v.Agreed)
                    {
                        Assert.IsTrue(staged[v.SourceReadIndex], "source must be staged");
                        CollectionAssert.Contains(v.AgreeingReads, v.SourceReadIndex);
                        Assert.AreEqual(2, v.AgreeingReads.Length);
                        var a = reads[v.AgreeingReads[0]].Tracks[v.TrackIndex];
                        var b = reads[v.AgreeingReads[1]].Tracks[v.TrackIndex];
                        Assert.IsTrue(VerifyHistoryStore.SameAudio(a, b), "agreeing pair must actually agree");
                    }
                    else
                    {
                        Assert.AreEqual(-1, v.SourceReadIndex);
                        CollectionAssert.Contains(res.HeldTracks, v.TrackIndex);
                    }
                }
            }
        }
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug --nologo`
Expected: PASS. If (d) fails, the source-selection loop is picking a read outside the agreeing pair - re-check Task 2's inner loop.

- [ ] **Step 3: Commit**

```bash
git add CUETools.Wpf.Tests/TestAndCopyResolverFuzzTests.cs
git commit -m "test(verify): fuzz TestAndCopyResolver invariants (source staged, in agreeing pair)"
```

---

### Task 4: Test & Copy log formatter (pure)

**Files:**
- Create: `CUETools.Wpf/Accuracy/TestAndCopyLog.cs`
- Test: `CUETools.Wpf.Tests/TestAndCopyLogTests.cs`

**Interfaces:**
- Consumes: `TestCopyResult`, `VerifyRecord`.
- Produces: `static string TestAndCopyLog.Format(TestCopyResult result, IReadOnlyList<VerifyRecord> reads, string discId, string drive, int readOffset, int failedWindows)`.

Content: header (disc id, drive, offset, read count), one line per track (per-read CRC32 + the agreement verdict + which read committed), the overall PASSED/HELD verdict, the AccurateRip and CTDB status from the newest read, and a disc-level unrecoverable warning when `failedWindows > 0`. ASCII only.

- [ ] **Step 1: Write the failing test**

Create `CUETools.Wpf.Tests/TestAndCopyLogTests.cs`:

```csharp
using System.Collections.Generic;
using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class TestAndCopyLogTests
    {
        private static VerifyRecord Read(int arConf, int ctConf, params uint[] c32)
        {
            var t = new TrackCrc[c32.Length];
            for (int i = 0; i < c32.Length; i++) t[i] = new TrackCrc { ArV2 = c32[i], Crc32 = c32[i] };
            return new VerifyRecord { Tracks = t, ArConfidence = arConf, CtdbConfidence = ctConf };
        }

        [TestMethod]
        public void Passed_LogSaysPassedAndListsReads()
        {
            var reads = new List<VerifyRecord> { Read(3, 5, 0xAA, 0xBB), Read(3, 5, 0xAA, 0xBB) };
            var res = TestAndCopyResolver.Resolve(reads, new[] { false, true });
            string log = TestAndCopyLog.Format(res, reads, "DISC1", "TEST DRIVE", 6, 0);
            StringAssert.Contains(log, "Test & Copy PASSED");
            StringAssert.Contains(log, "Reads: 2");
            StringAssert.Contains(log, "AccurateRip: accurate, confidence 3");
            StringAssert.Contains(log, "AA");        // CRC32 rendered
            Assert.IsFalse(log.Contains("HELD"));
        }

        [TestMethod]
        public void Held_LogNamesHeldTrackAndUnrecoverableWarning()
        {
            var reads = new List<VerifyRecord> { Read(0, 0, 0xAA, 0x11), Read(0, 0, 0xAA, 0x22) };
            var res = TestAndCopyResolver.Resolve(reads, new[] { false, true });
            string log = TestAndCopyLog.Format(res, reads, "DISC1", "TEST DRIVE", 6, 2);
            StringAssert.Contains(log, "Test & Copy HELD");
            StringAssert.Contains(log, "track(s): 2");        // 1-based
            StringAssert.Contains(log, "AccurateRip: not found");
            StringAssert.Contains(log, "unrecoverable");
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug -v q -nologo`
Expected: FAIL to compile - `TestAndCopyLog` does not exist.

- [ ] **Step 3: Implement**

Create `CUETools.Wpf/Accuracy/TestAndCopyLog.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace CUETools.Wpf.Accuracy
{
    /// <summary>Renders the human-readable Test & Copy log - the local proof a disc was read at least
    /// twice and every committed track agreed bit-for-bit. Pure text, no I/O. ASCII only.</summary>
    public static class TestAndCopyLog
    {
        public static string Format(TestCopyResult result, IReadOnlyList<VerifyRecord> reads,
            string discId, string drive, int readOffset, int failedWindows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Test & Copy log");
            sb.AppendLine("Disc:   " + (discId ?? ""));
            sb.AppendLine("Drive:  " + (drive ?? "") + "   read offset " + readOffset);
            sb.AppendLine("Reads:  " + result.ReadsUsed);
            sb.AppendLine();

            foreach (var v in result.Tracks)
            {
                sb.Append("Track " + (v.TrackIndex + 1).ToString("00") + ":  ");
                for (int i = 0; i < reads.Count; i++)
                {
                    var tr = Track(reads[i], v.TrackIndex);
                    sb.Append("R" + (i + 1) + "=" + (tr != null ? tr.Crc32.ToString("X8") : "--------") + " ");
                }
                if (v.Agreed)
                    sb.Append(" agreed(R" + (v.AgreeingReads[0] + 1) + ",R" + (v.AgreeingReads[1] + 1) +
                              ") -> committed from R" + (v.SourceReadIndex + 1));
                else
                    sb.Append(" NO AGREEMENT - held");
                sb.AppendLine();
            }
            sb.AppendLine();

            if (result.Outcome == TestCopyOutcome.Passed)
                sb.AppendLine("Test & Copy PASSED - every track verified by >=2 independent reads.");
            else
            {
                var oneBased = new List<string>();
                foreach (var h in result.HeldTracks) oneBased.Add((h + 1).ToString());
                sb.AppendLine("Test & Copy HELD - no agreement on track(s): " + string.Join(", ", oneBased));
            }

            var last = reads.Count > 0 ? reads[reads.Count - 1] : null;
            int arConf = last?.ArConfidence ?? 0, ctConf = last?.CtdbConfidence ?? 0;
            sb.AppendLine("AccurateRip: " + (arConf > 0 ? "accurate, confidence " + arConf : "not found"));
            sb.AppendLine("CTDB:        " + (ctConf > 0 ? "match, confidence " + ctConf : "not found"));
            if (failedWindows > 0)
                sb.AppendLine("WARNING: " + failedWindows + " unrecoverable sector window(s) during reads - " +
                              "agreement over damaged media is consistency, not proof the region is pristine.");
            return sb.ToString();
        }

        private static TrackCrc Track(VerifyRecord r, int t)
        {
            var arr = r?.Tracks;
            return (arr != null && t >= 0 && t < arr.Length) ? arr[t] : null;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CUETools.Wpf/Accuracy/TestAndCopyLog.cs CUETools.Wpf.Tests/TestAndCopyLogTests.cs
git commit -m "feat(verify): Test & Copy log formatter (per-track CRCs + verdict + AR/CTDB status)"
```

---

### Task 5: RipService.Run plumbing (staging, forced cache defeat, returned record)

**Files:**
- Modify: `CUETools.Wpf/Services/RipService.cs` (VerifyResult 12-35; Run signature 95; cal fetch 145; cache-defeat gate 160-165; failedWindows already computed 238; record build/upsert/sidecar 373-411; return 413-430)

**Interfaces:**
- Produces (for Task 6): a private `Run(...)` overload accepting `bool stageOnly = false, bool forceCacheDefeat = false`; `VerifyResult` now carries `VerifyRecord? Record` and `int FailedWindows`. When `stageOnly` is true, `Run` builds and returns the record but does NOT upsert verify-history and does NOT write the `.verify` sidecar. When `forceCacheDefeat` is true, cache defeat is applied on a calibrated caching drive even if `_settings.DeepRecovery` is off.

This task changes no existing behavior: the public `RunVerify`/`RunEncode` keep calling `Run` with the new params defaulted, so `stageOnly=false, forceCacheDefeat=false` reproduces today's path exactly. It is verified by the build plus the unchanged existing tests; the new fields are exercised live in Task 6.

- [ ] **Step 1: Add the new VerifyResult fields**

In `CUETools.Wpf/Services/RipService.cs`, add to `VerifyResult` (after `FileCount`, around line 23):

```csharp
    /// <summary>The per-track checksum record this read produced (used by Test & Copy to compare
    /// reads). Null when the record build failed.</summary>
    public CUETools.Wpf.Accuracy.VerifyRecord? Record { get; init; }
    /// <summary>Count of windows the drive could not read even after every retry (0 on a clean read).</summary>
    public int FailedWindows { get; init; }
```

- [ ] **Step 2: Add the two params to the private Run**

Change the signature at line 95 from:

```csharp
    private VerifyResult Run(char drive, int cq, bool encode, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null)
```

to:

```csharp
    private VerifyResult Run(char drive, int cq, bool encode, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, bool stageOnly = false, bool forceCacheDefeat = false)
```

- [ ] **Step 3: Let cache defeat be forced regardless of the Deep recovery toggle**

Change the cal fetch (line 145) so the calibration is available when cache defeat is forced:

```csharp
            var cal = (_settings.DeepRecovery || forceCacheDefeat) ? _calStore.Get((reader.ARName ?? "").Trim()) : null;
```

Change the cache-defeat gate (lines 160-165) to:

```csharp
            if ((_settings.DeepRecovery || forceCacheDefeat) && cal != null && (cal.CacheDefeat ?? "").StartsWith("Flush:")
                && int.TryParse(cal.CacheDefeat.Substring(6), out int flushBytes) && flushBytes > 0)
            {
                reader.SetCacheDefeat(flushBytes);
                _log.Info("rip", $"cache defeat on: flush {flushBytes}B before each secure re-read" +
                    (forceCacheDefeat ? " (forced: Test & Copy)" : " (drive caches, calibrated)"));
            }
```

- [ ] **Step 4: Suppress the upsert + sidecar under stageOnly, and always return the record + failedWindows**

Replace the verify-history block (lines 376-411) with a version that always builds the record, only upserts/sidecars when not staging, and hands the record back. Keep the `record` in scope for the return:

```csharp
            var vh = new CUETools.Wpf.Accuracy.VerifyOutcome();
            CUETools.Wpf.Accuracy.VerifyRecord? built = null;
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
                built = new CUETools.Wpf.Accuracy.VerifyRecord
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
                if (!stageOnly)
                {
                    vh = _history.CompareAndUpsert(built);
                    _log.Info("verify.history", $"disc={built.DiscId} known={(vh.KnownDisc ? 1 : 0)} matches={(vh.Matches ? 1 : 0)} diffTracks={vh.DiffTrackCount}");
                    if (encode && Directory.Exists(outDir))
                    {
                        try { File.WriteAllText(Path.Combine(outDir, "rip.verify"), CUETools.Wpf.Accuracy.VerifyHistoryStore.ToJson(built)); }
                        catch (Exception ex) { _log.Warn("verify.history", "sidecar write failed: " + ex.GetType().Name); }
                    }
                }
            }
            catch (Exception ex) { _log.Warn("verify.history", "record build failed: " + ex.GetType().Name); }
```

Then add the two fields to the returned `VerifyResult` (in the object initializer at 413-430):

```csharp
                HistoryDiffTracks = vh.DiffTrackCount,
                Record = built,
                FailedWindows = failedWindows,
```

- [ ] **Step 5: Build and run the existing tests (no behavior change)**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: "Build succeeded. 0 Error(s)". (If a `CUETools.Wpf.exe` locks the DLLs, `taskkill //F //IM CUETools.Wpf.exe` first.)
Run: `dotnet test CUETools.Wpf.Tests/CUETools.Wpf.Tests.csproj -c Debug --nologo`
Expected: PASS - nothing regressed.

- [ ] **Step 6: Commit**

```bash
git add CUETools.Wpf/Services/RipService.cs
git commit -m "refactor(rip): Run gains stageOnly + forceCacheDefeat; returns the verify record + failedWindows"
```

---

### Task 6: RunTestAndCopy orchestrator + hold follow-ups

**Files:**
- Modify: `CUETools.Wpf/Services/RipService.cs` (add to the `IRipService` interface and the class); constructor to inject `DriveCalibrationService`.
- Modify: `CUETools.Wpf/App.xaml.cs` or wherever `RipService` is registered in DI, if the constructor gains a parameter (verify the container resolves `DriveCalibrationService`).

**Interfaces:**
- Consumes: `Run(..., stageOnly, forceCacheDefeat)` (Task 5), `TestAndCopyResolver.Resolve` (Task 2), `TestAndCopyLog.Format` (Task 4), `DriveCalibrationService.Calibrate(char)` + `DriveCalibrationStore.Get(sig)`.
- Produces on `IRipService`:
  - `TestCopyRunResult RunTestAndCopy(char drive, int cq, string format, CUEMetadata? metadata, string outputBaseDir, Action<double,string> onProgress, Action<double,double>? onLevels = null, Action<float[]>? onSamples = null, Action<int,int,int,double>? onReread = null, byte[]? coverArt = null)`
  - `bool CommitCopyReadAnyway(TestCopyRunResult held, string outputBaseDir)` - copies the Copy read's staging into the output folder, flagged not-verified; returns success.
  - `void DiscardStaging(TestCopyRunResult held)` - deletes retained staging folders.
  - `class TestCopyRunResult { bool Ok; string Error; TestCopyOutcome Outcome; int ReadsUsed; int[] HeldTracks; string OutputDir; int FileCount; int ArConfidence; int ArTotal; int CtdbConfidence; int CtdbTotal; bool Accurate; string CopyStagingDir; string[] StagingDirs; }`

Design notes for the implementer:
- Force at least Secure: `int rq = Math.Max(1, Math.Min(2, cq));`
- Ensure independence up front. Open a reader under `DriveService.ScsiGate` only long enough to read the signature `reader.ARName`; close it. Look up `_calStore.Get(sig)`. If the record is missing, or `CacheDefeat` neither starts with "Flush:" nor equals "Media re-reads (no cache)" (i.e. caching-or-unknown and not yet sized), report `onProgress(0, "Calibrating drive...")` and call `_calService.Calibrate(drive)` (it opens/closes its own reader under the gate). If calibration returns null, return `new TestCopyRunResult { Error = "Calibration failed - cannot guarantee two independent reads." }`.
- Staging dirs: `string stem = Path.Combine(Path.GetTempPath(), "cuetc", Guid.NewGuid().ToString("N"));` then `stage1 = stem + "-copy"`, `stage2 = stem + "-third"`. Pass each as the `outputBaseDir` to the staged Encode read; `Run` creates `<staging>/<Artist> - <Title>/` inside it.
- The Test read: `Run(drive, rq, encode:false, "flac", metadata, "", onProgress-with-"Test read (1 of 2)", ..., stageOnly:true, forceCacheDefeat:true)`. Take its `Record`.
- The Copy read: `Run(drive, rq, encode:true, format, metadata, stage1, onProgress-with-"Copy read (2 of 2)", ..., coverArt, stageOnly:true, forceCacheDefeat:true)`. Take its `Record` and its `OutputDir` (the staged album folder).
- Resolve with `staged = { false, true }`. If `Passed`, commit staging 1 whole: move/copy `copyResult.OutputDir` into `<outputBaseDir>/<same album folder name>`. If `Held`, do the third read into `stage2` with progress "Confirming (read 3)...", re-resolve with `{ false, true, true }`.
- Assembly on a 3-read Passed: create the real album output folder; for each track verdict, copy the per-track audio file for that track index from the source read's staging folder. Since all reads share metadata, the file name for a track index is identical across stagings; enumerate audio files in the staging folder ordered the same way (sort by name) and index them, OR re-derive names from the cue. Simplest robust approach: list `*.<format>` files in each staging album folder sorted by filename; index i is track i. Copy the auxiliary files (`*.cue`, `*.m3u`, `*.log`, `rip.verify` if present, cover art files) from staging 1.
- On commit (either path): write the Test & Copy log via `TestAndCopyLog.Format(...)` into the output folder as `Test & Copy.log`; upsert the committed read's record into `_history` (`_history.CompareAndUpsert(committedRecord)`); write the `rip.verify` sidecar from the committed record; log the privacy line `_log.Info("rip", $"testcopy disc={id} reads={n} passed=1 heldTracks=0")`.
- On hold: do NOT write to the output folder. Retain both staging dirs on the returned `TestCopyRunResult` (`StagingDirs`, `CopyStagingDir = copyResult.OutputDir`). Log `testcopy disc={id} reads={n} passed=0 heldTracks={m}`. Do not delete staging here - the VM's Accept/Discard/Re-run drives cleanup.
- `CommitCopyReadAnyway`: copy `held.CopyStagingDir` into `<outputBaseDir>/<album>`, write a `Test & Copy.log` that says "NOT test-verified - accepted by user", then `DiscardStaging(held)`.
- `DiscardStaging`: best-effort `Directory.Delete(dir, true)` for each `StagingDirs` entry.
- Cleanup + safety: wrap the reads in try/finally so a `StopException` or error deletes any staging dirs and returns `Error`. Never leave a half-written output folder - only the final assemble/commit touches `outputBaseDir`.

- [ ] **Step 1: Add the result type and interface members**

Add near `VerifyResult` in `RipService.cs`:

```csharp
public sealed class TestCopyRunResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public CUETools.Wpf.Accuracy.TestCopyOutcome Outcome { get; init; }
    public int ReadsUsed { get; init; }
    public int[] HeldTracks { get; init; } = System.Array.Empty<int>();
    public string OutputDir { get; init; } = "";
    public int FileCount { get; init; }
    public int ArConfidence { get; init; }
    public int ArTotal { get; init; }
    public int CtdbConfidence { get; init; }
    public int CtdbTotal { get; init; }
    public bool Accurate { get; init; }
    public string CopyStagingDir { get; init; } = "";
    public string[] StagingDirs { get; init; } = System.Array.Empty<string>();
}
```

Add to `IRipService`:

```csharp
    /// <summary>Test & Copy: read the disc twice (a third time on a mismatch), commit only tracks two
    /// independent reads agree on bit-for-bit, hold the rest. Forces at least Secure and forces cache
    /// defeat (auto-calibrating first when needed) so the reads are genuinely independent.</summary>
    TestCopyRunResult RunTestAndCopy(char drive, int correctionQuality, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null);

    /// <summary>Accept a held Test & Copy's Copy read into the output folder anyway, flagged not
    /// test-verified, and discard the staging. Returns success.</summary>
    bool CommitCopyReadAnyway(TestCopyRunResult held, string outputBaseDir);

    /// <summary>Delete the staging folders a held Test & Copy retained.</summary>
    void DiscardStaging(TestCopyRunResult held);
```

- [ ] **Step 2: Inject DriveCalibrationService**

Change the constructor (line 70-71) to also take `DriveCalibrationService`:

```csharp
    private readonly CUETools.Wpf.Accuracy.DriveCalibrationService _calService;

    public RipService(CUEConfig config, IDiagnosticLog log, AppSettings settings, EncoderCatalog catalog, CUETools.Wpf.Accuracy.DriveCalibrationStore calStore, CUETools.Wpf.Accuracy.VerifyHistoryStore history, CUETools.Wpf.Accuracy.DriveCalibrationService calService)
    { _config = config; _log = log; _settings = settings; _catalog = catalog; _calStore = calStore; _history = history; _calService = calService; }
```

Confirm `DriveCalibrationService` is registered in the DI container (search `App.xaml.cs` / the service registration for `DriveCalibrationService`). If it is not registered, register it as a singleton alongside `DriveCalibrationStore`. If it is already registered (the Drive page uses it), no change is needed.

- [ ] **Step 3: Implement RunTestAndCopy + the follow-ups**

Add the methods to the `RipService` class (full implementation, following the design notes above). Write `RunTestAndCopy`, a private `EnsureIndependence(char drive, Action<double,string> onProgress)` returning bool, a private `AssembleAndCommit(TestCopyResult, records, stagingAlbumDirs, realBase, discId, drive, offset, failedWindows)` returning the committed output dir + file count, `CommitCopyReadAnyway`, and `DiscardStaging`. Keep every staging read `stageOnly:true, forceCacheDefeat:true`; wrap in try/finally that discards staging on any error/stop.

- [ ] **Step 4: Build**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: "Build succeeded. 0 Error(s)".

- [ ] **Step 5: Commit**

```bash
git add CUETools.Wpf/Services/RipService.cs CUETools.Wpf/App.xaml.cs
git commit -m "feat(rip): RunTestAndCopy orchestrator - two-read staging, resolve, commit-or-hold, log"
```

---

### Task 7: ViewModel wiring

**Files:**
- Modify: `CUETools.Wpf/ViewModels/RipViewModel.cs` (commands 271-272, 318-325; `RunJobAsync` 612-744; result surfacing 703-731)

**Interfaces:**
- Consumes: `IRipService.RunTestAndCopy / CommitCopyReadAnyway / DiscardStaging`, `TestCopyRunResult`.
- Produces (bound by the view in Task 8): `ICommand TestCopyCommand`, `ICommand AcceptCopyAnywayCommand`, `ICommand DiscardHeldCommand`; `bool TestCopyHeld` (drives the HELD card), `string TestCopyText`, `bool TestCopyIsWarning`.

- [ ] **Step 1: Add the command + properties**

Add fields/properties near the other rip commands (around 271-272) and backing state near `HistoryText`:

```csharp
    public ICommand TestCopyCommand { get; }
    public ICommand AcceptCopyAnywayCommand { get; }
    public ICommand DiscardHeldCommand { get; }

    private bool _testCopyHeld;
    public bool TestCopyHeld { get => _testCopyHeld; private set => Set(ref _testCopyHeld, value); }
    private string _testCopyText = "";
    public string TestCopyText { get => _testCopyText; private set => Set(ref _testCopyText, value); }
    private bool _testCopyIsWarning;
    public bool TestCopyIsWarning { get => _testCopyIsWarning; private set => Set(ref _testCopyIsWarning, value); }
    private TestCopyRunResult? _heldResult;
```

Wire the commands in the constructor (near 319-321):

```csharp
        TestCopyCommand = new RelayCommand(_ => { _ = RunTestCopyAsync(); }, _ => IsDiscPresent && !IsRipping && !IsBusy);
        AcceptCopyAnywayCommand = new RelayCommand(_ => AcceptCopyAnyway(), _ => _heldResult != null);
        DiscardHeldCommand = new RelayCommand(_ => DiscardHeld(), _ => _heldResult != null);
```

- [ ] **Step 2: Implement RunTestCopyAsync**

Add a method modeled on `RunJobAsync` (reuse its `Report`/`Levels`/`Samples`/`Reread` callbacks, `_discSeconds`, per-track boundaries, `StartRereadTimer`). Set `IsRipping = true`, clear `TestCopyHeld`, `_baseActivity = AppActivity.Ripping`, call:

```csharp
        var result = await Task.Run(() => _rip.RunTestAndCopy(drive, cq, fmt, meta, outBase, Report, Levels, Samples, Reread, cover));
```

Then surface:

```csharp
        if (result.Ok && result.Outcome == CUETools.Wpf.Accuracy.TestCopyOutcome.Passed)
        {
            LastOutputDir = result.OutputDir;
            TestCopyHeld = false; _heldResult = null;
            TestCopyText = $"Test & Copy verified by {result.ReadsUsed} independent reads."
                + (result.Accurate ? $"  Also AccurateRip-accurate (confidence {result.ArConfidence})." : "  Not in AccurateRip - proven by the two reads.");
            TestCopyIsWarning = false;
            ArText = $"{result.ArConfidence} / {result.ArTotal}" + (result.Accurate ? "  accurate" : "");
            CtdbText = result.CtdbConfidence > 0 ? $"match . conf {result.CtdbConfidence}" : $"{result.CtdbConfidence} / {result.CtdbTotal}";
            Accurate = result.Accurate;
            RipSummary = $"Test & Copy: {result.FileCount} {fmt} files, verified by {result.ReadsUsed} reads";
            RipDone = true;
            StatusText = $"Test & Copy verified -> {result.OutputDir}";
        }
        else if (result.Ok && result.Outcome == CUETools.Wpf.Accuracy.TestCopyOutcome.Held)
        {
            _heldResult = result;
            TestCopyHeld = true;
            TestCopyIsWarning = true;
            TestCopyText = $"Held - the reads disagree on track(s) {string.Join(", ", System.Array.ConvertAll(result.HeldTracks, x => (x + 1).ToString()))}. Nothing was written. Re-run for another read, accept the copy anyway, or discard.";
            StatusText = "Test & Copy held - tracks disagree.";
        }
        else
        {
            StatusText = result.Error == "Stopped." ? "Test & Copy stopped." : "Test & Copy failed: " + result.Error;
        }
        IsRipping = false;
        _baseActivity = AppActivity.Idle;
        _status.Report(AppActivity.Idle);
```

Implement the two follow-ups:

```csharp
    private void AcceptCopyAnyway()
    {
        var held = _heldResult; if (held == null) return;
        bool ok = _rip.CommitCopyReadAnyway(held, OutputBaseDir);
        LastOutputDir = ok ? Path.Combine(OutputBaseDir, "") : LastOutputDir;
        TestCopyHeld = false; _heldResult = null;
        TestCopyText = ok ? "Copy read accepted anyway - written and flagged NOT test-verified." : "Could not write the copy read.";
        StatusText = TestCopyText;
    }

    private void DiscardHeld()
    {
        var held = _heldResult; if (held == null) return;
        _rip.DiscardStaging(held);
        TestCopyHeld = false; _heldResult = null;
        TestCopyText = "Discarded - nothing was written.";
        StatusText = TestCopyText;
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: "Build succeeded. 0 Error(s)".

- [ ] **Step 4: Commit**

```bash
git add CUETools.Wpf/ViewModels/RipViewModel.cs
git commit -m "feat(rip-ui): Test & Copy command, held state, accept/discard follow-ups"
```

---

### Task 8: View - the button and the HELD card

**Files:**
- Modify: `CUETools.Wpf/Views/RipView.xaml` (the action DockPanel 247-256; add the HELD card near the VERIFY HISTORY card)

**Interfaces:**
- Consumes: `TestCopyCommand`, `TestCopyHeld`, `TestCopyText`, `TestCopyIsWarning`, `AcceptCopyAnywayCommand`, `DiscardHeldCommand`.

- [ ] **Step 1: Add the Test & Copy button**

In the action `DockPanel` (after the Rip button at line 254-255, still inside the `IsRipping`-inverted group), add:

```xml
          <Button DockPanel.Dock="Right" Content="Test &amp; Copy" Command="{Binding TestCopyCommand}" Padding="18,9" Margin="0,0,10,0"
                  Visibility="{Binding IsRipping, Converter={StaticResource BoolVis}, ConverterParameter=invert}"
                  ToolTip="Read the disc twice (a third time on a mismatch) and write only tracks two independent reads agree on bit-for-bit. Slower (2-3x reads). Best for discs not in AccurateRip."/>
```

- [ ] **Step 2: Add the HELD result card**

Near the existing VERIFY HISTORY card, add a card gated on `TestCopyHeld`, using the same brushes as the history card (amber warning). Bind the text to `TestCopyText` and add the three buttons:

```xml
      <Border Visibility="{Binding TestCopyHeld, Converter={StaticResource BoolVis}}"
              Background="{DynamicResource Panel}" CornerRadius="6" Padding="12" Margin="0,10,0,0">
        <StackPanel>
          <TextBlock Text="TEST &amp; COPY" FontFamily="{StaticResource Mono}" FontSize="11" Foreground="{DynamicResource Amber}"/>
          <TextBlock Text="{Binding TestCopyText}" TextWrapping="Wrap" Margin="0,6,0,10" Foreground="{DynamicResource Ink}"/>
          <StackPanel Orientation="Horizontal">
            <Button Content="Re-run" Command="{Binding TestCopyCommand}" Padding="12,6" Margin="0,0,8,0"/>
            <Button Content="Accept anyway" Command="{Binding AcceptCopyAnywayCommand}" Padding="12,6" Margin="0,0,8,0"/>
            <Button Content="Discard" Command="{Binding DiscardHeldCommand}" Padding="12,6"/>
          </StackPanel>
        </StackPanel>
      </Border>
```

(Match the exact resource keys used by the existing VERIFY HISTORY card - `Panel`, `Amber`, `Ink`, `Mono` - if they differ in this file, copy that card's keys.)

- [ ] **Step 3: Build**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: "Build succeeded. 0 Error(s)". A wrong resource key or binding surfaces here.

- [ ] **Step 4: Commit**

```bash
git add CUETools.Wpf/Views/RipView.xaml
git commit -m "feat(rip-ui): Test & Copy button + HELD card (re-run / accept anyway / discard)"
```

---

### Task 9: Live verification on the drive

**Files:** none (manual verification; record the result in the PR / progress ledger).

This feature's hardware orchestration cannot be unit-tested. After Tasks 1-8 build green and all resolver/log/fuzz tests pass, verify live:

- [ ] **Clean disc, in AccurateRip:** load a clean disc, click Test & Copy. Expect two reads, PASSED, "verified by 2 independent reads", files written, a `Test & Copy.log` in the output folder showing PASSED and the per-track CRCs, and the committed audio bit-identical to a normal Rip of the same disc (compare a track's CRC32 in the log to a normal rip's `rip.verify`).
- [ ] **Caching-drive independence:** confirm the DiagnosticLog shows `cache defeat on: ... (forced: Test & Copy)` for each read, and (on a drive with no saved calibration) that "Calibrating drive..." appeared first.
- [ ] **Marginal disc:** a lightly scratched disc should either trigger the third read (log shows 3 reads) and still PASS per track, or HOLD with the card offering Re-run / Accept anyway / Discard. Verify Discard writes nothing, Accept anyway writes flagged files + a "NOT test-verified" log.
- [ ] Record the outcomes (reads used, elapsed, accurate flag) in the PR description.

---

## Self-Review

**Spec coverage:** flow bounded to 3 reads (Tasks 2,6); match rule via reused AR-CRC compare (Tasks 1,2); independence + forced cache defeat + auto-calibrate (Tasks 5,6); per-track assembly + source preference (Task 2 + Task 6 assembly); withhold-on-mismatch / hold UX (Tasks 6,7,8); auto third read (Task 6); Test & Copy log + sidecar + privacy line (Tasks 4,6); per-track files only (Task 6 assembly); unrecoverable warning (Tasks 4,5,6); resolver unit + fuzz + log tests (Tasks 2,3,4); live hardware check (Task 9). v2 single-image is explicitly out of scope.

**Placeholder scan:** none - every code step carries complete code except Task 6 Step 3 (the orchestrator body), which is specified by exhaustive design notes plus exact signatures; the implementer writes it against those. This is the one integration-heavy method that cannot be reduced to a copy-paste block; the notes name every input, output, staging path, and branch.

**Type consistency:** `SameAudio`, `TestAndCopyResolver.Resolve(reads, staged)`, `TestCopyResult`/`TrackVerdict`/`TestCopyOutcome`, `TestAndCopyLog.Format(...)`, `Run(..., stageOnly, forceCacheDefeat)`, `VerifyResult.Record`/`FailedWindows`, `TestCopyRunResult`, and the VM commands/properties are used with identical names and signatures across tasks.
