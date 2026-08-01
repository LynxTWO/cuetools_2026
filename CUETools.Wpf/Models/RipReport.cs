using System;
using System.Text;

namespace CUETools.Wpf.Models;

/// <summary>
/// The result of one Verify or Rip job, in the shape the Report page renders as a receipt.
/// Every field comes
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
    /// <summary>Total optical reads used by a passed Test &amp; Copy run. Zero for ordinary
    /// rip/verify jobs and for an unverified accepted copy.</summary>
    public int OpticalReadsUsed { get; init; }
    /// <summary>Minimum number of reads known to agree for every committed track. Test &amp; Copy
    /// currently proves two-read agreement per track; when a third read is needed, the agreeing
    /// partner can differ by track, so this must not be reported as three-way agreement.</summary>
    public int MinimumAgreeingReads { get; init; }
    public string Status { get; init; } = "";
    public string OutputDir { get; init; } = "";
    public int FileCount { get; init; }
    /// <summary>Codec extension the files were written as ("flac", "m4a"...). Empty on reports
    /// archived before this field existed, which render the neutral word "audio".</summary>
    public string Format { get; init; } = "";
    /// <summary>Final encoded-output assurance is intentionally separate from database and
    /// optical-read evidence. Old reports and verify-only jobs leave Known false.</summary>
    public bool OutputVerificationKnown { get; init; }
    public bool LosslessOutput { get; init; }
    public bool OutputVerificationPerformed { get; init; }
    public string OutputVerificationDetail { get; init; } = "";
    public int TrackCount { get; init; }
    public string TocId { get; init; } = "";
    /// <summary>Sector windows the drive gave up on across the reads behind this result. Old
    /// archived reports leave this 0, which is why it must only ever WIDEN a warning, never
    /// upgrade one.</summary>
    public int FailedWindows { get; init; }
    /// <summary>True when the audio carries CTDB-detected damage (repairable or not).</summary>
    public bool DamageRepairRequired { get; init; }

    private static readonly string[] CqNames = { "Burst", "Secure", "Paranoid" };
    public string CorrectionQualityName => CqNames[Math.Clamp(CorrectionQuality, 0, 2)];

    /// <summary>Matching reads with unrecoverable windows or CTDB-detected damage are CONSISTENT,
    /// not cleanly verified - the same policy the Test &amp; Copy log applies.</summary>
    public bool Damaged => FailedWindows > 0 || DamageRepairRequired;
    /// <summary>True when the disc was confirmed by at least one independent database.</summary>
    public bool DatabaseConfirmed => Accurate || CtdbConfidence > 0;
    /// <summary>Compatibility name for database confirmation. Independent read agreement is a
    /// different assurance source and must not be mislabeled as an AccurateRip/CTDB match.</summary>
    public bool Confirmed => DatabaseConfirmed;
    public bool IndependentReadsVerified =>
        OpticalReadsUsed >= 2 && MinimumAgreeingReads >= 2 && !Damaged;
    /// <summary>True when a database or multiple independent reads verified the audio AND no
    /// damage was recorded. A damaged result renders as consistent everywhere, including here:
    /// the certificate headline, badge, and history must never outrank the log.</summary>
    public bool Verified => (DatabaseConfirmed || (OpticalReadsUsed >= 2 && MinimumAgreeingReads >= 2)) && !Damaged;

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
        if (OpticalReadsUsed >= 2 && MinimumAgreeingReads >= 2)
            sb.Append("Independent   : ").Append(Damaged ? "consistent" : "verified")
              .Append(" after ").Append(OpticalReadsUsed)
              .Append(" optical reads; every track agreed across at least ")
              .Append(MinimumAgreeingReads).Append(" reads\n");
        if (Damaged)
            sb.Append("Damage        : ")
              .Append(FailedWindows > 0
                  ? FailedWindows + " unrecoverable sector window(s) during reads"
                  : "CTDB-detected damage")
              .Append(FailedWindows > 0 && DamageRepairRequired ? "; CTDB-detected damage" : "")
              .Append(" - agreement over damaged media is consistency, not proof\n");
        // Any mode that WROTE something, not just Mode=="Rip": a Test & Copy writes files too, and
        // gating on the literal "Rip" left the highest-assurance mode's certificate naming neither
        // its file count nor its output folder. The codec is reported rather than assumed - this
        // line said "FLAC files" for every m4a and mp3 rip.
        if (OutputDir.Length > 0)
        {
            sb.Append("Output        : ").Append(FileCount).Append(' ')
              .Append(Format.Length > 0 ? Format.ToUpperInvariant() : "audio").Append(" files\n");
            if (OutputVerificationKnown)
                sb.Append("Output verify : ")
                  .Append(OutputVerificationDetail.Length > 0
                      ? OutputVerificationDetail
                      : OutputVerificationPerformed
                          ? "performed after metadata finalization"
                          : LosslessOutput
                              ? "not performed"
                              : "not applicable (lossy output)")
                  .Append('\n');
            sb.Append("Folder        : ").Append(OutputDir).Append('\n');
        }
        sb.Append("Result        : ").Append(Status.Replace("\r", " ").Replace("\n", " ")).Append('\n');
        return sb.ToString();
    }
}
