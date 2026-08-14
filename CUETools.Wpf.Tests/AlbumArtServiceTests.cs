using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CUETools.Processor;
using CUETools.Wpf.Services;
using CUETools.Wpf.Services.Artwork;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class AlbumArtServiceTests
{
    private static readonly Guid ReleaseId =
        Guid.Parse("76df3287-6cda-33eb-8e9a-044b5e15ffdd");

    [TestMethod]
    public async Task ExactMusicBrainzReleaseIncludesOtherArtButOnlyFrontIsAutomatic()
    {
        const string manifest =
            """
            {
              "images": [
                {
                  "id": 41,
                  "image": "http://coverartarchive.org/release/x/41.jpg",
                  "thumbnails": { "500": "http://coverartarchive.org/release/x/41-500.jpg" },
                  "front": true,
                  "approved": true,
                  "types": ["Front"]
                },
                {
                  "id": 42,
                  "image": "https://coverartarchive.org/release/x/42.jpg",
                  "thumbnails": { "250": "https://coverartarchive.org/release/x/42-250.jpg" },
                  "front": false,
                  "approved": true,
                  "types": ["Back"]
                }
              ]
            }
            """;
        using var service = new AlbumArtService(
            new DelegateHandler((_, _) => Json(manifest)),
            new NullLog(), new WpfImageTranscoder());
        ArtworkQuery query = Query("musicbrainz", ReleaseId.ToString("D"));

        var candidates = await service.FindCandidatesAsync(query);

        Assert.AreEqual(2, candidates.Count);
        ArtworkCandidate candidate = candidates.Single(item => item.IsFront);
        Assert.AreEqual("Cover Art Archive", candidate.Provider);
        Assert.AreEqual(ArtworkMatchTier.ExactRelease, candidate.MatchTier);
        Assert.IsTrue(candidate.IsFront);
        Assert.IsTrue(candidate.IsApproved);
        Assert.IsTrue(candidate.AutomaticEligible);
        ArtworkCandidate back = candidates.Single(item => !item.IsFront);
        Assert.IsFalse(back.AutomaticEligible);
        Assert.AreEqual("Back", back.ArtworkType);
        Assert.AreEqual(Uri.UriSchemeHttps, candidate.OriginalUri.Scheme);
    }

    [TestMethod]
    public async Task PrimarySearchReturnsFrontArtworkButExtensiveIncludesOtherTypes()
    {
        const string manifest =
            """
            {"images":[
              {"id":1,"image":"https://coverartarchive.org/release/x/front.jpg",
               "thumbnails":{},"front":true,"approved":true,"types":["Front"]},
              {"id":2,"image":"https://coverartarchive.org/release/x/back.jpg",
               "thumbnails":{},"front":false,"approved":true,"types":["Back"]}
            ]}
            """;
        using var service = new AlbumArtService(
            new DelegateHandler((_, _) => Json(manifest)),
            new NullLog(), new WpfImageTranscoder());
        ArtworkQuery query = Query(
            "musicbrainz",
            ReleaseId.ToString("D")) with
        {
            SearchMode = CUEConfigAdvanced.CTDBCoversSearch.Primary
        };

        IReadOnlyList<ArtworkCandidate> candidates =
            await service.FindCandidatesAsync(query);

        Assert.AreEqual(1, candidates.Count);
        Assert.IsTrue(candidates[0].IsFront);
    }

    [TestMethod]
    public async Task NoneSearchPerformsNoProviderRequest()
    {
        int requests = 0;
        using var service = new AlbumArtService(
            new DelegateHandler((_, _) =>
            {
                requests++;
                return Json("{}");
            }),
            new NullLog(), new WpfImageTranscoder());
        ArtworkQuery query = Query(
            "musicbrainz",
            ReleaseId.ToString("D")) with
        {
            SearchMode = CUEConfigAdvanced.CTDBCoversSearch.None
        };

        IReadOnlyList<ArtworkCandidate> candidates =
            await service.FindCandidatesAsync(query);

        Assert.AreEqual(0, candidates.Count);
        Assert.AreEqual(0, requests);
    }

    [TestMethod]
    public void TheAudioDbParserLabelsFrontAndBrowserOnlyArtwork()
    {
        byte[] json = Encoding.UTF8.GetBytes(
            """
            {"album":[{
              "idAlbum":"2109615",
              "strAlbumThumb":"https://r2.theaudiodb.com/front.jpg",
              "strAlbumThumbHQ":"https://r2.theaudiodb.com/front-hq.jpg",
              "strAlbumBack":"https://r2.theaudiodb.com/back.jpg",
              "strAlbumCDart":"https://r2.theaudiodb.com/disc.png"
            }]}
            """);

        IReadOnlyList<ArtworkCandidate> candidates =
            AlbumArtService.ParseTheAudioDb(
                json,
                ArtworkMatchTier.ReleaseGroup);

        Assert.AreEqual(3, candidates.Count);
        ArtworkCandidate front = candidates.Single(item => item.IsFront);
        Assert.AreEqual("TheAudioDB", front.Provider);
        Assert.IsTrue(front.AutomaticEligible);
        Assert.AreEqual(
            ArtworkProviderConfidence.TheAudioDbReleaseGroup,
            front.ProviderConfidence);
        Assert.IsTrue(candidates.Where(item => !item.IsFront)
            .All(item => !item.AutomaticEligible));
    }

    [TestMethod]
    public void TheAudioDbTextFallbackRejectsAnotherAlbumAndDemotesYearMismatch()
    {
        byte[] json = Encoding.UTF8.GetBytes(
            """
            {"album":[
              {
                "idAlbum":"1","strArtist":"Exact Artist","strAlbum":"Exact Album",
                "intYearReleased":"1999",
                "strAlbumThumb":"https://r2.theaudiodb.com/right.jpg"
              },
              {
                "idAlbum":"2","strArtist":"Other Artist","strAlbum":"Other Album",
                "intYearReleased":"2026",
                "strAlbumThumb":"https://r2.theaudiodb.com/wrong.jpg"
              }
            ]}
            """);

        IReadOnlyList<ArtworkCandidate> candidates =
            AlbumArtService.ParseTheAudioDb(
                json,
                ArtworkMatchTier.StrongText,
                "Exact Artist",
                "Exact Album",
                "2026");

        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(ArtworkMatchTier.WeakText, candidates[0].MatchTier);
        Assert.IsFalse(candidates[0].AutomaticEligible);
    }

    [TestMethod]
    public async Task EnabledTheAudioDbUsesKeyedOfficialEndpointAndRedactsKey()
    {
        const string key = "unit-key-7";
        var settings = new AppSettings
        {
            TheAudioDbEnabled = true,
            TheAudioDbApiKey = key
        };
        var log = new RecordingLog();
        Uri requested = null;
        using var service = new AlbumArtService(
            new DelegateHandler((request, _) =>
            {
                requested = request.RequestUri;
                return Json(
                    """
                    {"album":[{
                      "idAlbum":"7",
                      "strArtist":"artist",
                      "strAlbum":"album",
                      "strAlbumThumb":"https://r2.theaudiodb.com/front.jpg"
                    }]}
                    """);
            }),
            settings,
            log,
            new WpfImageTranscoder());

        IReadOnlyList<ArtworkCandidate> candidates =
            await service.FindCandidatesAsync(Query("", ""));

        Assert.AreEqual(1, candidates.Count);
        Assert.IsNotNull(requested);
        Assert.AreEqual("www.theaudiodb.com", requested.Host);
        StringAssert.Contains(requested.AbsolutePath, "/api/v1/json/" + key + "/");
        CollectionAssert.Contains(log.Redactions, key);
        Assert.IsFalse(string.Join("\n", log.Messages)
            .Contains(key, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task TheAudioDbRetriesOneRateLimitAndRejectsForeignImageHost()
    {
        var settings = new AppSettings
        {
            TheAudioDbEnabled = true,
            TheAudioDbApiKey = "rate-test-key"
        };
        int requests = 0;
        using var service = new AlbumArtService(
            new DelegateHandler((_, _) =>
            {
                requests++;
                if (requests == 1)
                {
                    var limited = new HttpResponseMessage(
                        HttpStatusCode.TooManyRequests);
                    limited.Headers.RetryAfter =
                        new System.Net.Http.Headers.RetryConditionHeaderValue(
                            TimeSpan.Zero);
                    return limited;
                }
                return Json(
                    """
                    {"album":[{
                      "idAlbum":"8",
                      "strArtist":"artist",
                      "strAlbum":"album",
                      "strAlbumThumb":"https://r2.theaudiodb.com/front.jpg"
                    }]}
                    """);
            }),
            settings,
            new NullLog(), new WpfImageTranscoder());

        IReadOnlyList<ArtworkCandidate> candidates =
            await service.FindCandidatesAsync(Query("", ""));

        Assert.AreEqual(2, requests);
        Assert.AreEqual(1, candidates.Count);

        ArtworkCandidate foreign = candidates[0] with
        {
            OriginalUri = new Uri("https://example.com/not-provider-art.jpg")
        };
        await Assert.ThrowsExceptionAsync<InvalidDataException>(
            () => service.DownloadAsync(foreign, thumbnail: false));
    }

    [TestMethod]
    public async Task ManifestContentLengthAboveLimitFailsClosed()
    {
        using var service = new AlbumArtService(
            new DelegateHandler((_, _) =>
            {
                var response = Json("{}");
                response.Content.Headers.ContentLength =
                    AlbumArtService.MaxManifestBytes + 1;
                return response;
            }),
            new NullLog(), new WpfImageTranscoder());

        var candidates = await service.FindCandidatesAsync(
            Query("musicbrainz", "99b09d02-9cc9-3fed-8431-f162165a9371"));

        Assert.AreEqual(0, candidates.Count);
    }

    [TestMethod]
    public async Task CoverArtRedirectOutsideProviderIsRejected()
    {
        using var service = new AlbumArtService(
            new DelegateHandler((_, _) => new HttpResponseMessage(
                HttpStatusCode.TemporaryRedirect)
            {
                Headers = { Location = new Uri("https://example.com/cover.jpg") }
            }),
            new NullLog(), new WpfImageTranscoder());
        ArtworkCandidate candidate = Candidate(
            new Uri("https://coverartarchive.org/release/x/41.jpg"));

        await Assert.ThrowsExceptionAsync<InvalidDataException>(
            () => service.DownloadAsync(candidate, thumbnail: false));
    }

    [TestMethod]
    public async Task MissingExactArtFallsBackToClearlyLabeledReleaseGroup()
    {
        Guid release = Guid.Parse("2ba4396d-c0be-4a56-b4ea-0438306eb3be");
        Guid group = Guid.Parse("c31a5e2b-0bf8-32e0-8aeb-ef4ba9973932");
        using var service = new AlbumArtService(
            new DelegateHandler((request, _) =>
            {
                string path = request.RequestUri.AbsolutePath;
                if (request.RequestUri.Host == "musicbrainz.org")
                    return Json(
                        "{\"release-group\":{\"id\":\"" +
                        group.ToString("D") +
                        "\"}}");
                if (path.StartsWith("/release-group/", StringComparison.Ordinal))
                    return Json(
                        """
                        {"images":[{
                          "id":77,
                          "image":"https://coverartarchive.org/release-group/x/77.jpg",
                          "thumbnails":{"250":"https://coverartarchive.org/release-group/x/77-250.jpg"},
                          "front":true,
                          "approved":true,
                          "types":["Front"]
                        }]}
                        """);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }),
            new NullLog(), new WpfImageTranscoder());

        var candidates = await service.FindCandidatesAsync(
            Query("musicbrainz", release.ToString("D")));

        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(ArtworkMatchTier.ReleaseGroup, candidates[0].MatchTier);
        StringAssert.Contains(candidates[0].MatchReason, "edition may differ");
    }

    [TestMethod]
    public void ImageHeaderReaderAcceptsPngAndRejectsUnknownInput()
    {
        byte[] pngHeader =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52,
            0, 0, 0x05, 0x78,
            0, 0, 0x05, 0x78
        };

        Assert.IsTrue(AlbumArtService.TryReadImageDimensions(
            pngHeader, out int width, out int height));
        Assert.AreEqual(1400, width);
        Assert.AreEqual(1400, height);
        Assert.IsFalse(AlbumArtService.TryReadImageDimensions(
            new byte[] { 1, 2, 3 }, out _, out _));
    }

    [TestMethod]
    public void LocalPngAndBmpAreBoundedAndConvertedToJpeg()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "cuetools-local-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string png = Path.Combine(root, "cover.png");
            string bmp = Path.Combine(root, "cover.bmp");
            File.WriteAllBytes(png, MakeImage(new PngBitmapEncoder(), 160, 80));
            File.WriteAllBytes(bmp, MakeImage(new BmpBitmapEncoder(), 140, 70));
            using var service = new AlbumArtService(
                new DelegateHandler((_, _) => Json("{}")),
                new NullLog(), new WpfImageTranscoder());

            AlbumArt pngArt = service.ImportLocalFile(png, 100);
            AlbumArt bmpArt = service.ImportLocalFile(bmp, 100);
            byte[] pngJpeg = pngArt.PreparedJpeg!;
            byte[] bmpJpeg = bmpArt.PreparedJpeg!;

            Assert.AreEqual("image/png", pngArt.Candidate.MimeType);
            Assert.AreEqual("image/bmp", bmpArt.Candidate.MimeType);
            Assert.AreEqual("Local file", pngArt.Candidate.Provider);
            Assert.AreEqual(0xFF, pngJpeg[0]);
            Assert.AreEqual(0xD8, pngJpeg[1]);
            Assert.AreEqual(0xFF, bmpJpeg[0]);
            Assert.AreEqual(0xD8, bmpJpeg[1]);
            Assert.IsTrue(AlbumArtService.TryReadImageDimensions(
                pngJpeg, out int width, out int height));
            Assert.AreEqual(100, width);
            Assert.AreEqual(50, height);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void InLimitLocalJpegRemainsByteIdentical()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "cuetools-local-art-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            byte[] jpeg = MakeImage(new JpegBitmapEncoder { QualityLevel = 92 }, 80, 80);
            File.WriteAllBytes(path, jpeg);
            using var service = new AlbumArtService(
                new DelegateHandler((_, _) => Json("{}")),
                new NullLog(), new WpfImageTranscoder());

            AlbumArt art = service.ImportLocalFile(path, 100);

            CollectionAssert.AreEqual(jpeg, art.PreparedJpeg);
            Assert.AreEqual("image/jpeg", art.Candidate.MimeType);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [TestMethod]
    public void LocalImportRejectsDirectoriesAndUnsafeBitmapDimensions()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "cuetools-local-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var service = new AlbumArtService(
                new DelegateHandler((_, _) => Json("{}")),
                new NullLog(), new WpfImageTranscoder());
            Assert.ThrowsException<InvalidDataException>(
                () => service.ImportLocalFile(root, 1000));

            string large = Path.Combine(root, "too-large.jpg");
            using (var stream = new FileStream(large, FileMode.CreateNew))
                stream.SetLength(AlbumArtService.MaxMasterBytes + 1L);
            Assert.ThrowsException<InvalidDataException>(
                () => service.ImportLocalFile(large, 1000));

            byte[] header = new byte[54];
            header[0] = 0x42;
            header[1] = 0x4D;
            BitConverter.GetBytes(40).CopyTo(header, 14);
            BitConverter.GetBytes(20_000).CopyTo(header, 18);
            BitConverter.GetBytes(20_000).CopyTo(header, 22);
            Assert.IsTrue(AlbumArtService.TryReadImageDimensions(
                header, out int width, out int height));
            Assert.AreEqual(20_000, width);
            Assert.AreEqual(20_000, height);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static byte[] MakeImage(
        BitmapEncoder encoder,
        int width,
        int height)
    {
        byte[] pixels = Enumerable.Repeat((byte)0xCC, width * height * 4).ToArray();
        BitmapSource source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static ArtworkQuery Query(string provider, string id) => new(
        "artist", "album", "2026", "", 10, "", "", provider, id, "", 1);

    private static ArtworkCandidate Candidate(Uri uri) => new()
    {
        CandidateId = "candidate",
        Provider = "Cover Art Archive",
        ProviderItemId = "41",
        ThumbnailUri = uri,
        OriginalUri = uri,
        MatchTier = ArtworkMatchTier.ExactRelease,
        ProviderConfidence = ArtworkProviderConfidence.CoverArtArchiveApproved,
        MatchReason = "test",
        IsFront = true,
        IsApproved = true
    };

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _send;

        public DelegateHandler(
            Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send) =>
            _send = send;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_send(request, cancellationToken));
    }

    private sealed class NullLog : IDiagnosticLog
    {
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message, Exception ex = null) { }
        public void Redact(params string[] sensitive) { }
        public string LogPath => "";
    }

    private sealed class RecordingLog : IDiagnosticLog
    {
        public readonly System.Collections.Generic.List<string> Messages = new();
        public readonly System.Collections.Generic.List<string> Redactions = new();
        public void Info(string category, string message) =>
            Messages.Add(category + ": " + message);
        public void Warn(string category, string message) =>
            Messages.Add(category + ": " + message);
        public void Error(string category, string message, Exception ex = null) =>
            Messages.Add(category + ": " + message);
        public void Redact(params string[] sensitive) =>
            Redactions.AddRange(sensitive.Where(item => !string.IsNullOrEmpty(item)));
        public string LogPath => "";
    }
}
