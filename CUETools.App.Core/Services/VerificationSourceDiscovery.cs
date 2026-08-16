using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CUETools.Processor;

namespace CUETools.Wpf.Services;

public enum VerificationSourceKind
{
    CueSheet,
    Playlist,
    AudioFile
}

public sealed class VerificationDiscSource
{
    public required string Path { get; init; }
    public required string RelativePath { get; init; }
    public required string DisplayName { get; init; }
    public VerificationSourceKind Kind { get; init; }
    public int DiscNumber { get; init; }
    public int TotalDiscs { get; init; }
}

public sealed class VerificationSourceSet
{
    public required string RootPath { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<VerificationDiscSource> Discs { get; init; }
}

public sealed class VerificationSourceDiscoveryResult
{
    public VerificationSourceSet? SourceSet { get; init; }
    public string Error { get; init; } = "";
    public bool Ok => SourceSet != null;
}

public interface IVerificationSourceDiscovery
{
    VerificationSourceDiscoveryResult Discover(IEnumerable<string> paths);
}

/// <summary>
/// Resolves a user-selected file or album folder into independent verification units.
/// Discovery is deliberately conservative: manifests may describe separate discs, but
/// overlapping manifests are rejected because choosing one would silently verify audio twice.
/// </summary>
public sealed class VerificationSourceDiscovery : IVerificationSourceDiscovery
{
    private const int MaximumDepth = 8;
    private const int MaximumCandidates = 2048;
    private const int MaximumDiscs = 100;

    private static readonly HashSet<string> BaselineAudioExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".flac", ".wv", ".ape", ".tak", ".m4a", ".tta", ".wav", ".ofr"
    };

    private readonly CUEConfig? _config;

    public VerificationSourceDiscovery()
    {
    }

    public VerificationSourceDiscovery(CUEConfig config)
    {
        _config = config;
    }

    private static readonly Regex CueFileLine = new(
        "^\\s*FILE\\s+(?:\"(?<path>[^\"]+)\"|(?<path>\\S+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CueDiscNumber = new(
        "^\\s*REM\\s+(?:DISCNUMBER|DISC)\\s+\"?(?<number>[0-9]{1,3})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CueTotalDiscs = new(
        "^\\s*REM\\s+(?:TOTALDISCS|DISCTOTAL)\\s+\"?(?<number>[0-9]{1,3})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PathDiscNumber = new(
        "(?:^|[\\s._-])(?:cd|disc|disk)[\\s._-]*0*(?<number>[0-9]{1,3})(?:$|[\\s._-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public VerificationSourceDiscoveryResult Discover(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string[] selected;
        try
        {
            selected = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure("The dropped selection contains an invalid path.");
        }

        if (selected.Length == 0)
            return Failure("Drop one album folder, CUE sheet, playlist, or supported lossless file.");
        if (selected.Length > MaximumCandidates)
            return Failure(
                $"The selection contains more than {MaximumCandidates} files. " +
                "Choose an album folder or a smaller manifest set.");

        bool[] directories = selected.Select(Directory.Exists).ToArray();
        bool[] files = selected.Select(File.Exists).ToArray();
        if (Enumerable.Range(0, selected.Length).Any(index =>
            !files[index] && !directories[index]))
            return Failure("One or more dropped files no longer exist.");
        if (directories.Any(value => value) && (selected.Length != 1 || files.Any(value => value)))
            return Failure("Drop one album folder at a time, or select manifest files from one album.");

        if (selected.Select(Path.GetPathRoot).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            return Failure("Selected files must belong to the same album location.");

        if (directories[0])
            return DiscoverDirectory(selected[0]);

        string root = CommonDirectory(selected);
        return Build(root, selected);
    }

    private VerificationSourceDiscoveryResult DiscoverDirectory(string root)
    {
        var files = new List<string>();
        var pending = new Stack<(DirectoryInfo Directory, int Depth)>();
        pending.Push((new DirectoryInfo(root), 0));

        try
        {
            while (pending.Count > 0)
            {
                (DirectoryInfo directory, int depth) = pending.Pop();
                foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
                {
                    FileAttributes attributes;
                    try { attributes = entry.Attributes; }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }

                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        // The selected root may itself be a junction, but discovery never escapes
                        // through a child link into another tree.
                        if (depth < MaximumDepth &&
                            (attributes & FileAttributes.ReparsePoint) == 0)
                            pending.Push(((DirectoryInfo)entry, depth + 1));
                        continue;
                    }

                    // Do not let a child file link escape the selected album tree. A link that
                    // the user selects directly remains an explicit source and is handled above.
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    if (!IsCandidate(entry.Extension))
                        continue;
                    files.Add(entry.FullName);
                    if (files.Count > MaximumCandidates)
                        return Failure(
                            $"The folder contains more than {MaximumCandidates} candidate files. " +
                            "Choose a disc folder or a specific CUE sheet instead.");
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Failure("CUETools could not read the selected folder.");
        }
        catch (IOException)
        {
            return Failure("The selected folder changed while CUETools was scanning it.");
        }

        return Build(root, files);
    }

    private VerificationSourceDiscoveryResult Build(string root, IReadOnlyCollection<string> files)
    {
        string[] cues = files.Where(path => Extension(path) == ".cue").ToArray();
        if (cues.Length > 0)
            return BuildManifestSet(root, cues, VerificationSourceKind.CueSheet);

        string[] playlists = files.Where(path =>
            Extension(path) is ".m3u" or ".m3u8").ToArray();
        if (playlists.Length > 0)
            return BuildManifestSet(root, playlists, VerificationSourceKind.Playlist);

        string[] audio = files.Where(path => IsAudioExtension(Extension(path))).ToArray();
        if (audio.Length == 0)
            return Failure("No CUE sheet, playlist, or supported lossless audio was found.");
        if (audio.Length > 1)
            return Failure(
                $"Found {audio.Length} audio files but no CUE sheet or playlist. " +
                "Choose the album CUE/M3U so track order and disc boundaries are explicit.");

        string path = audio[0];
        var source = new VerificationDiscSource
        {
            Path = path,
            RelativePath = Relative(root, path),
            DisplayName = Path.GetFileNameWithoutExtension(path),
            Kind = VerificationSourceKind.AudioFile,
            DiscNumber = 1,
            TotalDiscs = 1
        };
        return Success(root, new[] { source });
    }

    private static VerificationSourceDiscoveryResult BuildManifestSet(
        string root,
        IReadOnlyCollection<string> paths,
        VerificationSourceKind kind)
    {
        if (paths.Count > MaximumDiscs)
            return Failure(
                $"Found more than {MaximumDiscs} disc manifests. " +
                "Choose the intended album folder or manifest files instead.");

        var manifests = new List<ManifestInfo>();
        foreach (string path in paths)
        {
            ManifestInfo info;
            try { info = ReadManifest(path, kind); }
            catch (IOException)
            {
                return Failure("A selected manifest changed while CUETools was reading it.");
            }
            catch (UnauthorizedAccessException)
            {
                return Failure("CUETools could not read one of the selected manifests.");
            }
            manifests.Add(info);
        }

        for (int left = 0; left < manifests.Count; left++)
        {
            for (int right = left + 1; right < manifests.Count; right++)
            {
                if (!manifests[left].References.Overlaps(manifests[right].References))
                    continue;
                return Failure(
                    "Two manifests reference the same audio (" +
                    Path.GetFileName(manifests[left].Path) + " and " +
                    Path.GetFileName(manifests[right].Path) +
                    "). Choose the intended CUE/playlist so CUETools does not have to guess.");
            }
        }

        if (manifests.Count > 1)
        {
            int[] discNumbers = manifests
                .Select(info => info.DiscNumber > 0
                    ? info.DiscNumber
                    : InferDiscNumber(info.Path))
                .OrderBy(number => number)
                .ToArray();
            if (discNumbers.Any(number => number <= 0) ||
                !discNumbers.SequenceEqual(Enumerable.Range(1, manifests.Count)))
                return Failure(
                    "Multiple manifests were found, but their disc numbers are missing, " +
                    "duplicated, or incomplete. Name/tag them as Disc 1, Disc 2, and so on " +
                    "so CUETools does not guess album order.");
            if (manifests.Any(info => info.TotalDiscs > manifests.Count))
                return Failure(
                    "The manifests describe more discs than were found. Choose the complete " +
                    "album folder, or verify one disc by itself.");
        }

        int discoveredTotal = manifests.Count;
        VerificationDiscSource[] sources = manifests
            .Select(info =>
            {
                int discNumber = info.DiscNumber > 0
                    ? info.DiscNumber
                    : InferDiscNumber(info.Path);
                int totalDiscs = Math.Max(info.TotalDiscs, discoveredTotal);
                return new VerificationDiscSource
                {
                    Path = info.Path,
                    RelativePath = Relative(root, info.Path),
                    DisplayName = DisplayName(info.Path, discNumber, discoveredTotal),
                    Kind = kind,
                    DiscNumber = discNumber,
                    TotalDiscs = totalDiscs
                };
            })
            .OrderBy(source => source.DiscNumber <= 0 ? int.MaxValue : source.DiscNumber)
            .ThenBy(source => source.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Success(root, sources);
    }

    private static ManifestInfo ReadManifest(string path, VerificationSourceKind kind)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int discNumber = 0;
        int totalDiscs = 0;
        foreach (string raw in File.ReadLines(path).Take(4096))
        {
            string line = raw.Trim();
            if (kind == VerificationSourceKind.CueSheet)
            {
                Match file = CueFileLine.Match(raw);
                if (file.Success)
                    AddReference(path, file.Groups["path"].Value, references);
                if (discNumber == 0)
                    discNumber = MatchNumber(CueDiscNumber, raw);
                if (totalDiscs == 0)
                    totalDiscs = MatchNumber(CueTotalDiscs, raw);
            }
            else if (line.Length > 0 && line[0] != '#')
            {
                AddReference(path, line.Trim('"'), references);
            }
        }

        // A reference-free manifest still has its own identity. It cannot silently collapse
        // into every other malformed manifest during overlap detection.
        if (references.Count == 0)
            references.Add(Path.GetFullPath(path));
        return new ManifestInfo(path, references, discNumber, totalDiscs);
    }

    private static void AddReference(
        string manifest,
        string reference,
        ISet<string> references)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return;
        try
        {
            bool isAbsoluteUri = Uri.TryCreate(reference, UriKind.Absolute, out Uri? uri);
            if (!Path.IsPathRooted(reference) && isAbsoluteUri && uri?.IsFile != true)
                return;
            string resolved = uri?.IsFile == true
                ? Path.GetFullPath(uri.LocalPath)
                : Path.IsPathRooted(reference)
                    ? Path.GetFullPath(reference)
                    : Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(manifest) ?? "",
                        reference));
            references.Add(resolved);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // The engine owns final manifest parsing and will report the exact malformed line.
            // Discovery simply omits an unusable reference from overlap decisions.
        }
    }

    private static int MatchNumber(Regex regex, string value)
    {
        Match match = regex.Match(value);
        return match.Success && int.TryParse(match.Groups["number"].Value, out int number)
            ? number
            : 0;
    }

    private static int InferDiscNumber(string path)
    {
        string? current = Path.GetFileNameWithoutExtension(path);
        string? directory = Path.GetDirectoryName(path);
        for (int attempt = 0; attempt < 3 && !string.IsNullOrEmpty(current); attempt++)
        {
            Match match = PathDiscNumber.Match(current);
            if (match.Success && int.TryParse(match.Groups["number"].Value, out int number))
                return number;
            current = string.IsNullOrEmpty(directory)
                ? null
                : Path.GetFileName(directory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
            directory = string.IsNullOrEmpty(directory) ? null : Path.GetDirectoryName(directory);
        }
        return 0;
    }

    private static string DisplayName(string path, int discNumber, int count)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        if (count <= 1)
            return stem;
        return discNumber > 0 ? $"Disc {discNumber}: {stem}" : stem;
    }

    private static VerificationSourceDiscoveryResult Success(
        string root,
        IReadOnlyList<VerificationDiscSource> sources)
    {
        string trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string display = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(display))
            display = trimmed;
        return new VerificationSourceDiscoveryResult
        {
            SourceSet = new VerificationSourceSet
            {
                RootPath = root,
                DisplayName = display,
                Discs = new ReadOnlyCollection<VerificationDiscSource>(sources.ToArray())
            }
        };
    }

    private static VerificationSourceDiscoveryResult Failure(string error)
        => new() { Error = error };

    private bool IsCandidate(string extension)
        => extension.Equals(".cue", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".m3u", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase) ||
           IsAudioExtension(extension);

    private bool IsAudioExtension(string extension)
    {
        if (BaselineAudioExtensions.Contains(extension))
            return true;
        if (_config == null || extension.Length == 0 || extension[0] != '.')
            return false;

        return _config.formats.TryGetValue(extension[1..], out var format) &&
            format.allowLossless &&
            format.decoder?.IsValid == true;
    }

    private static string Extension(string path) => Path.GetExtension(path).ToLowerInvariant();

    private static string Relative(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative == "." ? Path.GetFileName(path) : relative;
    }

    private static string CommonDirectory(IReadOnlyList<string> files)
    {
        string common = Path.GetDirectoryName(files[0]) ?? Directory.GetCurrentDirectory();
        foreach (string file in files.Skip(1))
        {
            string candidate = Path.GetDirectoryName(file) ?? common;
            while (!candidate.StartsWith(
                common.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) &&
                !candidate.Equals(common, StringComparison.OrdinalIgnoreCase))
            {
                string? parent = Path.GetDirectoryName(common);
                if (string.IsNullOrEmpty(parent) || parent == common)
                    return Path.GetPathRoot(files[0]) ?? common;
                common = parent;
            }
        }
        return common;
    }

    private sealed record ManifestInfo(
        string Path,
        HashSet<string> References,
        int DiscNumber,
        int TotalDiscs);
}
