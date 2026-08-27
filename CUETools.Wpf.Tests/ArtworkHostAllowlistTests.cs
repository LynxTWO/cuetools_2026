using System;
using System.IO;
using System.Reflection;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// F-24: artwork named by a database response used to be fetched from any HTTPS host,
/// because the ExternalArtwork policy's allowlist arm was "everything". A response must
/// not choose which host the app connects to.
/// </summary>
[TestClass]
public sealed class ArtworkHostAllowlistTests
{
    private static readonly MethodInfo Validate =
        typeof(AlbumArtService).GetMethod("ValidateUri",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ValidateUri not found");

    // ProviderPolicy is a private nested enum: reached by reflection so the policy can be
    // pinned without widening production visibility for a test.
    private static readonly Type PolicyType =
        typeof(AlbumArtService).GetNestedType("ProviderPolicy", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ProviderPolicy not found");

    private static object Policy(string name) => Enum.Parse(PolicyType, name);

    private static void Check(string url, string policy)
        => Validate.Invoke(null, new object[] { new Uri(url), Policy(policy) });

    private static void AssertRejected(string url, string policy)
    {
        try
        {
            Check(url, policy);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException)
        {
            return;
        }
        Assert.Fail($"{url} should not be fetchable under {policy}");
    }

    [TestMethod]
    public void DatabaseNamedArtworkIsLimitedToTheKnownArtworkHosts()
    {
        Check("https://coverartarchive.org/release/1/front.jpg", "ExternalArtwork");
        Check("https://ia800207.us.archive.org/art.jpg", "ExternalArtwork");
        Check("https://db.cuetools.net/art.jpg", "ExternalArtwork");
        Check("https://db.cue.tools/art.jpg", "ExternalArtwork");
        Check("https://musicbrainz.org/release/1/front", "ExternalArtwork");
    }

    [TestMethod]
    public void ADatabaseCannotSendTheAppToAnyOtherHost()
    {
        AssertRejected("https://example.com/art.jpg", "ExternalArtwork");
        AssertRejected("https://coverartarchive.org.evil.test/art.jpg", "ExternalArtwork");
        AssertRejected("https://notarchive.org/art.jpg", "ExternalArtwork");
    }

    [TestMethod]
    public void PlainHttpAndNonDefaultPortsStayRejected()
    {
        AssertRejected("http://coverartarchive.org/art.jpg", "ExternalArtwork");
        AssertRejected("https://coverartarchive.org:8443/art.jpg", "ExternalArtwork");
    }

    [TestMethod]
    public void LocalAndPrivateAddressesStayRejected()
    {
        AssertRejected("https://localhost/art.jpg", "ExternalArtwork");
        AssertRejected("https://127.0.0.1/art.jpg", "ExternalArtwork");
        AssertRejected("https://192.168.1.10/art.jpg", "ExternalArtwork");
    }

    [TestMethod]
    public void TheNamedProvidersKeepTheirOwnHosts()
    {
        Check("https://coverartarchive.org/release/1/front.jpg", "CoverArtArchive");
        Check("https://r2.theaudiodb.com/images/album.jpg", "TheAudioDb");
        Check("https://musicbrainz.org/ws/2/release/1", "MusicBrainz");
        AssertRejected("https://example.com/art.jpg", "CoverArtArchive");
        AssertRejected("https://example.com/art.jpg", "TheAudioDb");
        AssertRejected("https://coverartarchive.org/art.jpg", "MusicBrainz");
    }
}
