using System;
using System.Text;

namespace CUETools.Wpf.Models;

/// <summary>
/// The result of one Verify or Rip job, in the shape the Report page renders as a certificate
/// and the shape the tamper-evident log is sealed over. Every field is a receipt: it comes
/// from the live AccurateRip / CTDB check or the drive, never a placeholder.
/// </summary>
public sealed class RipReport
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Mode { get; init; } = "Verify";   // "Verify" | "Rip"
    public string Album { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Year { get; init; } = "";
    public string DriveName { get; init; } = "";
    public int Offset { get; init; }
    public int CorrectionQuality { get; init; }      // 0 Burst / 1 Secure / 2 Paranoid
    public int ArConfidence { get; init; }
    public int ArTotal { get; init; }
    public int CtdbConfidence { get; init; }
    public int CtdbTotal { get; init; }
    public bool Accurate { get; init; }
    public string Status { get; init; } = "";
    public string OutputDir { get; init; } = "";
    public int FileCount { get; init; }
    public int TrackCount { get; init; }
    public string TocId { get; init; } = "";

    private static readonly string[] CqNames = { "Burst", "Secure", "Paranoid" };
    public string CorrectionQualityName => CqNames[Math.Clamp(CorrectionQuality, 0, 2)];

    /// <summary>True when the disc was confirmed by at least one independent database.</summary>
    public bool Confirmed => Accurate || CtdbConfidence > 0;

    public string OffsetText => Offset >= 0 ? "+" + Offset : Offset.ToString();

    /// <summary>The canonical rip-log body the SHA-256 self-check covers (no footer line).
    /// Fixed field order and LF endings so the same job always hashes the same way.</summary>
    public string BuildLogBody()
    {
        var sb = new StringBuilder();
        sb.Append("CUETools 2026 rip log\n");
        sb.Append("Date          : ").Append(Timestamp.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
        sb.Append("Mode          : ").Append(Mode).Append('\n');
        sb.Append("Artist        : ").Append(Artist).Append('\n');
        sb.Append("Album         : ").Append(Album).Append(Year.Length > 0 ? " (" + Year + ")" : "").Append('\n');
        sb.Append("Drive         : ").Append(DriveName).Append('\n');
        sb.Append("Read offset   : ").Append(OffsetText).Append(" samples\n");
        sb.Append("Accuracy mode : ").Append(CorrectionQualityName).Append('\n');
        sb.Append("Tracks        : ").Append(TrackCount).Append('\n');
        if (TocId.Length > 0) sb.Append("TOC id        : ").Append(TocId).Append('\n');
        sb.Append("AccurateRip   : ").Append(Accurate
            ? "accurate (confidence " + ArConfidence + ")"
            : "not confirmed (" + ArConfidence + " / " + ArTotal + ")").Append('\n');
        sb.Append("CTDB          : ").Append(CtdbConfidence > 0
            ? "verified (confidence " + CtdbConfidence + ")"
            : "not found").Append('\n');
        if (Mode == "Rip")
        {
            sb.Append("Output        : ").Append(FileCount).Append(" FLAC files\n");
            if (OutputDir.Length > 0) sb.Append("Folder        : ").Append(OutputDir).Append('\n');
        }
        sb.Append("Result        : ").Append(Status.Replace("\r", " ").Replace("\n", " ")).Append('\n');
        return sb.ToString();
    }
}
