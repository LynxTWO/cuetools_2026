using System;
using System.Collections.Generic;
using System.Text;
using CUETools.AccurateRip;

namespace CUETools.Wpf.Accuracy;

/// <summary>One pairwise offset measurement between two completed reads of the same disc.</summary>
public readonly record struct ReadOffsetPair(
    int FromRead,
    int ToRead,
    bool Comparable,
    bool OffsetFound,
    int OffsetSamples);

/// <summary>
/// R122, measurement only. Asks one question of two completed reads: is the second a constant
/// sample shift of the first? Nothing here touches the vote, the audio, or any assurance claim -
/// it exists to settle with numbers whether the "defective discs come back shifted" hypothesis
/// is true for a given disc, before anyone builds realignment on top of it.
/// <para>The fingerprint is AccurateRipVerify's own <see cref="AccurateRipVerify.OffsetSafeCRC"/>,
/// a 128-entry record whose search covers +/-4096 samples. That bound matters: a whole-read slip
/// larger than the window is reported as "no constant offset", which is not the same as "the
/// reads are aligned". Say the first, never the second.</para>
/// <para>Read-vs-read is also NOT read-vs-database. AccurateRip already sweeps its own offset
/// range against the database on every rip; a disc can be present-but-unmatched there while two
/// local reads sit perfectly aligned with each other, and vice versa.</para>
/// </summary>
public static class ReadOffsetProbe
{
    /// <summary>Measure every ordered pair of reads. Fingerprints that are missing or unparsable
    /// yield a non-comparable pair rather than a guess.</summary>
    public static IReadOnlyList<ReadOffsetPair> Compare(IReadOnlyList<string?> fingerprints)
    {
        var results = new List<ReadOffsetPair>();
        if (fingerprints == null) return results;
        var parsed = new OffsetSafeCRCRecord?[fingerprints.Count];
        for (int i = 0; i < fingerprints.Count; i++)
            parsed[i] = Parse(fingerprints[i]);

        for (int a = 0; a < parsed.Length; a++)
            for (int b = a + 1; b < parsed.Length; b++)
            {
                if (parsed[a] == null || parsed[b] == null)
                {
                    results.Add(new ReadOffsetPair(a, b, false, false, 0));
                    continue;
                }
                bool found;
                int offset;
                try { found = parsed[a]!.FindOffset(parsed[b]!, out offset); }
                catch { results.Add(new ReadOffsetPair(a, b, false, false, 0)); continue; }
                results.Add(new ReadOffsetPair(a, b, true, found, found ? offset : 0));
            }
        return results;
    }

    internal static OffsetSafeCRCRecord? Parse(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            var record = new OffsetSafeCRCRecord();
            record.Base64 = base64;
            return record.Value != null && record.Value.Length == 128 ? record : null;
        }
        catch { return null; }
    }

    /// <summary>A log-ready summary. Deliberately blunt about what a null result means.</summary>
    public static string Describe(IReadOnlyList<ReadOffsetPair> pairs)
    {
        if (pairs == null || pairs.Count == 0) return "no comparable reads";
        var text = new StringBuilder();
        foreach (var pair in pairs)
        {
            if (text.Length > 0) text.Append("; ");
            text.Append("read").Append(pair.FromRead + 1).Append("->read").Append(pair.ToRead + 1)
                .Append('=');
            if (!pair.Comparable) text.Append("not-comparable");
            else if (!pair.OffsetFound) text.Append("no-constant-offset-within-4096");
            else if (pair.OffsetSamples == 0) text.Append("aligned");
            else text.Append(pair.OffsetSamples).Append("-samples");
        }
        return text.ToString();
    }

    /// <summary>True when at least one pair is a genuine nonzero constant shift - the only
    /// result that would make realignment worth designing for this disc.</summary>
    public static bool AnyConstantShift(IReadOnlyList<ReadOffsetPair> pairs)
    {
        if (pairs == null) return false;
        foreach (var pair in pairs)
            if (pair.Comparable && pair.OffsetFound && pair.OffsetSamples != 0)
                return true;
        return false;
    }
}
