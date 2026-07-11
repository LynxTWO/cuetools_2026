using System;
using System.IO;
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
    Task<AlbumArt?> FindHiRes(string artist, string album, CancellationToken ct = default);

    /// <summary>Find hi-res art and re-encode it as JPEG downscaled so its longest side is at most
    /// <paramref name="maxSize"/> px (RIOT-matched). Returns null if nothing is found.</summary>
    Task<byte[]?> FindResizedJpeg(string artist, string album, int maxSize, int quality = 90, CancellationToken ct = default);
}

public sealed class AlbumArtService : IAlbumArtService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Regex SizeToken = new(@"\d+x\d+bb", RegexOptions.Compiled);

    public async Task<AlbumArt?> FindHiRes(string artist, string album, CancellationToken ct = default)
    {
        string term = Uri.EscapeDataString($"{artist} {album}".Trim());
        if (term.Length == 0) return null;
        string search = $"https://itunes.apple.com/search?term={term}&entity=album&limit=5";

        string json;
        try { json = await Http.GetStringAsync(search, ct); }
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

    public async Task<byte[]?> FindResizedJpeg(string artist, string album, int maxSize, int quality = 90, CancellationToken ct = default)
    {
        var art = await FindHiRes(artist, album, ct);
        if (art == null) return null;

        var src = Decode(art.Bytes);
        if (src == null) return null;
        int w = src.PixelWidth, h = src.PixelHeight;

        BitmapSource result;
        if (Math.Max(w, h) <= maxSize)
        {
            result = src; // already small enough - just re-encode
        }
        else
        {
            double f = (double)maxSize / Math.Max(w, h);
            int dw = Math.Max(1, (int)Math.Round(w * f));
            int dh = Math.Max(1, (int)Math.Round(h * f));
            result = RiotResampler.Resize(src, dw, dh);
        }
        return EncodeJpeg(result, quality);
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
