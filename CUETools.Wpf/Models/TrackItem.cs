using System;
using System.Collections.Generic;
using CUETools.Wpf.Mvvm;

namespace CUETools.Wpf.Models;

/// <summary>One audio track shown in the Rip track list. Editable title now; the
/// per-track rip Quality / AccurateRip / C2 columns get added when ripping (Phase 3 cont).</summary>
public sealed class TrackItem : ViewModelBase
{
    public int Number { get; init; }

    private string _title = "";
    public string Title { get => _title; set => Set(ref _title, value); }

    public TimeSpan Length { get; init; }
    public string LengthText => $"{(int)Length.TotalMinutes}:{Length.Seconds:00}";

    private bool _include = true;
    public bool Include { get => _include; set => Set(ref _include, value); }

    // Per-track verification, filled in after a rip/verify. "-" until then (no data yet).
    private string _arResult = "-";
    public string ArResult { get => _arResult; set => Set(ref _arResult, value); }

    private string _ctdbResult = "-";
    public string CtdbResult { get => _ctdbResult; set => Set(ref _ctdbResult, value); }

    // Full-range CRC32 evidence. Unlike AccurateRip CRCs these cover the samples at both disc edges.
    // They persist in verify history and are restored when the same disc is inserted again.
    private string _testCrc = "-";
    public string TestCrc { get => _testCrc; private set => Set(ref _testCrc, value); }

    private string _copyCrc = "-";
    public string CopyCrc { get => _copyCrc; private set => Set(ref _copyCrc, value); }

    private bool _crcsMatch;
    public bool CrcsMatch { get => _crcsMatch; private set => Set(ref _crcsMatch, value); }

    private bool _crcsDiffer;
    public bool CrcsDiffer { get => _crcsDiffer; private set => Set(ref _crcsDiffer, value); }

    private bool _crossDriveCorroborated;
    public bool CrossDriveCorroborated
    {
        get => _crossDriveCorroborated;
        private set => Set(ref _crossDriveCorroborated, value);
    }

    private string _crcEvidenceTip = "No Test or Copy CRC evidence yet.";
    public string CrcEvidenceTip
    {
        get => _crcEvidenceTip;
        private set => Set(ref _crcEvidenceTip, value);
    }

    /// <summary>Show one CRC the moment its track finished reading (R120), before the whole
    /// album completes. Live values fill only the column the read owns and never clear the
    /// other one, so a Copy read cannot erase the Test evidence beside it. The authoritative
    /// snapshot still arrives through <see cref="ApplyCrcEvidence"/> at read end and overwrites
    /// this; live cells are presentation only.</summary>
    public void ApplyLiveCrc(uint crc32, bool isCopyRead)
    {
        if (crc32 == 0) return;
        string formatted = crc32.ToString("X8");
        if (isCopyRead) CopyCrc = formatted; else TestCrc = formatted;

        uint test = ParseCrc(TestCrc), copy = ParseCrc(CopyCrc);
        CrcsMatch = test != 0 && copy != 0 && test == copy;
        CrcsDiffer = test != 0 && copy != 0 && test != copy;
        CrcEvidenceTip =
            $"Live from the {(isCopyRead ? "Copy" : "Test")} read as this track finished. " +
            "The complete evidence is recorded when the read ends.";
    }

    private static uint ParseCrc(string text)
        => uint.TryParse(
            (text ?? "").Split(' ')[0],
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out uint value) ? value : 0u;

    public void ApplyCrcEvidence(CUETools.Wpf.Accuracy.TrackCrc? evidence)
    {
        uint test = evidence?.TestCrc32 ?? 0;
        uint copy = evidence?.CopyCrc32 ?? 0;
        int testCount = Math.Max(0, evidence?.TestMatchCount ?? 0);
        int copyCount = Math.Max(0, evidence?.CopyMatchCount ?? 0);
        int testDrives = Math.Max(0, evidence?.TestDriveCount ?? 0);
        int copyDrives = Math.Max(0, evidence?.CopyDriveCount ?? 0);

        TestCrc = FormatCrc(test, testCount);
        CopyCrc = FormatCrc(copy, copyCount);
        CrcsMatch = test != 0 && copy != 0 && test == copy;
        CrcsDiffer = test != 0 && copy != 0 && test != copy;
        int distinctDrives = CountDistinctDrives(
            evidence?.TestDriveFingerprints,
            evidence?.CopyDriveFingerprints);
        CrossDriveCorroborated = CrcsMatch && distinctDrives > 1;
        CrcEvidenceTip =
            $"Test: {(test == 0 ? "not recorded" : test.ToString("X8"))}, " +
            $"{testCount} matching job(s), {testDrives} drive(s). " +
            $"Copy: {(copy == 0 ? "not recorded" : copy.ToString("X8"))}, " +
            $"{copyCount} matching job(s), {copyDrives} drive(s)." +
            (CrcsMatch && distinctDrives > 1
                ? $" Corroborated across {distinctDrives} distinct drives."
                : "");
    }

    private static int CountDistinctDrives(params string?[] groups)
    {
        var drives = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? group in groups)
            foreach (string value in (group ?? "").Split(','))
                if (value.Length > 0)
                    drives.Add(value);
        return drives.Count;
    }

    private static string FormatCrc(uint crc, int count) =>
        crc == 0
            ? "-"
            : crc.ToString("X8") + (count > 1 ? " x" + count : "");

    // Live per-track progress during a rip (0..1), derived from the read head position.
    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    private bool _active;
    public bool Active { get => _active; set => Set(ref _active, value); }
}

/// <summary>A row in the "recently ripped" list on the eject/no-disc screen.</summary>
public sealed class RecentRip
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string When { get; init; } = "";
    public string Result { get; init; } = "";
}
