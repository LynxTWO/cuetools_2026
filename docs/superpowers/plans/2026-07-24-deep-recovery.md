# Deep Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in "Deep recovery" rip mode that recovers more audio from damaged discs via a progress-aware re-read cap, slowing the drive to its floor on stuck windows, and a cross-correlation slip-recovery probe - without ever altering the default rip's output.

**Architecture:** A single `DeepRecovery` flag flows from an AppSettings toggle through RipService into `CDDriveReader`. When false, every code path below is bypassed and output is byte-identical to today. When true: (Part 1) `PrefetchSector` keeps re-reading a stuck window while its best error count improves (stop on plateau or time ceiling) and steps the drive down toward a probed minimum speed; (Part 2) on a persistent slip, `FetchSectors` cross-correlates raw reads and shifts them into alignment before the unchanged secure vote. The pure algorithms (progress decision, offset correlation, min-speed ladder) live in dependency-free helper classes with MSTest unit tests; the SCSI integration is verified against a clean disc (must stay AccurateRip-accurate) and the pinholed disc (recovery target).

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, MSTest v2, MMC SCSI via the existing `Device` wrapper. Build per project with `dotnet build` (no full Visual Studio).

## Global Constraints

- Deep recovery OFF must be byte-for-byte identical to today. The flag defaults to false. Verbatim invariant: no change to `CorrectSectors` (the vote), the accepted bytes, or the default cap when the flag is false.
- No em dashes / en dashes / typographic Unicode in code, comments, or UI copy. ASCII only: `" - "`, `->`, `~`, `<=`, `...`.
- DiagnosticLog and any new logging: numbers/structure only. Never album, artist, track, username, or unscrubbed path.
- SET CD SPEED is applied ONLY at a fresh-window boundary on the read thread (never mid-window - that crashed the drive). Reuse the existing pending-value path in `PrefetchSector`.
- Part 2 re-alignment must never let wrong data pass: the unchanged `(1 + correctionQuality)` clean-agreement vote remains the sole gate on accepted bytes.
- Constants (plateau = 8 passes, ceiling = 120 s, slip threshold = 0.85, min correlation strength) are named `const` fields, grouped and commented for retuning.
- Commit after each task. Push is a separate owner action (not part of tasks).

## File Structure

- `CUETools.Wpf/Services/AppSettings.cs` - add `DeepRecovery` bool.
- `CUETools.Wpf/ViewModels/RipViewModel.cs` - `DeepRecovery` bound property.
- `CUETools.Wpf/Views/RipView.xaml` - toggle next to the quality picker.
- `CUETools.Wpf/Services/RipService.cs` - set `reader.DeepRecovery`; slow-to-floor policy in the ReadProgress handler.
- `CUETools.Ripper.SCSI/RecoveryPolicy.cs` (new) - pure progress-aware cap decision. Dependency-free.
- `CUETools.Ripper.SCSI/SlipCorrelator.cs` (new) - pure raw-read cross-correlation offset finder. Dependency-free.
- `CUETools.Ripper.SCSI/SCSIDrive.cs` - `DeepRecovery` property; wire RecoveryPolicy into `PrefetchSector`; slow-to-floor stepping; min-speed probe; wire SlipCorrelator into `FetchSectors`.
- `CUETools.Wpf/Accuracy/DriveCalibration.cs` - add `MinSpeedKbps`.
- `CUETools.Wpf/Accuracy/DriveCalibrationService.cs` - probe + store min speed.
- `CUETools.Ripper.Tests/CUETools.Ripper.Tests.csproj` (new) - MSTest v2, `net8.0-windows`, references `CUETools.Ripper.SCSI`. Home for RecoveryPolicy + SlipCorrelator tests.

---

### Task 1: Deep recovery flag + toggle (plumbing, no behavior yet)

**Files:**
- Modify: `CUETools.Wpf/Services/AppSettings.cs` (after line 34, the AdaptiveReadSpeed property)
- Modify: `CUETools.Wpf/ViewModels/RipViewModel.cs` (near the other bool options ~line 225-227)
- Modify: `CUETools.Wpf/Views/RipView.xaml` (near the quality ComboBox at line 236-241)
- Modify: `CUETools.Ripper.SCSI/SCSIDrive.cs` (add property near `_correctionQuality`, line 47)
- Modify: `CUETools.Wpf/Services/RipService.cs` (in `Run`, after `reader.CorrectionQuality = ...` ~line 101)

**Interfaces:**
- Produces: `AppSettings.DeepRecovery` (bool), `RipViewModel.DeepRecovery` (bool, two-way), `CDDriveReader.DeepRecovery` (bool auto-property, default false).

- [ ] **Step 1: Add the setting.** In `AppSettings.cs` after the `AdaptiveReadSpeed` property:

```csharp
    /// <summary>Deep recovery: an opt-in mode that recovers more audio from damaged discs by
    /// re-reading a stuck window while it is still improving (instead of a blind pass cap), slowing
    /// the drive to its floor on stuck spots, and re-aligning a persistent slip. Trades time for
    /// data. OFF by default - the default rip path is unchanged. The audio is only ever accepted by
    /// the same clean-agreement vote, so this can recover more but never corrupt.</summary>
    public bool DeepRecovery { get; set; } = false;
```

- [ ] **Step 2: Add the reader property.** In `SCSIDrive.cs`, next to `int _correctionQuality = 1;` (line 47), add a public auto-property:

```csharp
		/// <summary>Opt-in deep recovery (see AppSettings.DeepRecovery). When false the re-read loop
		/// and read path behave exactly as before. Set before the rip starts.</summary>
		public bool DeepRecovery { get; set; } = false;
```

- [ ] **Step 3: Add the VM property.** In `RipViewModel.cs`, alongside `CreateCue`/`WriteLog` (~line 225):

```csharp
    public bool DeepRecovery { get => _settings.DeepRecovery; set { if (_settings.DeepRecovery != value) { _settings.DeepRecovery = value; OnPropertyChanged(); } } }
```

- [ ] **Step 4: Add the toggle to the view.** In `RipView.xaml`, immediately after the quality ComboBox block (closes at line 241), inside the same options panel, add:

```xml
          <CheckBox DockPanel.Dock="Right" Margin="0,0,10,0" VerticalAlignment="Center"
                    Content="Deep recovery" IsChecked="{Binding DeepRecovery, Mode=TwoWay}"
                    ToolTip="Damaged discs only: re-read while still improving, slow the drive on stuck spots, re-align a persistent slip. Slower, recovers more. The audio is always agreement-checked, so it can only recover - never corrupt."/>
```

(If the panel is a horizontal DockPanel like the quality row, `DockPanel.Dock="Right"` keeps it on that row; adjust to `StackPanel` child if the surrounding container differs - match the sibling controls' container.)

- [ ] **Step 5: Pass the flag to the reader.** In `RipService.cs` `Run`, right after `reader.CorrectionQuality = Math.Max(0, Math.Min(2, cq));` (line 101):

```csharp
            reader.DeepRecovery = _settings.DeepRecovery;
            if (reader.DeepRecovery) _log.Info("rip", "deep recovery ON: progress-aware cap + slow-to-floor + slip probe");
```

- [ ] **Step 6: Build.**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit.**

```bash
git add CUETools.Wpf/Services/AppSettings.cs CUETools.Wpf/ViewModels/RipViewModel.cs CUETools.Wpf/Views/RipView.xaml CUETools.Ripper.SCSI/SCSIDrive.cs CUETools.Wpf/Services/RipService.cs
git commit -m "feat(deep-recovery): add opt-in Deep recovery flag + toggle (no behavior yet)"
```

---

### Task 2: Test project + RecoveryPolicy (pure progress-aware cap decision)

**Files:**
- Create: `CUETools.Ripper.Tests/CUETools.Ripper.Tests.csproj`
- Create: `CUETools.Ripper.Tests/RecoveryPolicyTests.cs`
- Create: `CUETools.Ripper.SCSI/RecoveryPolicy.cs`

**Interfaces:**
- Produces: `RecoveryPolicy` with `void StartWindow()`, `bool ShouldContinue(int currentErrors, double elapsedSeconds)` returning true while re-reading should continue, and consts `PlateauPasses = 8`, `CeilingSeconds = 120`. `ShouldContinue` is called once per completed pass; it returns false when errors==0, or best hasn't improved for PlateauPasses calls, or elapsedSeconds >= CeilingSeconds.

- [ ] **Step 1: Create the test project file.**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
    <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.6.1" />
    <PackageReference Include="MSTest.TestFramework" Version="3.6.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CUETools.Ripper.SCSI\CUETools.Ripper.SCSI.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing tests.** Create `RecoveryPolicyTests.cs`:

```csharp
using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests;

[TestClass]
public class RecoveryPolicyTests
{
    [TestMethod]
    public void StopsImmediatelyWhenConverged()
    {
        var p = new RecoveryPolicy(); p.StartWindow();
        Assert.IsFalse(p.ShouldContinue(0, 1.0), "errors==0 must stop");
    }

    [TestMethod]
    public void ContinuesWhileImproving()
    {
        var p = new RecoveryPolicy(); p.StartWindow();
        int[] seq = { 400, 300, 200, 150, 100, 60, 30, 10, 5, 2 };
        double t = 0;
        foreach (var e in seq) { t += 1; Assert.IsTrue(p.ShouldContinue(e, t), $"still improving at {e}"); }
    }

    [TestMethod]
    public void StopsAfterPlateau()
    {
        var p = new RecoveryPolicy(); p.StartWindow();
        // improve to 100, then flatline; best never beats 100 again
        p.ShouldContinue(100, 1);
        for (int i = 0; i < RecoveryPolicy.PlateauPasses - 1; i++)
            Assert.IsTrue(p.ShouldContinue(100, 2 + i), "within plateau window");
        Assert.IsFalse(p.ShouldContinue(100, 50), "plateau exhausted -> stop");
    }

    [TestMethod]
    public void PlateauResetsOnNewBest()
    {
        var p = new RecoveryPolicy(); p.StartWindow();
        p.ShouldContinue(100, 1);
        for (int i = 0; i < RecoveryPolicy.PlateauPasses - 2; i++) p.ShouldContinue(100, 2 + i);
        Assert.IsTrue(p.ShouldContinue(90, 20), "new best resets the counter");
        for (int i = 0; i < RecoveryPolicy.PlateauPasses - 1; i++)
            Assert.IsTrue(p.ShouldContinue(90, 30 + i));
        Assert.IsFalse(p.ShouldContinue(90, 60));
    }

    [TestMethod]
    public void StopsAtTimeCeiling()
    {
        var p = new RecoveryPolicy(); p.StartWindow();
        Assert.IsFalse(p.ShouldContinue(50, RecoveryPolicy.CeilingSeconds + 1), "over the ceiling -> stop even if improving");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail.**

Run: `dotnet test CUETools.Ripper.Tests/CUETools.Ripper.Tests.csproj -v q`
Expected: FAIL - `RecoveryPolicy` does not exist (compile error).

- [ ] **Step 4: Implement RecoveryPolicy.** Create `RecoveryPolicy.cs`:

```csharp
namespace CUETools.Ripper.SCSI;

/// <summary>Pure decision for the deep-recovery progress-aware cap. No SCSI, no state beyond the
/// window's best-error history, so it is unit-tested with no drive. Call StartWindow() when a new
/// window's re-reads begin, then ShouldContinue(currentErrors, elapsedSeconds) once per completed
/// pass. Returns false to stop re-reading. Never affects the vote or the accepted bytes.</summary>
public sealed class RecoveryPolicy
{
    public const int PlateauPasses = 8;      // stop after this many passes with no new best
    public const double CeilingSeconds = 120; // hard wall-clock stop per window

    private int _best = int.MaxValue;
    private int _sinceImproved;

    public void StartWindow() { _best = int.MaxValue; _sinceImproved = 0; }

    public bool ShouldContinue(int currentErrors, double elapsedSeconds)
    {
        if (currentErrors <= 0) return false;             // converged
        if (elapsedSeconds >= CeilingSeconds) return false; // time ceiling
        if (currentErrors < _best) { _best = currentErrors; _sinceImproved = 0; }
        else if (++_sinceImproved >= PlateauPasses) return false; // plateau
        return true;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass.**

Run: `dotnet test CUETools.Ripper.Tests/CUETools.Ripper.Tests.csproj -v q`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit.**

```bash
git add CUETools.Ripper.Tests CUETools.Ripper.SCSI/RecoveryPolicy.cs
git commit -m "feat(deep-recovery): RecoveryPolicy (progress-aware cap) + tests"
```

---

### Task 3: Wire the progress-aware cap into PrefetchSector

**Files:**
- Modify: `CUETools.Ripper.SCSI/SCSIDrive.cs` (`PrefetchSector`, the `for (int pass = 0; pass < max_scans; pass++)` loop at ~1333-1372)

**Interfaces:**
- Consumes: `RecoveryPolicy` (Task 2), `DeepRecovery` (Task 1).

- [ ] **Step 1: Add a policy field.** Near the reader's other fields (by `_thisPassErrors`, line ~48):

```csharp
		private readonly RecoveryPolicy _recovery = new RecoveryPolicy();
```

- [ ] **Step 2: Change the loop to be progress-aware when DeepRecovery is on.** Replace the loop header + the early-break. Today (SCSIDrive.cs ~1332-1334, 1370-1371):

```csharp
			int max_scans = 16 << _correctionQuality;
			for (int pass = 0; pass < max_scans; pass++)
			{
```
...and at the bottom of the loop:
```csharp
				if (pass >= _correctionQuality && _currentErrorsCount == 0)
					break;
```

Replace with:

```csharp
			int max_scans = 16 << _correctionQuality;
			// Deep recovery: the blind cap becomes a progress-aware one. RecoveryPolicy keeps us going
			// while the error count is still improving and stops on a plateau or a time ceiling. When
			// deep recovery is off, hard_cap stays at max_scans and the plateau logic never fires, so
			// this loop is identical to before.
			int hard_cap = DeepRecovery ? int.MaxValue : max_scans;
			var recoveryStart = DateTime.Now;
			if (DeepRecovery) _recovery.StartWindow();
			for (int pass = 0; pass < hard_cap; pass++)
			{
```
...and the early-break becomes:
```csharp
				if (pass >= _correctionQuality && _currentErrorsCount == 0)
					break;
				if (DeepRecovery && pass >= _correctionQuality &&
				    !_recovery.ShouldContinue(_currentErrorsCount, (DateTime.Now - recoveryStart).TotalSeconds))
					break;
```

Note: `_thisPassErrors`/`_currentErrorsCount` semantics are unchanged; we only read `_currentErrorsCount` to decide whether to keep looping. The vote is untouched.

- [ ] **Step 3: Build.**

Run: `dotnet build CUETools.Ripper.SCSI/CUETools.Ripper.SCSI.csproj -c Debug -v q -nologo`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Bit-exact guard (off path).** Confirm by inspection that with `DeepRecovery == false`: `hard_cap == max_scans`, `_recovery` is never called, and the second break never fires. The loop is identical to the original.

- [ ] **Step 5: Commit.**

```bash
git add CUETools.Ripper.SCSI/SCSIDrive.cs
git commit -m "feat(deep-recovery): progress-aware cap in PrefetchSector (gated by the flag)"
```

- [ ] **Step 6: Disc check (manual, owner-run).** With Deep recovery ON, verify the pinholed disc: the previously near-miss windows (min 2-5) should now converge (`converged=1` in rip.recovery), and slip/oscillating windows should stop on the plateau, not run away. With Deep recovery OFF, the run must match today's baseline.

---

### Task 4: Min-speed probe + slow-to-floor

**Files:**
- Modify: `CUETools.Wpf/Accuracy/DriveCalibration.cs` (add field, line ~24)
- Modify: `CUETools.Ripper.SCSI/SCSIDrive.cs` (min-speed probe method + slow-to-floor in `PrefetchSector`)
- Modify: `CUETools.Wpf/Accuracy/DriveCalibrationService.cs` (call the probe, store the result)
- Modify: `CUETools.Wpf/Services/RipService.cs` (feed the floor to the reader; step down on plateau)

**Interfaces:**
- Consumes: `RecoveryPolicy` improvement signal, existing SET CD SPEED pending path (`RequestReadSpeed`), `GetSupportedSpeeds()`.
- Produces: `DriveCalibration.MinSpeedKbps` (int); `CDDriveReader.ProbeMinSpeedKbps()` (int, read-only, returns the lowest accepted speed).

- [ ] **Step 1: Add the calibration field.** In `DriveCalibration.cs` after `MaxSpeedKbps` (line 24):

```csharp
    public int MinSpeedKbps { get; set; }   // lowest read speed the drive accepts (probed); 0 = unknown
```

- [ ] **Step 2: Implement the min-speed probe (read-only).** In `SCSIDrive.cs`, model it on the existing `DriveProbe`/`GetSupportedSpeeds` pattern: after `TestReadCommand()`, step SET CD SPEED down through the CD ladder (48x,24x,16x,12x,8x,4x,2x,1x = multiples of 176) and, for each, do one small read of a mid-disc audio sector; the lowest speed that both accepts the SET CD SPEED command (`CommandStatus.Success`) and returns a successful read is the floor. Return it in kB/s. Reads audio only, writes nothing, runs under the app SCSI gate outside a rip.

```csharp
		/// <summary>Read-only: find the lowest read speed the drive actually accepts, by stepping SET
		/// CD SPEED down and doing one probe read at each. Returns kB/s (multiple of 176), or the
		/// drive default (0) if none below max is accepted. Uses the autodetected read command.</summary>
		public unsafe int ProbeMinSpeedKbps()
		{
		    if (!TestReadCommand()) return 0;
		    int lba = (int)(_toc.AudioLength / 2);   // mid-disc
		    int floor = 0;
		    foreach (int x in new[] { 8, 4, 2, 1 })  // only try genuinely slow speeds
		    {
		        ushort v = (ushort)(x * 176);
		        if (m_device.SetCdSpeed(Device.RotationalControl.CLVandNonPureCav, v, v) != Device.CommandStatus.Success)
		            break;   // drive refused this speed; the previous one is the floor
		        if (!ReadOneProbeSector(lba)) break;   // could not read at this speed; stop
		        floor = v;
		    }
		    // restore a sane speed
		    try { var max = GetSupportedSpeeds(); if (max.Length > 0) m_device.SetCdSpeed(Device.RotationalControl.CLVandNonPureCav, (ushort)Math.Min(0xFFFE, max[max.Length-1]), (ushort)Math.Min(0xFFFE, max[max.Length-1])); } catch { }
		    return floor;
		}
```

`ReadOneProbeSector(int lba)` is a small helper that does one autodetected read of a single sector into a scratch buffer and returns whether the CommandStatus was Success (model on the existing FetchSectors dispatch: `_readCDCommand == ReadCDCommand.ReadCdBEh ? m_device.ReadCDAndSubChannel(...) : m_device.ReadCDDA(...)`, sizing the buffer 2352 + C2 per the detected mode). Add it as a private helper.

- [ ] **Step 3: Probe + store in calibration.** In `DriveCalibrationService.Calibrate`, after opening the reader and probing cache/max, also call `reader.ProbeMinSpeedKbps()` and set `cal.MinSpeedKbps`. Save through the store (existing pattern).

- [ ] **Step 4: Slow-to-floor during a stuck window.** In `RipService.cs` ReadProgress handler, extend the adaptive-speed policy: today it steps down one rung on `OnErrorCluster`. When `DeepRecovery` is on AND a window is still stuck after a step down, request the drive's floor (`cal.MinSpeedKbps` if known, else the lowest of `GetSupportedSpeeds()`), via the existing `reader.RequestReadSpeed(...)` (which the reader applies only at the next window boundary). Do NOT change speed mid-window.

```csharp
            // deep recovery: on a window that stays stuck, go all the way to the drive floor
            if (_settings.DeepRecovery && reReads > 0 && lastReReads > 0)
            {
                int floor = cal?.MinSpeedKbps > 0 ? cal.MinSpeedKbps : (speeds.Length > 0 ? speeds[0] : 0);
                if (floor > 0 && floor < lastRequested) { lastRequested = floor; reader.RequestReadSpeed(floor); }
            }
```

(Where `cal` is the `DriveCalibration` already fetched in RipService for cache defeat context; `speeds` is the array from `GetSupportedSpeeds()`.)

- [ ] **Step 5: Build both projects.**

Run: `dotnet build CUETools.Wpf/CUETools.Wpf.csproj -c Debug -v q -nologo`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit.**

```bash
git add CUETools.Wpf/Accuracy/DriveCalibration.cs CUETools.Ripper.SCSI/SCSIDrive.cs CUETools.Wpf/Accuracy/DriveCalibrationService.cs CUETools.Wpf/Services/RipService.cs
git commit -m "feat(deep-recovery): min-speed probe + slow-to-floor on stuck windows"
```

- [ ] **Step 7: Disc check (manual).** Calibrate the drive: the panel shows a min speed. On the pinholed disc with Deep recovery on, a persistently-stuck window should be seen (rip.speed log) dropping to the floor before it gives up.

---

### Task 5: SlipCorrelator (pure cross-correlation offset finder) + tests

**Files:**
- Create: `CUETools.Ripper.SCSI/SlipCorrelator.cs`
- Create: `CUETools.Ripper.Tests/SlipCorrelatorTests.cs`

**Interfaces:**
- Produces: `static (int offset, double strength) SlipCorrelator.FindOffset(short[] reference, short[] candidate, int maxShift)`. `offset` is the sample shift that best aligns `candidate` onto `reference`; `strength` in [0,1] is the normalized correlation at that offset. `MinStrength = 0.9` const: below it, treat as no reliable alignment (destruction).

- [ ] **Step 1: Write the failing tests.** Create `SlipCorrelatorTests.cs`:

```csharp
using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests;

[TestClass]
public class SlipCorrelatorTests
{
    private static short[] Ramp(int n, int start) { var a = new short[n]; for (int i = 0; i < n; i++) a[i] = (short)((start + i) * 7 % 4000 - 2000); return a; }

    [TestMethod]
    public void ZeroOffsetForIdentical()
    {
        var a = Ramp(2000, 0);
        var (off, str) = SlipCorrelator.FindOffset(a, (short[])a.Clone(), 64);
        Assert.AreEqual(0, off);
        Assert.IsTrue(str >= SlipCorrelator.MinStrength, $"identical should be strong, was {str}");
    }

    [TestMethod]
    public void DetectsPositiveShift()
    {
        var reference = Ramp(2000, 0);
        var candidate = Ramp(2000, 5);   // same signal shifted by 5 samples
        var (off, str) = SlipCorrelator.FindOffset(reference, candidate, 64);
        Assert.AreEqual(5, off);
        Assert.IsTrue(str >= SlipCorrelator.MinStrength);
    }

    [TestMethod]
    public void WeakForUnrelatedGarbage()
    {
        var reference = Ramp(2000, 0);
        var rnd = new System.Random(1);
        var garbage = new short[2000]; for (int i = 0; i < garbage.Length; i++) garbage[i] = (short)rnd.Next(-2000, 2000);
        var (_, str) = SlipCorrelator.FindOffset(reference, garbage, 64);
        Assert.IsTrue(str < SlipCorrelator.MinStrength, $"garbage should be weak, was {str}");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail.**

Run: `dotnet test CUETools.Ripper.Tests/CUETools.Ripper.Tests.csproj --filter SlipCorrelatorTests -v q`
Expected: FAIL - `SlipCorrelator` does not exist.

- [ ] **Step 3: Implement SlipCorrelator.** Create `SlipCorrelator.cs`:

```csharp
using System;

namespace CUETools.Ripper.SCSI;

/// <summary>Pure cross-correlation to detect read misalignment (jitter) in a persistent-slip
/// window. Given a reference read and a candidate read of the same window, finds the sample shift
/// that best aligns the candidate and how strong that alignment is. No SCSI - unit-tested with no
/// drive. This only PROPOSES an alignment; the unchanged clean-agreement vote still decides what is
/// accepted, so a wrong offset costs a failed recovery, never wrong data.</summary>
public static class SlipCorrelator
{
    public const double MinStrength = 0.9;   // below this, no reliable alignment -> treat as destruction

    /// <summary>Best shift of candidate onto reference within +/-maxShift, and its normalized
    /// correlation [0,1]. offset > 0 means candidate lags reference by that many samples.</summary>
    public static (int offset, double strength) FindOffset(short[] reference, short[] candidate, int maxShift)
    {
        int n = Math.Min(reference.Length, candidate.Length);
        if (n == 0) return (0, 0);
        double bestCorr = double.NegativeInfinity; int bestOff = 0;
        for (int shift = -maxShift; shift <= maxShift; shift++)
        {
            double dot = 0, er = 0, ec = 0; int count = 0;
            for (int i = 0; i < n; i++)
            {
                int j = i + shift;
                if (j < 0 || j >= n) continue;
                double r = reference[i], c = candidate[j];
                dot += r * c; er += r * r; ec += c * c; count++;
            }
            if (count < n / 2) continue;                 // too little overlap to trust
            double denom = Math.Sqrt(er * ec);
            double corr = denom > 0 ? dot / denom : 0;    // normalized [-1,1]
            if (corr > bestCorr) { bestCorr = corr; bestOff = shift; }
        }
        return (bestOff, Math.Max(0, bestCorr));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass.**

Run: `dotnet test CUETools.Ripper.Tests/CUETools.Ripper.Tests.csproj --filter SlipCorrelatorTests -v q`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit.**

```bash
git add CUETools.Ripper.SCSI/SlipCorrelator.cs CUETools.Ripper.Tests/SlipCorrelatorTests.cs
git commit -m "feat(deep-recovery): SlipCorrelator (raw-read offset finder) + tests"
```

---

### Task 6: Wire the slip probe into the read path

**Files:**
- Modify: `CUETools.Ripper.SCSI/SCSIDrive.cs` (persistent-slip detection in `PrefetchSector`; apply the shift in `FetchSectors` before `ReorganiseSectors`)

**Interfaces:**
- Consumes: `SlipCorrelator.FindOffset` (Task 5), `DeepRecovery`, `_currentErrorsCount`, the window size (`_currentEnd - _currentStart`).

- [ ] **Step 1: Detect a persistent slip and capture a reference read.** In `PrefetchSector`, when `DeepRecovery` and a window has been pegged (`_currentErrorsCount >= 0.85 * (_currentEnd - _currentStart)`) for several consecutive passes, set a field `_slipActive = true` and remember the first full-window read as `_slipReference` (a `short[]` copy of `currentData.Bytes` reinterpreted as samples for the window). Reset `_slipActive = false` at each new window (top of `PrefetchSector`, by the `_thisPassErrors = 0`-style resets).

- [ ] **Step 2: Apply the shift in FetchSectors.** In `FetchSectors`, after a raw read lands and before `ReorganiseSectors(sector, Sectors2Read)` folds it in: if `_slipActive`, run `SlipCorrelator.FindOffset(_slipReference-window-slice, thisReadAsSamples, maxShift: 32)`; if `strength >= SlipCorrelator.MinStrength` and `offset != 0`, shift this read's samples by `offset` (in the raw buffer) so it aligns before folding. If `strength < MinStrength`, do nothing (destruction -> the window will plateau and give up via Task 3). Guard every array access so a shift near the window edge cannot read out of bounds.

- [ ] **Step 3: Assert the safety invariant in a comment at the fold point.**

```csharp
			// SAFETY: re-alignment only re-positions this read so it CAN agree with others. The
			// unchanged (1 + correctionQuality) clean-agreement vote in CorrectSectors is still the
			// sole gate on accepted bytes - a wrong offset yields no agreement (failed recovery),
			// never wrong data. Only runs when DeepRecovery && _slipActive.
```

- [ ] **Step 4: Build.**

Run: `dotnet build CUETools.Ripper.SCSI/CUETools.Ripper.SCSI.csproj -c Debug -v q -nologo`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Off-path guard.** Inspect: with `DeepRecovery == false`, `_slipActive` is never set, so `FetchSectors` never calls the correlator and never shifts. Byte-identical to today.

- [ ] **Step 6: Commit.**

```bash
git add CUETools.Ripper.SCSI/SCSIDrive.cs
git commit -m "feat(deep-recovery): persistent-slip re-alignment probe in the read path"
```

---

### Task 7: Bit-exact gate + recovery-state UI polish

**Files:**
- Modify: `CUETools.Wpf/Views/RipView.xaml` and/or `CUETools.Wpf/Controls/CodecScope.cs` (surface "re-aligning" when the slip probe is active - optional)

- [ ] **Step 1: Clean-disc bit-exact check (manual, owner-run).** With the Genesis (clean) disc: verify with Deep recovery OFF (record AR result), then verify with Deep recovery ON. Both must report the same AccurateRip-accurate result. This proves the probe is a no-op on clean audio.

- [ ] **Step 2: Pinholed recovery check (manual).** With Deep recovery ON on the pinholed disc: the near-miss/slow-converging windows should convert to `converged=1`; if the persistent slip is jitter, AR should move off 0/82 (AccurateRip independently confirms the recovery is correct); if it is destruction, it plateaus and gives up with no corruption.

- [ ] **Step 3 (optional): Surface "re-aligning".** If `_slipActive` is exposed via a reader-observable (e.g., an event or a field read in the ReadProgress handler), pass it to the scope veil so it reads "re-aligning read" instead of the generic "recovering read errors" while the slip probe runs. Keep it numbers/state only.

- [ ] **Step 4: Commit (if Step 3 done).**

```bash
git add CUETools.Wpf/Controls/CodecScope.cs CUETools.Wpf/Views/RipView.xaml
git commit -m "feat(deep-recovery): surface re-aligning state in the scope"
```

---

## Notes for the implementer

- The riskiest change is Task 6 (the only data-path edit). Its safety rests entirely on the vote in `CorrectSectors` being untouched - do not modify `CorrectSectors`. Re-alignment happens strictly before the fold.
- Tasks 1-4 (the safe core) can ship and be proven independently of Tasks 5-6 (the slip probe). If the slip probe proves low-value on this drive, Tasks 1-4 stand on their own.
- SCSI integration (Tasks 3, 4, 6) cannot be unit-tested without a drive; they are gated by the disc checks. The pure logic (Tasks 2, 5) carries the automated tests.
