using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CUETools.Wpf.Imaging;

namespace CUETools.Wpf.Services;

public sealed record AlbumArt(string Url, byte[] Bytes, int Width, int Height, string Album, string Artist);

/// <summary>
/// Finds album art from Apple's public iTunes Search API (the same source the bendodson.com
/// artwork finder uses) and downscales it to the target size with the RIOT-matching
/// Mitchell-Netravali resampler. The iTunes result gives a 100x100 URL; swapping the size token
/// for a huge one makes Apple return the largest master it has (often 1400-3000 px).
/// </summary>
public interface IAlbumArtService
{
    /// <summary>Find the largest cover Apple has. When <paramref name="barcode"/> (the disc's UPC/EAN)
    /// is given, look up that EXACT release first; otherwise (or if the UPC is unknown to Apple) fall
    /// back to an artist+album text search.</summary>
    Task<AlbumArt?> FindHiRes(string artist, string album, string? barcode = null, CancellationToken ct = default);

    /// <summary>Find hi-res art and re-encode it as JPEG downscaled so its longest side is at most
    /// <paramref name="maxSize"/> px (RIOT-matched). Returns null if nothing is found.</summary>
    Task<byte[]?> FindResizedJpeg(string artist, string album, int maxSize, int quality = 95, string? barcode = null, CancellationToken ct = default);

    /// <summary>Downscale already-fetched image bytes to a JPEG whose longest side is at most
    /// <paramref name="maxSize"/> px, using the RIOT-matching resampler. No network - lets a caller
    /// that already fetched a master (for a preview) produce the embed copy without fetching twice.
    /// Never upscales; a JPEG source that already fits is returned byte-for-byte (zero loss).</summary>
    byte[]? ResizeToJpeg(byte[] source, int maxSize, int quality = 95);
}

public sealed class AlbumArtService : IAlbumArtService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Regex SizeToken = new(@"\d+x\d+bb", RegexOptions.Compiled);

    public async Task<AlbumArt?> FindHiRes(string artist, string album, string? barcode = null, CancellationToken ct = default)
    {
        // UPC-exact first: the disc's barcode (MCN / release UPC) picks the exact Apple release, so the
        // cover is guaranteed to match this pressing rather than a same-title search hit.
        string upc = new string((barcode ?? "").Where(char.IsDigit).ToArray());
        if (upc.Length >= 8)
        {
            var exact = await LookupAsync($"https://itunes.apple.com/lookup?upc={upc}&entity=album&limit=1", album, artist, ct);
            if (exact != null) return exact;
        }

        string term = Uri.EscapeDataString($"{artist} {album}".Trim());
        if (term.Length == 0) return null;
        return await LookupAsync($"https://itunes.apple.com/search?term={term}&entity=album&limit=5", album, artist, ct);
    }

    // Run one iTunes query (search or upc lookup), take the top album result, and pull its largest
    // master. Shared by both the UPC-exact and text-search paths.
    private async Task<AlbumArt?> LookupAsync(string url, string album, string artist, CancellationToken ct)
    {
        string json;
        try { json = await Http.GetStringAsync(url, ct); }
        catch { return null; }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;

        var top = results[0];
        string url100 = top.TryGetProperty("artworkUrl100", out var a) ? a.GetString() ?? "" : "";
        if (url100.Length == 0) return null;

        // Ask for a 3000px master; Apple returns the real master capped to its true size. (An
        // arbitrarily huge size like 100000x100000bb is rejected 400 on older catalog URLs, so
        // 3000 is the reliable "give me the biggest you have up to 3000px" request.)
        string hi = SizeToken.Replace(url100, "3000x3000bb");
        byte[] bytes;
        try { bytes = await Http.GetByteArrayAsync(hi, ct); }
        catch { return null; }

        var frame = Decode(bytes);
        return new AlbumArt(hi, bytes, frame?.PixelWidth ?? 0, frame?.PixelHeight ?? 0,
            top.TryGetProperty("collectionName", out var cn) ? cn.GetString() ?? album : album,
            top.TryGetProperty("artistName", out var an) ? an.GetString() ?? artist : artist);
    }

    public async Task<byte[]?> FindResizedJpeg(string artist, string album, int maxSize, int quality = 95, string? barcode = null, CancellationToken ct = default)
    {
        var art = await FindHiRes(artist, album, barcode, ct);
        return art == null ? null : ResizeToJpeg(art.Bytes, maxSize, quality);
    }

    public byte[]? ResizeToJpeg(byte[] source, int maxSize, int quality = 95)
    {
        var src = Decode(source);
        if (src == null) return null;
        int w = src.PixelWidth, h = src.PixelHeight;

        // NEVER upscale: if the master already fits within maxSize, keep its own pixels.
        if (Math.Max(w, h) <= maxSize)
        {
            // a JPEG that needs no resize passes through byte-for-byte - re-encoding an unchanged
            // JPEG only adds generational loss. (FFD8 = JPEG SOI marker.)
            if (source.Length >= 2 && source[0] == 0xFF && source[1] == 0xD8)
                return source;
            // PNG or other source: one encode at the visually-lossless quality
            return EncodeJpeg(src, quality);
        }

        double f = (double)maxSize / Math.Max(w, h);
        int dw = Math.Max(1, (int)Math.Round(w * f));
        int dh = Math.Max(1, (int)Math.Round(h * f));
        // Mitchell-Netravali downscale (the RIOT-matched resampler), then a quality-95 encode:
        // at q95 on a freshly downscaled image, JPEG block/ringing artifacts sit below visual
        // threshold at 1:1 viewing - the compression must never visibly change the scaled result.
        return EncodeJpeg(RiotResampler.Resize(src, dw, dh), quality);
    }

    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var f = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return new FormatConvertedBitmap(f, PixelFormats.Bgra32, null, 0);
        }
        catch { return null; }
    }

    private static byte[] EncodeJpeg(BitmapSource src, int quality)
    {
        var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        // JPEG has no alpha; drop it to avoid a black/!-channel surprise
        enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
