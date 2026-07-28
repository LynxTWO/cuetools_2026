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
