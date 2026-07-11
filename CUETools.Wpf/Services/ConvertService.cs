using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

/// <summary>Transcode an existing rip (a .cue, an album folder, or a file with an embedded cue)
/// to another lossless format and layout - the file-source twin of the disc encode. Blocking, so
/// callers marshal it onto a background thread.</summary>
public interface IConvertService
{
    /// <summary>Lossless output formats that have a working encoder in this build (e.g. flac, wav).
    /// Data-driven so a dropped-in codec plugin extends the list without code changes.</summary>
    IReadOnlyList<string> LosslessFormats();

    ConvertResult Convert(string inputPath, string format, string outputDir, Action<double, string> onProgress);
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

    private string Safe(string s) => string.IsNullOrEmpty(s) ? "" : _config.CleanseString(s);
    private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
