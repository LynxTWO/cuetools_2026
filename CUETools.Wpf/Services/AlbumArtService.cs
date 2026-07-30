using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CUETools.Processor;
using CUETools.Wpf.Imaging;
using CUETools.Wpf.Services.Artwork;

namespace CUETools.Wpf.Services;

public sealed record AlbumArt(
    ArtworkCandidate Candidate,
    byte[] Bytes,
    int Width,
    int Height,
    byte[]? PreparedJpeg = null);

public interface IAlbumArtService
{
    Task<IReadOnlyList<ArtworkCandidate>> FindCandidatesAsync(
        ArtworkQuery query,
        CancellationToken ct = default);

    Task<AlbumArt?> DownloadAsync(
        ArtworkCandidate candidate,
        bool thumbnail,
        CancellationToken ct = default);

    AlbumArt ImportLocalFile(string path, int maxSize, int quality = 92);
    byte[]? ResizeToJpeg(byte[] source, int maxSize, int quality = 92);
}

/// <summary>
/// Discovers release-bound CTDB and Cover Art Archive candidates, then downloads only requested
/// thumbnails or the selected master through one bounded network and image-validation path.
/// Apple Search API art is deliberately absent because its public terms describe promotional
/// store use rather than embedding artwork into local audio files.
/// </summary>
public sealed class AlbumArtService : IAlbumArtService, IDisposable
{
    internal const int MaxManifestBytes = 2 * 1024 * 1024;
    internal const int MaxThumbnailBytes = 5 * 1024 * 1024;
    internal const int MaxMasterBytes = 30 * 1024 * 1024;
    internal const long MaxPixels = 100_000_000;
    private const int MaxRedirects = 5;
    private const int MaxCandidatesPerProvider = 10;

    private static readonly SemaphoreSlim MusicBrainzGate = new(1, 1);
    private static readonly SemaphoreSlim TheAudioDbGate = new(1, 1);
    private static DateTime _lastMusicBrainzRequestUtc = DateTime.MinValue;
    private static DateTime _lastTheAudioDbRequestUtc = DateTime.MinValue;
    private static readonly ConcurrentDictionary<string, ManifestCacheEntry> ManifestCache =
        new(StringComparer.Ordinal);

    private readonly HttpClient _http;
    private readonly IDiagnosticLog _log;
    private readonly AppSettings _settings;

    public AlbumArtService(CUEConfig config, AppSettings settings, IDiagnosticLog log)
        : this(CreateHandler(config), settings, log)
    {
    }

    internal AlbumArtService(HttpMessageHandler handler, IDiagnosticLog log)
        : this(handler, new AppSettings(), log)
    {
    }

    internal AlbumArtService(
        HttpMessageHandler handler,
        AppSettings settings,
        IDiagnosticLog log)
    {
        _settings = settings;
        _log = log;
        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CUETools", "2026.1"));
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("(https://github.com/LynxTWO/cuetools_2026)"));
    }

    private static HttpMessageHandler CreateHandler(CUEConfig config)
    {
        IWebProxy? proxy = config.GetProxy();
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            Proxy = proxy,
            UseProxy = proxy != null
        };
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> FindCandidatesAsync(
        ArtworkQuery query,
        CancellationToken ct = default)
    {
        if (query.SearchMode == CUEConfigAdvanced.CTDBCoversSearch.None)
            return Array.Empty<ArtworkCandidate>();

        var candidates = new List<ArtworkCandidate>();
        AddMetadataCandidates(query, candidates);

        var releases = new List<ReleaseIdentity>();
        if (IsMusicBrainzSource(query.ProviderKey) &&
            Guid.TryParse(query.ProviderId, out Guid releaseId))
        {
            releases.Add(new ReleaseIdentity(
                releaseId,
                null,
                ArtworkMatchTier.ExactRelease,
                "MusicBrainz release selected for this disc"));
        }
        else if (!string.IsNullOrWhiteSpace(query.DiscId))
        {
            try
            {
                releases.AddRange(await LookupDiscReleasesAsync(query, ct));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warn("art", "MusicBrainz lookup unavailable: " + ex.GetType().Name);
            }
        }

        foreach (var release in releases
                     .DistinctBy(item => item.ReleaseId)
                     .Take(5))
        {
            try
            {
                IReadOnlyList<ArtworkCandidate> releaseCandidates =
                    await FetchCoverArtArchiveAsync(
                        release.ReleaseId,
                        release.Tier,
                        release.Reason,
                        releaseGroup: false,
                        ct);
                candidates.AddRange(releaseCandidates);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warn("art", "Cover Art Archive release unavailable: " + ex.GetType().Name);
            }
        }

        var releaseGroups = new HashSet<Guid>();
        if (candidates.Count == 0 || IsTheAudioDbEnabled())
        {
            foreach (Guid groupId in releases
                         .Where(item => item.ReleaseGroupId.HasValue)
                         .Select(item => item.ReleaseGroupId!.Value))
                releaseGroups.Add(groupId);
            if (releaseGroups.Count == 0 && releases.Count == 1)
            {
                try
                {
                    Guid? groupId = await LookupReleaseGroupAsync(
                        releases[0].ReleaseId,
                        ct);
                    if (groupId.HasValue) releaseGroups.Add(groupId.Value);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.Warn("art", "MusicBrainz release-group lookup unavailable: " +
                                     ex.GetType().Name);
                }
            }
        }
        foreach (Guid groupId in releaseGroups.Take(3))
        {
            if (candidates.Count == 0)
            {
                try
                {
                    candidates.AddRange(await FetchCoverArtArchiveAsync(
                        groupId,
                        ArtworkMatchTier.ReleaseGroup,
                        "Canonical release-group artwork; edition may differ",
                        releaseGroup: true,
                        ct));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.Warn("art", "Cover Art Archive release group unavailable: " +
                                     ex.GetType().Name);
                }
            }
        }

        if (IsTheAudioDbEnabled())
        {
            try
            {
                candidates.AddRange(await FetchTheAudioDbAsync(
                    query,
                    releaseGroups.FirstOrDefault(),
                    ct));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warn("art", "TheAudioDB unavailable: " + ex.GetType().Name);
            }
        }

        candidates.Sort(ArtworkCandidateComparer.Recommended);
        IEnumerable<ArtworkCandidate> selected = candidates;
        if (query.SearchMode == CUEConfigAdvanced.CTDBCoversSearch.Primary)
            selected = selected.Where(candidate => candidate.IsFront);
        return selected
            .DistinctBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .Take(MaxCandidatesPerProvider * 2)
            .ToArray();
    }

    private static void AddMetadataCandidates(
        ArtworkQuery query,
        ICollection<ArtworkCandidate> destination)
    {
        int order = 0;
        foreach (var image in query.MetadataArtwork ?? Array.Empty<CUETools.CTDB.CTDBResponseMetaImage>())
        {
            string original = image.uri ?? "";
            string thumb = string.IsNullOrWhiteSpace(image.uri150) ? original : image.uri150;
            if (!TryHttpsUri(original, out Uri? originalUri) || originalUri == null ||
                !TryHttpsUri(thumb, out Uri? thumbnailUri) || thumbnailUri == null)
            {
                continue;
            }

            string id = "ctdb:" + order + ":" + originalUri.AbsoluteUri;
            destination.Add(new ArtworkCandidate
            {
                CandidateId = id,
                Provider = "CTDB metadata",
                ProviderItemId = query.ProviderId ?? "",
                ThumbnailUri = thumbnailUri,
                OriginalUri = originalUri,
                MatchTier = ArtworkMatchTier.MetadataRelease,
                ProviderConfidence = image.primary
                    ? ArtworkProviderConfidence.CtdbPrimary
                    : ArtworkProviderConfidence.CoverArtArchiveUnapproved,
                MatchReason = "Artwork returned with the selected metadata release",
                Width = image.width > 0 ? image.width : null,
                Height = image.height > 0 ? image.height : null,
                IsFront = true,
                IsPrimary = image.primary,
                InfoUri = TryHttpsUri(query.InfoUrl, out Uri? infoUri)
                    ? infoUri
                    : null,
                ProviderOrder = order++
            });
        }
    }

    private async Task<IReadOnlyList<ReleaseIdentity>>
        LookupDiscReleasesAsync(ArtworkQuery query, CancellationToken ct)
    {
        await MusicBrainzGate.WaitAsync(ct);
        try
        {
            TimeSpan since = DateTime.UtcNow - _lastMusicBrainzRequestUtc;
            if (since < TimeSpan.FromSeconds(1))
                await Task.Delay(TimeSpan.FromSeconds(1) - since, ct);

            string url = "https://musicbrainz.org/ws/2/discid/" +
                         Uri.EscapeDataString(query.DiscId) +
                         "?inc=release-groups+media&fmt=json";
            byte[] json;
            try
            {
                json = await GetBytesAsync(
                    new Uri(url),
                    MaxManifestBytes,
                    ProviderPolicy.MusicBrainz,
                    ct);
                _lastMusicBrainzRequestUtc = DateTime.UtcNow;
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode == HttpStatusCode.NotFound &&
                !string.IsNullOrWhiteSpace(query.Toc))
            {
                _lastMusicBrainzRequestUtc = DateTime.UtcNow;
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                string fuzzyUrl =
                    "https://musicbrainz.org/ws/2/discid/-?toc=" +
                    Uri.EscapeDataString(query.Toc) +
                    "&cdstubs=no&inc=release-groups+media&fmt=json";
                json = await GetBytesAsync(
                    new Uri(fuzzyUrl),
                    MaxManifestBytes,
                    ProviderPolicy.MusicBrainz,
                    ct);
                _lastMusicBrainzRequestUtc = DateTime.UtcNow;
            }

            using JsonDocument document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = 32 });
            if (!document.RootElement.TryGetProperty("releases", out JsonElement releases) ||
                releases.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ReleaseIdentity>();
            }

            string barcode = NormalizeBarcode(query.Barcode);
            var results = new List<ReleaseIdentity>();
            foreach (JsonElement release in releases.EnumerateArray().Take(10))
            {
                if (!release.TryGetProperty("id", out JsonElement idElement) ||
                    !Guid.TryParse(idElement.GetString(), out Guid id))
                {
                    continue;
                }

                string releaseBarcode = release.TryGetProperty("barcode", out JsonElement barcodeElement)
                    ? NormalizeBarcode(barcodeElement.GetString())
                    : "";
                bool exactBarcode = barcode.Length >= 8 &&
                                    string.Equals(barcode, releaseBarcode, StringComparison.Ordinal);
                Guid? groupId = null;
                if (release.TryGetProperty("release-group", out JsonElement groupElement) &&
                    groupElement.TryGetProperty("id", out JsonElement groupIdElement) &&
                    Guid.TryParse(groupIdElement.GetString(), out Guid parsedGroupId))
                {
                    groupId = parsedGroupId;
                }
                results.Add(new ReleaseIdentity(
                    id,
                    groupId,
                    exactBarcode ? ArtworkMatchTier.ExactRelease : ArtworkMatchTier.MetadataRelease,
                    exactBarcode
                        ? "MusicBrainz disc and barcode match"
                        : "MusicBrainz disc ID match"));
            }
            return results;
        }
        finally
        {
            MusicBrainzGate.Release();
        }
    }

    private async Task<Guid?> LookupReleaseGroupAsync(
        Guid releaseId,
        CancellationToken ct)
    {
        await MusicBrainzGate.WaitAsync(ct);
        try
        {
            TimeSpan since = DateTime.UtcNow - _lastMusicBrainzRequestUtc;
            if (since < TimeSpan.FromSeconds(1))
                await Task.Delay(TimeSpan.FromSeconds(1) - since, ct);
            byte[] json = await GetBytesAsync(
                new Uri(
                    "https://musicbrainz.org/ws/2/release/" +
                    releaseId.ToString("D") +
                    "?inc=release-groups&fmt=json"),
                MaxManifestBytes,
                ProviderPolicy.MusicBrainz,
                ct);
            _lastMusicBrainzRequestUtc = DateTime.UtcNow;
            using JsonDocument document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.TryGetProperty(
                    "release-group",
                    out JsonElement group) &&
                group.TryGetProperty("id", out JsonElement idElement) &&
                Guid.TryParse(idElement.GetString(), out Guid id))
            {
                return id;
            }
            return null;
        }
        finally
        {
            MusicBrainzGate.Release();
        }
    }

    private async Task<IReadOnlyList<ArtworkCandidate>> FetchCoverArtArchiveAsync(
        Guid entityId,
        ArtworkMatchTier tier,
        string reason,
        bool releaseGroup,
        CancellationToken ct)
    {
        Uri manifestUri = new(
            "https://coverartarchive.org/" +
            (releaseGroup ? "release-group/" : "release/") +
            entityId.ToString("D"));
        byte[] json = await GetBytesAsync(
            manifestUri,
            MaxManifestBytes,
            ProviderPolicy.CoverArtArchive,
            ct);

        using JsonDocument document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { MaxDepth = 32 });
        if (!document.RootElement.TryGetProperty("images", out JsonElement images) ||
            images.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ArtworkCandidate>();
        }

        var candidates = new List<ArtworkCandidate>();
        int order = 0;
        foreach (JsonElement image in images.EnumerateArray())
        {
            if (candidates.Count >= MaxCandidatesPerProvider) break;
            bool front = image.TryGetProperty("front", out JsonElement frontElement) &&
                         frontElement.ValueKind == JsonValueKind.True;

            string original = image.TryGetProperty("image", out JsonElement originalElement)
                ? originalElement.GetString() ?? ""
                : "";
            string thumbnail = GetCaaThumbnail(image, original);
            if (!TryHttpsUri(original, out Uri? originalUri) || originalUri == null ||
                !TryHttpsUri(thumbnail, out Uri? thumbnailUri) || thumbnailUri == null)
            {
                order++;
                continue;
            }

            string itemId = image.TryGetProperty("id", out JsonElement itemElement)
                ? itemElement.ToString()
                : order.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bool approved = image.TryGetProperty("approved", out JsonElement approvedElement) &&
                            approvedElement.ValueKind == JsonValueKind.True;
            bool watermarked = HasType(image, "Watermark");
            candidates.Add(new ArtworkCandidate
            {
                CandidateId = "caa:" +
                              (releaseGroup ? "group:" : "release:") +
                              entityId.ToString("D") + ":" + itemId,
                Provider = "Cover Art Archive",
                ProviderItemId = itemId,
                ThumbnailUri = thumbnailUri,
                OriginalUri = originalUri,
                MatchTier = tier,
                ProviderConfidence = approved
                    ? ArtworkProviderConfidence.CoverArtArchiveApproved
                    : ArtworkProviderConfidence.CoverArtArchiveUnapproved,
                MatchReason = reason,
                IsFront = front,
                IsApproved = approved,
                IsWatermarked = watermarked,
                AutomaticEligible = front,
                ArtworkType = GetArtworkType(image),
                InfoUri = new Uri(
                    "https://musicbrainz.org/" +
                    (releaseGroup ? "release-group/" : "release/") +
                    entityId.ToString("D")),
                ProviderOrder = order++
            });
        }
        return candidates
            .OrderByDescending(candidate => candidate.IsFront)
            .ThenBy(candidate => candidate.ProviderOrder)
            .Take(MaxCandidatesPerProvider)
            .OrderBy(candidate => candidate.ProviderOrder)
            .ToArray();
    }

    private bool IsTheAudioDbEnabled() =>
        _settings.TheAudioDbEnabled &&
        !string.IsNullOrWhiteSpace(_settings.TheAudioDbApiKey);

    private async Task<IReadOnlyList<ArtworkCandidate>> FetchTheAudioDbAsync(
        ArtworkQuery query,
        Guid releaseGroupId,
        CancellationToken ct)
    {
        string key = _settings.TheAudioDbApiKey;
        _log.Redact(key);
        if (string.IsNullOrWhiteSpace(key))
            return Array.Empty<ArtworkCandidate>();

        await TheAudioDbGate.WaitAsync(ct);
        try
        {
            TimeSpan since = DateTime.UtcNow - _lastTheAudioDbRequestUtc;
            if (since < TimeSpan.FromSeconds(2))
                await Task.Delay(TimeSpan.FromSeconds(2) - since, ct);

            string endpoint = releaseGroupId != Guid.Empty
                ? "album-mb.php?i=" + releaseGroupId.ToString("D")
                : "searchalbum.php?s=" + Uri.EscapeDataString(query.Artist) +
                  "&a=" + Uri.EscapeDataString(query.Album);
            byte[] json = await GetBytesAsync(
                new Uri(
                    "https://www.theaudiodb.com/api/v1/json/" +
                    Uri.EscapeDataString(key) + "/" + endpoint),
                MaxManifestBytes,
                ProviderPolicy.TheAudioDb,
                ct);
            _lastTheAudioDbRequestUtc = DateTime.UtcNow;
            return ParseTheAudioDb(
                json,
                releaseGroupId != Guid.Empty
                    ? ArtworkMatchTier.ReleaseGroup
                    : ArtworkMatchTier.StrongText,
                query.Artist,
                query.Album,
                query.Year);
        }
        finally
        {
            TheAudioDbGate.Release();
        }
    }

    internal static IReadOnlyList<ArtworkCandidate> ParseTheAudioDb(
        byte[] json,
        ArtworkMatchTier tier,
        string queryArtist = "",
        string queryAlbum = "",
        string queryYear = "")
    {
        using JsonDocument document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { MaxDepth = 32 });
        if (!document.RootElement.TryGetProperty("album", out JsonElement albums) ||
            albums.ValueKind != JsonValueKind.Array)
            return Array.Empty<ArtworkCandidate>();

        var candidates = new List<ArtworkCandidate>();
        int order = 0;
        foreach (JsonElement album in albums.EnumerateArray().Take(5))
        {
            ArtworkMatchTier albumTier = tier;
            if (tier <= ArtworkMatchTier.StrongText)
            {
                string resultArtist = StringProperty(album, "strArtist");
                string resultAlbum = StringProperty(album, "strAlbum");
                if (!SameNormalizedText(queryAlbum, resultAlbum) ||
                    !SameNormalizedText(queryArtist, resultArtist))
                    continue;
                string resultYear = StringProperty(album, "intYearReleased");
                if (!string.IsNullOrWhiteSpace(queryYear) &&
                    !string.IsNullOrWhiteSpace(resultYear) &&
                    !string.Equals(
                        queryYear.Trim(),
                        resultYear.Trim(),
                        StringComparison.Ordinal))
                    albumTier = ArtworkMatchTier.WeakText;
            }
            string albumId = StringProperty(album, "idAlbum");
            string page = string.IsNullOrWhiteSpace(albumId)
                ? "https://www.theaudiodb.com/"
                : "https://www.theaudiodb.com/album/" + Uri.EscapeDataString(albumId);
            AddTheAudioDbImage(
                candidates,
                album,
                "strAlbumThumbHQ",
                "strAlbumThumb",
                albumId,
                "Front",
                true,
                albumTier,
                page,
                ref order);
            AddTheAudioDbImage(
                candidates,
                album,
                "strAlbumBack",
                null,
                albumId,
                "Back",
                false,
                albumTier,
                page,
                ref order);
            AddTheAudioDbImage(
                candidates,
                album,
                "strAlbumCDart",
                null,
                albumId,
                "Medium",
                false,
                albumTier,
                page,
                ref order);
            AddTheAudioDbImage(
                candidates,
                album,
                "strAlbumSpine",
                null,
                albumId,
                "Spine",
                false,
                albumTier,
                page,
                ref order);
        }
        return candidates;
    }

    private static void AddTheAudioDbImage(
        ICollection<ArtworkCandidate> candidates,
        JsonElement album,
        string originalProperty,
        string? fallbackProperty,
        string albumId,
        string artworkType,
        bool front,
        ArtworkMatchTier tier,
        string page,
        ref int order)
    {
        string original = StringProperty(album, originalProperty);
        if (string.IsNullOrWhiteSpace(original) && fallbackProperty != null)
            original = StringProperty(album, fallbackProperty);
        if (!TryHttpsUri(original, out Uri? originalUri) || originalUri == null)
            return;
        string thumbnail = StringProperty(album, "strAlbumThumb");
        if (!TryHttpsUri(thumbnail, out Uri? thumbnailUri) || thumbnailUri == null)
            thumbnailUri = originalUri;
        if (!TryHttpsUri(page, out Uri? infoUri))
            infoUri = null;

        candidates.Add(new ArtworkCandidate
        {
            CandidateId = "tadb:" + albumId + ":" +
                          originalProperty.ToLowerInvariant(),
            Provider = "TheAudioDB",
            ProviderItemId = albumId,
            ThumbnailUri = thumbnailUri,
            OriginalUri = originalUri,
            MatchTier = tier,
            ProviderConfidence = tier == ArtworkMatchTier.ReleaseGroup
                ? ArtworkProviderConfidence.TheAudioDbReleaseGroup
                : ArtworkProviderConfidence.TextSearch,
            MatchReason = tier == ArtworkMatchTier.ReleaseGroup
                ? "TheAudioDB MusicBrainz release-group match"
                : "TheAudioDB artist and album text match",
            IsFront = front,
            IsPrimary = front,
            AutomaticEligible = front && tier >= ArtworkMatchTier.StrongText,
            ArtworkType = artworkType,
            InfoUri = infoUri,
            ProviderOrder = order++
        });
    }

    private static string StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool SameNormalizedText(string expected, string actual)
    {
        static string Normalize(string value) => new string(
            (value ?? "")
            .Normalize(NormalizationForm.FormKC)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        string left = Normalize(expected);
        return left.Length > 0 &&
               string.Equals(left, Normalize(actual), StringComparison.Ordinal);
    }

    public async Task<AlbumArt?> DownloadAsync(
        ArtworkCandidate candidate,
        bool thumbnail,
        CancellationToken ct = default)
    {
        Uri uri = thumbnail ? candidate.ThumbnailUri : candidate.OriginalUri;
        int limit = thumbnail ? MaxThumbnailBytes : MaxMasterBytes;
        ProviderPolicy policy = candidate.Provider switch
        {
            "Cover Art Archive" => ProviderPolicy.CoverArtArchive,
            "TheAudioDB" => ProviderPolicy.TheAudioDb,
            _ => ProviderPolicy.ExternalArtwork
        };
        byte[] bytes = await GetBytesAsync(uri, limit, policy, ct);
        if (!TryReadImageDimensions(bytes, out int width, out int height) ||
            width <= 0 || height <= 0 ||
            (long)width * height > MaxPixels)
        {
            throw new InvalidDataException("Artwork has an unsupported or unsafe image shape.");
        }
        if (Decode(bytes) == null)
            throw new InvalidDataException("Artwork could not be decoded.");
        return new AlbumArt(candidate, bytes, width, height);
    }

    public AlbumArt ImportLocalFile(string path, int maxSize, int quality = 92)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Choose one image file.");
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("The dropped item must be a regular image file.");

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            32 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > MaxMasterBytes)
            throw new InvalidDataException("The image exceeds the 30 MB input limit.");
        using var output = new MemoryStream((int)stream.Length);
        var buffer = new byte[32 * 1024];
        int total = 0;
        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total += read;
            if (total > MaxMasterBytes)
                throw new InvalidDataException("The image exceeds the 30 MB input limit.");
            output.Write(buffer, 0, read);
        }
        byte[] master = output.ToArray();
        if (!TryReadImageDimensions(master, out int width, out int height) ||
            (long)width * height > MaxPixels)
            throw new InvalidDataException("The image format or dimensions are unsafe.");
        byte[]? jpeg = ResizeToJpeg(master, Math.Clamp(maxSize, 100, 10000), quality);
        if (jpeg == null)
            throw new InvalidDataException("The image could not be decoded.");
        if (!TryReadImageDimensions(jpeg, out _, out _))
            throw new InvalidDataException("The converted JPEG is invalid.");

        var local = new ArtworkCandidate
        {
            CandidateId = "local:" + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(master)),
            Provider = "Local file",
            ProviderItemId = "user",
            ThumbnailUri = new Uri("https://local.invalid/cover"),
            OriginalUri = new Uri("https://local.invalid/cover"),
            MatchTier = ArtworkMatchTier.ExactRelease,
            ProviderConfidence = ArtworkProviderConfidence.CoverArtArchiveApproved,
            MatchReason = "User-selected cover for this release",
            Width = width,
            Height = height,
            ByteLength = master.LongLength,
            MimeType = DetectMimeType(master),
            IsFront = true,
            IsPrimary = true,
            AutomaticEligible = true,
            ArtworkType = "Front"
        };
        return new AlbumArt(local, master, width, height, jpeg);
    }

    private async Task<byte[]> GetBytesAsync(
        Uri initialUri,
        int maxBytes,
        ProviderPolicy policy,
        CancellationToken ct)
    {
        string cacheKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(initialUri.AbsoluteUri)));
        if (maxBytes == MaxManifestBytes &&
            ManifestCache.TryGetValue(cacheKey, out ManifestCacheEntry? cached) &&
            cached.ExpiresUtc > DateTime.UtcNow)
        {
            return (byte[])cached.Bytes.Clone();
        }

        Uri uri = initialUri;
        int rateLimitRetries = 0;
        for (int redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            ValidateUri(uri, policy);
            if (policy == ProviderPolicy.ExternalArtwork)
                await ValidatePublicHostAsync(uri, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using HttpResponseMessage response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects || response.Headers.Location == null)
                    throw new HttpRequestException("Artwork redirect limit exceeded.");
                uri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(uri, response.Headers.Location);
                continue;
            }
            if (policy == ProviderPolicy.TheAudioDb &&
                response.StatusCode == HttpStatusCode.TooManyRequests &&
                rateLimitRetries++ == 0)
            {
                TimeSpan delay = response.Headers.RetryAfter?.Delta ??
                                 TimeSpan.FromSeconds(2);
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                if (delay > TimeSpan.FromSeconds(5))
                    delay = TimeSpan.FromSeconds(5);
                await Task.Delay(delay, ct);
                redirect--;
                continue;
            }

            response.EnsureSuccessStatusCode();
            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength > maxBytes)
                throw new InvalidDataException("Artwork response exceeds the byte limit.");

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using var output = new MemoryStream(
                contentLength is > 0 and <= int.MaxValue ? (int)contentLength.Value : 0);
            var buffer = new byte[32 * 1024];
            int total = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0) break;
                total += read;
                if (total > maxBytes)
                    throw new InvalidDataException("Artwork response exceeds the byte limit.");
                output.Write(buffer, 0, read);
            }
            byte[] bytes = output.ToArray();
            if (maxBytes == MaxManifestBytes)
            {
                if (ManifestCache.Count >= 128)
                    foreach (string expiredKey in ManifestCache
                                 .Where(item => item.Value.ExpiresUtc <= DateTime.UtcNow)
                                 .Select(item => item.Key)
                                 .Take(32))
                        ManifestCache.TryRemove(expiredKey, out _);
                ManifestCache[cacheKey] = new ManifestCacheEntry(
                    (byte[])bytes.Clone(),
                    DateTime.UtcNow.AddHours(12));
            }
            return bytes;
        }
        throw new HttpRequestException("Artwork redirect limit exceeded.");
    }

    public byte[]? ResizeToJpeg(byte[] source, int maxSize, int quality = 92)
    {
        if (!TryReadImageDimensions(source, out int width, out int height) ||
            (long)width * height > MaxPixels)
            return null;
        BitmapSource? src = Decode(source);
        if (src == null) return null;
        int w = src.PixelWidth;
        int h = src.PixelHeight;

        if (Math.Max(w, h) <= maxSize)
        {
            if (source.Length >= 2 && source[0] == 0xFF && source[1] == 0xD8)
                return source;
            return EncodeJpeg(src, quality);
        }

        double factor = (double)maxSize / Math.Max(w, h);
        int destinationWidth = Math.Max(1, (int)Math.Round(w * factor));
        int destinationHeight = Math.Max(1, (int)Math.Round(h * factor));
        return EncodeJpeg(
            RiotResampler.Resize(src, destinationWidth, destinationHeight),
            quality);
    }

    internal static bool TryReadImageDimensions(
        ReadOnlySpan<byte> bytes,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length >= 24 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E &&
            bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A &&
            bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            uint pngWidth = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
            uint pngHeight = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
            if (pngWidth > int.MaxValue || pngHeight > int.MaxValue) return false;
            width = (int)pngWidth;
            height = (int)pngHeight;
            return width > 0 && height > 0;
        }

        if (bytes.Length >= 26 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            uint dibSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(14, 4));
            if (dibSize == 12)
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(18, 2));
                height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(20, 2));
                return width > 0 && height > 0;
            }
            if (dibSize < 40)
                return false;
            int bmpWidth = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(18, 4));
            int bmpHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(22, 4));
            long absoluteHeight = Math.Abs((long)bmpHeight);
            if (bmpWidth <= 0 || absoluteHeight <= 0 || absoluteHeight > int.MaxValue)
                return false;
            width = bmpWidth;
            height = (int)absoluteHeight;
            return true;
        }

        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return false;
        int offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF) { offset++; continue; }
            byte marker = bytes[offset + 1];
            offset += 2;
            if (marker is 0xD8 or 0xD9) continue;
            if (offset + 2 > bytes.Length) return false;
            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > bytes.Length) return false;
            if (IsStartOfFrame(marker) && segmentLength >= 7)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                return width > 0 && height > 0;
            }
            offset += segmentLength;
        }
        return false;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            BitmapFrame frame = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            return converted;
        }
        catch { return null; }
    }

    private static byte[] EncodeJpeg(BitmapSource source, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(
            new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string GetCaaThumbnail(JsonElement image, string fallback)
    {
        if (!image.TryGetProperty("thumbnails", out JsonElement thumbnails) ||
            thumbnails.ValueKind != JsonValueKind.Object)
            return fallback;
        foreach (string key in new[] { "500", "250", "large", "small" })
            if (thumbnails.TryGetProperty(key, out JsonElement value) &&
                !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString()!;
        return fallback;
    }

    private static string GetArtworkType(JsonElement image)
    {
        if (!image.TryGetProperty("types", out JsonElement types) ||
            types.ValueKind != JsonValueKind.Array)
            return "Other";
        string? value = types.EnumerateArray()
            .Select(type => type.GetString())
            .FirstOrDefault(type => !string.IsNullOrWhiteSpace(type));
        return value ?? "Other";
    }

    private static string DetectMimeType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50)
            return "image/png";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            return "image/bmp";
        return "image/jpeg";
    }

    private static bool HasType(JsonElement image, string expected)
    {
        if (!image.TryGetProperty("types", out JsonElement types) ||
            types.ValueKind != JsonValueKind.Array)
            return false;
        return types.EnumerateArray().Any(
            type => string.Equals(type.GetString(), expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryHttpsUri(string value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)) return false;
        if (parsed.Scheme == Uri.UriSchemeHttp)
        {
            var builder = new UriBuilder(parsed) { Scheme = Uri.UriSchemeHttps, Port = -1 };
            parsed = builder.Uri;
        }
        if (parsed.Scheme != Uri.UriSchemeHttps) return false;
        uri = parsed;
        return true;
    }

    private static void ValidateUri(Uri uri, ProviderPolicy policy)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort)
            throw new InvalidDataException("Artwork URL must use default-port HTTPS.");
        string host = uri.IdnHost;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(host, out IPAddress? address) && IsPrivate(address))
            throw new InvalidDataException("Artwork URL targets a local address.");

        bool allowed = policy switch
        {
            ProviderPolicy.MusicBrainz =>
                host.Equals("musicbrainz.org", StringComparison.OrdinalIgnoreCase),
            ProviderPolicy.CoverArtArchive =>
                host.Equals("coverartarchive.org", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("archive.org", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".archive.org", StringComparison.OrdinalIgnoreCase),
            ProviderPolicy.TheAudioDb =>
                host.Equals("www.theaudiodb.com", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("theaudiodb.com", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("r2.theaudiodb.com", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        if (!allowed)
            throw new InvalidDataException("Artwork URL redirected outside its provider.");
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168;
        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
               address.Equals(IPAddress.IPv6Loopback);
    }

    private static async Task ValidatePublicHostAsync(Uri uri, CancellationToken ct)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        if (addresses.Length == 0 || addresses.Any(IsPrivate))
            throw new InvalidDataException("Artwork host did not resolve to a public address.");
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Moved or HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static bool IsMusicBrainzSource(string source) =>
        (source ?? "").IndexOf("musicbrainz", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string NormalizeBarcode(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    public void Dispose() => _http.Dispose();

    private sealed record ReleaseIdentity(
        Guid ReleaseId,
        Guid? ReleaseGroupId,
        ArtworkMatchTier Tier,
        string Reason);

    private sealed record ManifestCacheEntry(byte[] Bytes, DateTime ExpiresUtc);

    private enum ProviderPolicy
    {
        MusicBrainz,
        CoverArtArchive,
        TheAudioDb,
        ExternalArtwork
    }
}
