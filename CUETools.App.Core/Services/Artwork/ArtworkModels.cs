using System;
using System.Collections.Generic;
using CUETools.CTDB;

namespace CUETools.Wpf.Services.Artwork;

public enum ArtworkMatchTier
{
    WeakText = 0,
    StrongText = 1,
    ReleaseGroup = 2,
    ExactBarcode = 3,
    MetadataRelease = 4,
    ExactRelease = 5
}

public enum ArtworkProviderConfidence
{
    TextSearch = 0,
    TheAudioDbReleaseGroup = 1,
    AppleLicensedExact = 2,
    CoverArtArchiveUnapproved = 3,
    CtdbPrimary = 4,
    CoverArtArchiveApproved = 5
}

/// <summary>
/// Structured identity for one artwork lookup. Provider identifiers remain associated with their
/// provider key so an opaque CTDB result cannot be guessed to be a MusicBrainz UUID.
/// </summary>
public sealed record ArtworkQuery(
    string Artist,
    string Album,
    string Year,
    string Barcode,
    int TrackCount,
    string DiscId,
    string Toc,
    string ProviderKey,
    string ProviderId,
    string InfoUrl,
    long Generation,
    IReadOnlyList<CTDBResponseMetaImage>? MetadataArtwork = null,
    CUETools.Processor.CUEConfigAdvanced.CTDBCoversSearch SearchMode =
        CUETools.Processor.CUEConfigAdvanced.CTDBCoversSearch.Extensive);

/// <summary>Provider-neutral artwork metadata. Image bytes are loaded only by the bounded loader.</summary>
public sealed record ArtworkCandidate
{
    public required string CandidateId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderItemId { get; init; }
    public required Uri ThumbnailUri { get; init; }
    public required Uri OriginalUri { get; init; }
    public required ArtworkMatchTier MatchTier { get; init; }
    public required ArtworkProviderConfidence ProviderConfidence { get; init; }
    public required string MatchReason { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public long? ByteLength { get; init; }
    public string MimeType { get; init; } = "";
    public bool IsFront { get; init; }
    public bool IsApproved { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsWatermarked { get; init; }
    public bool AutomaticEligible { get; init; } = true;
    public int ProviderOrder { get; init; }
    public Uri? InfoUri { get; init; }
    public string ArtworkType { get; init; } = "Front";

    public string DimensionsText =>
        Width is > 0 && Height is > 0 ? $"{Width} x {Height}" : "unknown";

    public string SizeText =>
        ByteLength is > 0 ? FormatBytes(ByteLength.Value) : "unknown";

    public string MatchText => MatchTier switch
    {
        ArtworkMatchTier.ExactRelease => "Exact release",
        ArtworkMatchTier.MetadataRelease => "Selected release",
        ArtworkMatchTier.ExactBarcode => "Exact barcode",
        ArtworkMatchTier.ReleaseGroup => "Canonical release group",
        ArtworkMatchTier.StrongText => "Strong text match",
        _ => "Possible text match"
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L)
            return $"{bytes / (1024d * 1024d):0.##} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024d:0.#} KB";
        return $"{bytes} B";
    }
}

/// <summary>
/// Release identity is compared before quality. This prevents a larger image for another edition
/// from outranking exact art.
/// </summary>
public sealed class ArtworkCandidateComparer : IComparer<ArtworkCandidate>
{
    public static ArtworkCandidateComparer Recommended { get; } = new();

    public int Compare(ArtworkCandidate? x, ArtworkCandidate? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        int result = Desc((int)x.MatchTier, (int)y.MatchTier);
        if (result != 0) return result;
        result = Desc(x.IsFront, y.IsFront);
        if (result != 0) return result;
        result = Desc(x.IsApproved || x.IsPrimary, y.IsApproved || y.IsPrimary);
        if (result != 0) return result;
        result = Asc(x.IsWatermarked, y.IsWatermarked);
        if (result != 0) return result;
        result = Desc((int)x.ProviderConfidence, (int)y.ProviderConfidence);
        if (result != 0) return result;
        result = Desc(PixelArea(x), PixelArea(y));
        if (result != 0) return result;
        result = Asc(SquarePenalty(x), SquarePenalty(y));
        if (result != 0) return result;
        result = Desc(x.ByteLength ?? -1, y.ByteLength ?? -1);
        if (result != 0) return result;
        result = string.Compare(x.Provider, y.Provider, StringComparison.OrdinalIgnoreCase);
        if (result != 0) return result;
        result = x.ProviderOrder.CompareTo(y.ProviderOrder);
        if (result != 0) return result;
        return string.Compare(x.CandidateId, y.CandidateId, StringComparison.Ordinal);
    }

    private static long PixelArea(ArtworkCandidate candidate) =>
        candidate.Width is > 0 && candidate.Height is > 0
            ? (long)candidate.Width.Value * candidate.Height.Value
            : -1;

    private static double SquarePenalty(ArtworkCandidate candidate)
    {
        if (candidate.Width is not > 0 || candidate.Height is not > 0)
            return double.MaxValue;
        return Math.Abs(Math.Log((double)candidate.Width.Value / candidate.Height.Value));
    }

    private static int Desc<T>(T x, T y) where T : IComparable<T> => y.CompareTo(x);
    private static int Asc<T>(T x, T y) where T : IComparable<T> => x.CompareTo(y);
}
