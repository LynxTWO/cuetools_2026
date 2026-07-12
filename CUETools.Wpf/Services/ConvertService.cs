using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CUETools.Codecs;
using CUETools.Processor;

namespace CUETools.Wpf.Services;

public sealed class ConvertResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public string Status { get; init; } = "";
    public string OutputDir { get; init; } = "";
    public int FileCount { get; init; }
}

/// <summary>A short real snippet of the source audio (decoded to mono windows) plus the detected
/// source format, so the convert round-trip scope shows the real audio and the true source codec.</summary>
public sealed class SourcePreview
{
    public string SourceFormat { get; init; } = "";
    public IReadOnlyList<float[]> Windows { get; init; } = Array.Empty<float[]>();
}

/// <summary>Transcode an existing rip (a .cue, an album folder, or a file with an embedded cue)
/// to another lossless format and layout - the file-source twin of the disc encode. Blocking, so
/// callers marshal it onto a background thread.</summary>
public interface IConvertService
{
    /// <summary>Lossless output formats that have a working encoder in this build (e.g. flac, wav).
    /// Data-driven so a dropped-in codec plugin extends the list without code changes.</summary>
    IReadOnlyList<string> LosslessFormats();

    ConvertResult Convert(string inputPath, string format, string outputDir, Action<double, string> onProgress);

    /// <summary>Decode a short real snippet of the source into mono sample windows (for the
    /// round-trip scope) and detect the source format. Best-effort; never throws.</summary>
    SourcePreview PreloadSource(string inputPath);
}

public sealed class ConvertService : IConvertService
{
    private readonly CUEConfig _config;

    public ConvertService(CUEConfig config) => _config = config;

    public IReadOnlyList<string> LosslessFormats()
    {
        try
        {
            return _config.formats
                .Where(f => f.Value.allowLossless && f.Value.encoderLossless != null)
                .Select(f => f.Key)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return new List<string> { "flac", "wav" }; }
    }

    public ConvertResult Convert(string inputPath, string format, string outputDir, Action<double, string> onProgress)
    {
        try
        {
            var cue = new CUESheet(_config);
            cue.CUEToolsProgress += (s, e) => onProgress(Clamp(e.percent), e.status);
            cue.Open(inputPath);

            string artist = Safe(cue.Metadata?.Artist ?? "");
            string title = Safe(cue.Metadata?.Title ?? "");
            string album = (artist.Length == 0 && title.Length == 0)
                ? Path.GetFileNameWithoutExtension(inputPath)
                : $"{artist} - {title}";

            string baseDir = string.IsNullOrWhiteSpace(outputDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CUETools")
                : outputDir;
            string outDir = Path.Combine(baseDir, album);
            Directory.CreateDirectory(outDir);

            cue.Action = CUEAction.Encode;
            cue.OutputStyle = CUEStyle.GapsAppended;
            cue.GenerateFilenames(AudioEncoderType.Lossless, format, Path.Combine(outDir, "album.cue"));

            onProgress(0, $"Converting to {format}...");
            string status = cue.Go();
            onProgress(1, status);

            int files = 0;
            try { files = Directory.GetFiles(outDir, "*." + format).Length; } catch { }

            return new ConvertResult { Ok = true, Status = status, OutputDir = outDir, FileCount = files };
        }
        catch (Exception ex)
        {
            return new ConvertResult { Error = ex.Message };
        }
    }

    private static readonly string[] AudioExts = { ".flac", ".wav", ".ape", ".wv", ".tta", ".tak", ".m4a", ".ofr" };

    public SourcePreview PreloadSource(string inputPath)
    {
        try
        {
            string? audio = ResolveAudioFile(inputPath);
            if (audio == null) return new SourcePreview();
            string fmt = Path.GetExtension(audio).TrimStart('.').ToLowerInvariant();

            IAudioSource src = AudioReadWrite.GetAudioSource(audio, null, _config);
            try
            {
                var buf = new AudioBuffer(src.PCM, 320);
                int ch = src.PCM.ChannelCount;
                double full = 1 << (src.PCM.BitsPerSample - 1);
                var windows = new List<float[]>();
                int guard = 0;
                while (windows.Count < 240 && guard++ < 4000)      // ~1.7s of real source audio
                {
                    int n = src.Read(buf, 320);
                    if (n <= 0) break;
                    int[,] s = buf.Samples;
                    var win = new float[n];
                    for (int i = 0; i < n; i++)
                        win[i] = ch >= 2 ? (float)((s[i, 0] + s[i, 1]) * 0.5 / full) : (float)(s[i, 0] / full);
                    windows.Add(win);
                }
                return new SourcePreview { SourceFormat = fmt, Windows = windows };
            }
            finally { try { src.Close(); } catch { } }
        }
        catch { return new SourcePreview(); }   // scope falls back to structure-only
    }

    // Resolve the input (a file, a .cue, or a folder) to a concrete audio file to decode.
    private static string? ResolveAudioFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (Array.IndexOf(AudioExts, ext) >= 0) return path;
                return LargestAudio(Path.GetDirectoryName(path));   // .cue / .m3u -> its folder
            }
            if (Directory.Exists(path)) return LargestAudio(path);
        }
        catch { }
        return null;
    }

    private static string? LargestAudio(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        string? best = null; long bestLen = -1;
        foreach (var f in Directory.GetFiles(dir))
        {
            if (Array.IndexOf(AudioExts, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
            long len = new FileInfo(f).Length;
            if (len > bestLen) { bestLen = len; best = f; }
        }
        return best;
    }

    private string Safe(string s) => string.IsNullOrEmpty(s) ? "" : _config.CleanseString(s);
    private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
