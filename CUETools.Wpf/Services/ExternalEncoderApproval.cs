using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CUETools.Wpf.Services;

/// <summary>
/// Receipt for one executable copied into the app-managed encoders directory. The hash and size
/// are the runtime authority. Version/product fields are provenance hints bound by that hash, not
/// a publisher signature or a claim that the file came from the download URL shown in the UI.
/// </summary>
internal sealed class ExternalEncoderApproval
{
    public string FileName { get; init; } = "";
    public string EncoderName { get; init; } = "";
    public string Extension { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Length { get; init; }
    public string FileVersion { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string OriginalFileName { get; init; } = "";
    public string SourceFileName { get; init; } = "";
    public string OriginKind { get; init; } = "";
    public string ImportedUtc { get; init; } = "";
}

internal static class ExternalEncoderApprovalCodec
{
    private const string RecordVersion = "v1";
    private const int MaximumSerializedLength = 64 * 1024;
    private const int FieldCount = 12;

    public static bool TryGet(
        string serialized,
        string fileName,
        out ExternalEncoderApproval? approval)
    {
        approval = null;
        if (string.IsNullOrEmpty(serialized) ||
            serialized.Length > MaximumSerializedLength ||
            string.IsNullOrEmpty(fileName))
            return false;

        foreach (string encodedRecord in serialized.Split(';'))
        {
            if (!TryDecode(encodedRecord, out ExternalEncoderApproval? candidate))
                continue;
            if (string.Equals(candidate!.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                approval = candidate;
                return true;
            }
        }

        return false;
    }

    public static string Upsert(string serialized, ExternalEncoderApproval approval)
    {
        if (approval == null)
            throw new ArgumentNullException(nameof(approval));

        var records = new List<string>();
        if (!string.IsNullOrEmpty(serialized) && serialized.Length <= MaximumSerializedLength)
        {
            foreach (string encodedRecord in serialized.Split(';'))
            {
                if (!TryDecode(encodedRecord, out ExternalEncoderApproval? existing))
                    continue;
                if (!string.Equals(
                    existing!.FileName, approval.FileName, StringComparison.OrdinalIgnoreCase))
                    records.Add(encodedRecord);
            }
        }

        records.Add(Encode(approval));
        records.Sort(StringComparer.Ordinal);
        string result = string.Join(";", records);
        if (result.Length > MaximumSerializedLength)
            throw new InvalidOperationException("External encoder approval data exceeds its limit.");
        return result;
    }

    private static string Encode(ExternalEncoderApproval approval)
    {
        string payload = string.Join("\n", new[]
        {
            RecordVersion,
            Normalize(approval.FileName),
            Normalize(approval.EncoderName),
            Normalize(approval.Extension),
            Normalize(approval.Sha256),
            approval.Length.ToString(CultureInfo.InvariantCulture),
            Normalize(approval.FileVersion),
            Normalize(approval.ProductName),
            Normalize(approval.OriginalFileName),
            Normalize(approval.SourceFileName),
            Normalize(approval.OriginKind),
            Normalize(approval.ImportedUtc),
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryDecode(
        string encodedRecord,
        out ExternalEncoderApproval? approval)
    {
        approval = null;
        if (string.IsNullOrEmpty(encodedRecord) || encodedRecord.Length > 16 * 1024)
            return false;

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(Convert.FromBase64String(encodedRecord));
        }
        catch (FormatException)
        {
            return false;
        }

        string[] fields = payload.Split('\n');
        if (fields.Length != FieldCount ||
            !string.Equals(fields[0], RecordVersion, StringComparison.Ordinal) ||
            !long.TryParse(
                fields[5],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long length) ||
            length < 0)
            return false;

        approval = new ExternalEncoderApproval
        {
            FileName = fields[1],
            EncoderName = fields[2],
            Extension = fields[3],
            Sha256 = fields[4],
            Length = length,
            FileVersion = fields[6],
            ProductName = fields[7],
            OriginalFileName = fields[8],
            SourceFileName = fields[9],
            OriginKind = fields[10],
            ImportedUtc = fields[11],
        };
        return true;
    }

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unavailable";

        var result = new StringBuilder(Math.Min(value.Length, 256));
        foreach (char c in value.Trim())
        {
            if (result.Length == 256)
                break;
            result.Append(char.IsControl(c) ? ' ' : c);
        }
        return result.ToString();
    }
}
