using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CUETools.Wpf.Services;

/// <summary>
/// Stable machine-readable proof for a published CTDB repair. Paths are relative to the source or
/// repaired album root, so the receipt remains portable and does not disclose the user's library.
/// </summary>
public sealed class RepairReceipt
{
    public string Schema { get; set; } = "CUETOOLS_REPAIR_RECEIPT_V1";
    public DateTime Utc { get; set; }
    public string Version { get; set; } = "";
    public bool RepairApplied { get; set; }
    public bool IndependentlyVerified { get; set; }
    public int TrackCount { get; set; }
    public int RepairSamples { get; set; }
    public int RepairSectors { get; set; }
    public int RepairTotalSectors { get; set; }
    public int RepairNpar { get; set; }
    // Repair headroom (R115): worst per-stripe error count vs the npar/2 stripe capacity.
    // Additive fields; receipts written before them deserialize to 0 (unknown).
    public int RepairWorstStripeErrors { get; set; }
    public int RepairStripeCapacity { get; set; }
    public string RepairRanges { get; set; } = "";
    public int AccurateRipConfidence { get; set; }
    public int AccurateRipTotal { get; set; }
    public int CtdbConfidence { get; set; }
    public int CtdbTotal { get; set; }
    public string AccurateRipLog { get; set; } = "";
    public string RepairLog { get; set; } = "";
    public RepairFileReceipt[] SourceFiles { get; set; } = Array.Empty<RepairFileReceipt>();
    public RepairFileReceipt[] OutputFiles { get; set; } = Array.Empty<RepairFileReceipt>();
}

public sealed class RepairFileReceipt
{
    public string RelativePath { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>
/// Keeps the private absolute path beside the portable receipt fields. The source is hashed before
/// the repair reads it and re-hashed immediately before publication.
/// </summary>
internal sealed class RepairFileProof
{
    private RepairFileProof(string fullPath, RepairFileReceipt receipt)
    {
        FullPath = fullPath;
        Receipt = receipt;
    }

    public string FullPath { get; }
    public RepairFileReceipt Receipt { get; }

    public static RepairFileProof Capture(string rootDirectory, string path)
    {
        string root = Path.GetFullPath(rootDirectory);
        string full = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(root, full);
        if (relative == "."
            || Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            relative = Path.GetFileName(full);
        }

        var info = new FileInfo(full);
        if (!info.Exists)
            throw new FileNotFoundException("A repair input is missing.", full);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("A repair input is a link or reparse point.");

        return new RepairFileProof(
            full,
            new RepairFileReceipt
            {
                RelativePath = relative,
                Length = info.Length,
                Sha256 = HashFile(full)
            });
    }

    public void RequireUnchanged(string description = "repair source")
    {
        var info = new FileInfo(FullPath);
        if (!info.Exists
            || info.Length != Receipt.Length
            || !string.Equals(HashFile(FullPath), Receipt.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A " + description + " changed while the repaired copy was being built.");
        }
    }

    internal static string HashFile(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RepairReceipt))]
internal sealed partial class RepairReceiptJsonContext : JsonSerializerContext
{
}

internal static class RepairEvidence
{
    internal const string ReceiptFileName = "repair.verify";

    public static RepairReceipt Create(
        IReadOnlyList<RepairFileProof> sourceProofs,
        IReadOnlyList<RepairFileProof> outputProofs,
        VerifyFilesResult repaired,
        VerifyFilesResult verified,
        string accurateRipLogName,
        string repairLogName)
    {
        return new RepairReceipt
        {
            Utc = DateTime.UtcNow,
            Version = CUETools.Processor.CUESheet.CUEToolsVersion,
            RepairApplied = repaired.RepairApplied,
            IndependentlyVerified =
                verified.ArConfidence > 0 || verified.CtdbConfidence > 0,
            TrackCount = verified.TrackCount > 0
                ? verified.TrackCount
                : repaired.TrackCount,
            RepairSamples = repaired.RepairSamples,
            RepairSectors = repaired.RepairSectors,
            RepairTotalSectors = repaired.RepairTotalSectors,
            RepairNpar = repaired.RepairNpar,
            RepairWorstStripeErrors = repaired.RepairWorstStripeErrors,
            RepairStripeCapacity = repaired.RepairStripeCapacity,
            RepairRanges = repaired.RepairRanges,
            AccurateRipConfidence = verified.ArConfidence,
            AccurateRipTotal = verified.ArTotal,
            CtdbConfidence = verified.CtdbConfidence,
            CtdbTotal = verified.CtdbTotal,
            AccurateRipLog = accurateRipLogName,
            RepairLog = repairLogName,
            SourceFiles = sourceProofs.Select(proof => proof.Receipt).ToArray(),
            OutputFiles = outputProofs.Select(proof => proof.Receipt).ToArray()
        };
    }

    // Source-generated serialization: reflection-based System.Text.Json is disabled when a host
    // publishes with AOT (IsDynamicCodeSupported=false), and the receipt must seal there too.
    public static string ToJson(RepairReceipt receipt) =>
        JsonSerializer.Serialize(
            receipt,
            RepairReceiptJsonContext.Default.RepairReceipt);

    public static void WriteDurableText(string path, string contents)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read);
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false),
            1024,
            leaveOpen: true);
        writer.Write(contents);
        writer.Flush();
        stream.Flush(true);
    }

    public static string HumanLog(RepairReceipt receipt)
    {
        var text = new StringBuilder();
        text.AppendLine("CUETools CTDB repair report");
        text.AppendLine("Result: independently verified and published");
        text.AppendLine("Repair applied: yes");
        text.AppendLine("Corrected samples: " + receipt.RepairSamples);
        text.AppendLine("Corrected sectors: " + receipt.RepairSectors);
        if (receipt.RepairStripeCapacity > 0)
            text.AppendLine("Parity headroom: worst stripe used " +
                receipt.RepairWorstStripeErrors + " of " +
                receipt.RepairStripeCapacity + " correctable errors");
        if (!string.IsNullOrWhiteSpace(receipt.RepairRanges))
            text.AppendLine("Corrected ranges: " + receipt.RepairRanges);
        text.AppendLine(
            "AccurateRip: " +
            (receipt.AccurateRipConfidence > 0
                ? "accurate, confidence " + receipt.AccurateRipConfidence +
                  " / " + receipt.AccurateRipTotal
                : receipt.AccurateRipTotal > 0
                    ? "found, no exact match"
                    : "not found"));
        text.AppendLine(
            "CTDB: " +
            (receipt.CtdbConfidence > 0
                ? "exact match, confidence " + receipt.CtdbConfidence +
                  " / " + receipt.CtdbTotal
                : receipt.CtdbTotal > 0
                    ? "found, no exact match"
                    : "not found"));
        text.AppendLine(
            "Source preservation: SHA-256 unchanged for " +
            receipt.SourceFiles.Length + " source file(s)");
        text.AppendLine(
            "Published payload: SHA-256 recorded for " +
            receipt.OutputFiles.Length + " repaired file(s)");
        text.AppendLine("Machine receipt: " + ReceiptFileName);
        text.AppendLine("Database detail: " + receipt.AccurateRipLog);
        return text.ToString();
    }
}
