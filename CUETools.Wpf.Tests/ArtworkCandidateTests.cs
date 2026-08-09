using System;
using System.Collections.Generic;
using CUETools.Wpf.Services.Artwork;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class ArtworkCandidateTests
{
    [TestMethod]
    public void ExactReleaseOutranksMuchLargerReleaseGroupImage()
    {
        ArtworkCandidate exact = Candidate(
            "exact", ArtworkMatchTier.ExactRelease, 500, 500, 100_000);
        ArtworkCandidate canonical = Candidate(
            "canonical", ArtworkMatchTier.ReleaseGroup, 5000, 5000, 20_000_000);

        var candidates = new List<ArtworkCandidate> { canonical, exact };
        candidates.Sort(ArtworkCandidateComparer.Recommended);

        Assert.AreSame(exact, candidates[0]);
    }

    [TestMethod]
    public void QualityBreaksTiesOnlyInsideTheSameIdentityTier()
    {
        ArtworkCandidate small = Candidate(
            "small", ArtworkMatchTier.ExactRelease, 500, 500, 90_000);
        ArtworkCandidate large = Candidate(
            "large", ArtworkMatchTier.ExactRelease, 1200, 1200, 450_000);

        var candidates = new List<ArtworkCandidate> { small, large };
        candidates.Sort(ArtworkCandidateComparer.Recommended);

        Assert.AreSame(large, candidates[0]);
    }

    [TestMethod]
    public void KnownDimensionsAndSizesHaveClearDisplayText()
    {
        ArtworkCandidate candidate = Candidate(
            "display", ArtworkMatchTier.MetadataRelease, 1400, 1400, 1_572_864);

        Assert.AreEqual("1400 x 1400", candidate.DimensionsText);
        Assert.AreEqual("1.5 MB", candidate.SizeText);
        Assert.AreEqual("Selected release", candidate.MatchText);
    }

    [TestMethod]
    public void UnknownAndBoundaryImageMetadataHasStableDisplayText()
    {
        ArtworkCandidate candidate = Candidate(
            "boundaries", ArtworkMatchTier.ExactRelease, 1, 1, 1);

        Assert.AreEqual("unknown", (candidate with { Width = null }).DimensionsText);
        Assert.AreEqual("unknown", (candidate with { Height = 0 }).DimensionsText);
        Assert.AreEqual("unknown", (candidate with { ByteLength = 0 }).SizeText);
        Assert.AreEqual("1023 B", (candidate with { ByteLength = 1023 }).SizeText);
        Assert.AreEqual("1 KB", (candidate with { ByteLength = 1024 }).SizeText);
        Assert.AreEqual("1023 KB", (candidate with { ByteLength = 1023 * 1024 }).SizeText);
        Assert.AreEqual("1 MB", (candidate with { ByteLength = 1024 * 1024 }).SizeText);
    }

    [DataTestMethod]
    [DataRow(ArtworkMatchTier.ExactRelease, "Exact release")]
    [DataRow(ArtworkMatchTier.MetadataRelease, "Selected release")]
    [DataRow(ArtworkMatchTier.ExactBarcode, "Exact barcode")]
    [DataRow(ArtworkMatchTier.ReleaseGroup, "Canonical release group")]
    [DataRow(ArtworkMatchTier.StrongText, "Strong text match")]
    [DataRow(ArtworkMatchTier.WeakText, "Possible text match")]
    public void EveryMatchTierHasUserFacingText(ArtworkMatchTier tier, string expected)
    {
        Assert.AreEqual(expected, Candidate("tier", tier, 100, 100, 100).MatchText);
    }

    [TestMethod]
    public void NullCandidatesSortLastAndSameReferenceComparesEqual()
    {
        ArtworkCandidate candidate = Candidate(
            "same", ArtworkMatchTier.ExactRelease, 100, 100, 100);

        Assert.AreEqual(0, ArtworkCandidateComparer.Recommended.Compare(candidate, candidate));
        Assert.IsTrue(ArtworkCandidateComparer.Recommended.Compare(null, candidate) > 0);
        Assert.IsTrue(ArtworkCandidateComparer.Recommended.Compare(candidate, null) < 0);
    }

    [TestMethod]
    public void RecommendedOrderingUsesEveryQualityTieBreakerInOrder()
    {
        ArtworkCandidate baseline = Candidate(
            "baseline", ArtworkMatchTier.ExactRelease, 100, 100, 100);

        AssertComesFirst(baseline, baseline with { IsFront = false, CandidateId = "back" });
        AssertComesFirst(
            baseline with { IsApproved = false, IsPrimary = true, CandidateId = "primary" },
            baseline with { IsApproved = false, IsPrimary = false, CandidateId = "unapproved" });
        AssertComesFirst(baseline, baseline with { IsWatermarked = true, CandidateId = "marked" });
        AssertComesFirst(
            baseline,
            baseline with
            {
                ProviderConfidence = ArtworkProviderConfidence.TextSearch,
                CandidateId = "weak-provider"
            });
        AssertComesFirst(
            baseline with { Width = 200, Height = 100, CandidateId = "large" },
            baseline with { Width = 100, Height = 100, CandidateId = "small" });

        // Equal pixel area makes shape, rather than area, the deciding quality signal.
        AssertComesFirst(
            baseline with { Width = 100, Height = 100, CandidateId = "square" },
            baseline with { Width = 200, Height = 50, CandidateId = "wide" });
        AssertComesFirst(
            baseline with { ByteLength = 200, CandidateId = "larger-file" },
            baseline with { ByteLength = null, CandidateId = "unknown-file" });
        AssertComesFirst(
            baseline with { Provider = "alpha", CandidateId = "provider-a" },
            baseline with { Provider = "beta", CandidateId = "provider-b" });
        AssertComesFirst(
            baseline with { ProviderOrder = 1, CandidateId = "order-1" },
            baseline with { ProviderOrder = 2, CandidateId = "order-2" });
        AssertComesFirst(
            baseline with { CandidateId = "a" },
            baseline with { CandidateId = "b" });
    }

    [TestMethod]
    public void MissingDimensionsNeverOutrankKnownDimensions()
    {
        ArtworkCandidate known = Candidate(
            "known", ArtworkMatchTier.ExactRelease, 1, 1, 100);
        ArtworkCandidate missingWidth = known with { CandidateId = "missing-width", Width = null };
        ArtworkCandidate missingHeight = known with { CandidateId = "missing-height", Height = null };

        AssertComesFirst(known, missingWidth);
        AssertComesFirst(known, missingHeight);
    }

    private static void AssertComesFirst(ArtworkCandidate first, ArtworkCandidate second)
    {
        Assert.IsTrue(ArtworkCandidateComparer.Recommended.Compare(first, second) < 0);
        Assert.IsTrue(ArtworkCandidateComparer.Recommended.Compare(second, first) > 0);
    }

    private static ArtworkCandidate Candidate(
        string id,
        ArtworkMatchTier tier,
        int width,
        int height,
        long bytes) => new()
        {
            CandidateId = id,
            Provider = "test",
            ProviderItemId = id,
            ThumbnailUri = new Uri("https://example.invalid/" + id + "-thumb.jpg"),
            OriginalUri = new Uri("https://example.invalid/" + id + ".jpg"),
            MatchTier = tier,
            ProviderConfidence = ArtworkProviderConfidence.CoverArtArchiveApproved,
            MatchReason = "test",
            Width = width,
            Height = height,
            ByteLength = bytes,
            IsFront = true,
            IsApproved = true
        };
}
