using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CUETools.CTDB;
using CUETools.Processor;

namespace CUETools.Wpf.Services;

internal sealed class RepairEngineResult
{
    public required VerifyFilesResult Result { get; init; }
    public required string StagedCuePath { get; init; }
    public required IReadOnlyList<string> ExpectedAudioPaths { get; init; }
    public bool Applied { get; init; }
}

internal sealed class RepairVerificationResult
{
    public required VerifyFilesResult Result { get; init; }
    public bool Verified { get; init; }
}

internal interface IRepairEngine
{
    RepairEngineResult Repair(
        string sourcePath,
        string stagingDirectory,
        CUEConfig config,
        Action<double, string> onProgress);

    RepairVerificationResult Verify(
        string stagedCuePath,
        CUEConfig config,
        Action<double, string> onProgress);
}

/// <summary>
/// Adapts the legacy CUESheet repair script to an isolated output directory and exposes explicit
/// evidence that the script reached its CTDB-fix encode branch.
/// </summary>
internal sealed class CueRepairEngine : IRepairEngine
{
    private readonly IDiagnosticLog _log;

    public CueRepairEngine(IDiagnosticLog log)
    {
        _log = log;
    }

    public RepairEngineResult Repair(
        string sourcePath,
        string stagingDirectory,
        CUEConfig config,
        Action<double, string> onProgress)
    {
        var cue = new CUESheet(config);
        try
        {
            cue.CUEToolsProgress += (_, e) => onProgress(Clamp(e.percent), e.status);
            // A single candidate is selected internally. Choose deterministically only when the
            // engine presents multiple recoverable CTDB variants.
            cue.CUEToolsSelection += (_, e) => e.selection = 0;
            cue.Open(sourcePath);

            if (!config.scripts.TryGetValue("repair", out CUEToolsScript? repair))
                throw new InvalidOperationException("Repair script not available.");

            // Always create a portable external cue in staging. Preserve an image source as one
            // image; otherwise use the engine's established gaps-appended track layout.
            cue.OutputStyle = cue.HasSingleFilename ? CUEStyle.SingleFile : CUEStyle.GapsAppended;
            string sourceStem = Path.GetFileNameWithoutExtension(sourcePath);
            string stagedCue = Path.Combine(
                stagingDirectory,
                AlbumArtifactNames.CueFileName(sourceStem));
            cue.GenerateFilenames(AudioEncoderType.Lossless, "flac", stagedCue);
            string[] expectedAudio = (cue.DestPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
            if (expectedAudio.Length == 0)
                throw new InvalidOperationException(
                    "The repair engine did not generate an audio destination.");
            // Preserving source basenames can make different source extensions
            // converge on one .flac name. Reject that before ExecuteScript writes.
            RepairWorkspace.RequireDistinctAudioPaths(expectedAudio);
            RepairWorkspace.RequireSafeStagingDirectory(stagingDirectory);
            RepairWorkspace.RequireContainedPath(stagingDirectory, stagedCue, "repaired cue");
            foreach (string path in expectedAudio)
                RepairWorkspace.RequireContainedPath(stagingDirectory, path, "repaired audio");

            string status = cue.ExecuteScript(repair);
            bool applied = cue.IsUsingCUEToolsDBFix
                && cue.Processed
                && cue.Action == CUEAction.Encode;
            VerifyFilesResult result = VerifyService.Gather(
                cue, status, sourcePath, ok: true, error: "", applied, _log);

            return new RepairEngineResult
            {
                Result = result,
                StagedCuePath = stagedCue,
                ExpectedAudioPaths = expectedAudio,
                Applied = applied
            };
        }
        finally
        {
            TryClose(cue);
        }
    }

    public RepairVerificationResult Verify(
        string stagedCuePath,
        CUEConfig config,
        Action<double, string> onProgress)
    {
        var cue = new CUESheet(config);
        try
        {
            cue.CUEToolsProgress += (_, e) => onProgress(Clamp(e.percent), e.status);
            cue.Open(stagedCuePath);
            cue.UseCUEToolsDB("CUETools 2026", null, true, CTDBMetadataSearch.Fast);
            cue.UseAccurateRip();
            cue.Action = CUEAction.Verify;

            string status = cue.Go();
            VerifyFilesResult result = VerifyService.Gather(
                cue, status, stagedCuePath, ok: true, error: "", applied: false, _log);

            // This is a fresh decode of the staged files. Exact agreement with either database
            // proves the repaired copy. CTDB may contain other variants; those are not failure.
            bool verified = result.ArConfidence > 0 || result.CtdbConfidence > 0;
            return new RepairVerificationResult { Result = result, Verified = verified };
        }
        finally
        {
            TryClose(cue);
        }
    }

    private static double Clamp(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

    private void TryClose(CUESheet cue)
    {
        try { cue.Close(); }
        catch (Exception ex)
        {
            _log.Warn("verify", "could not close repair engine resources: " + ex.GetType().Name);
        }
    }
}

/// <summary>
/// Owns exactly one same-volume staging directory. Dispose removes only that directory; Publish
/// atomically renames it to a unique sibling and transfers ownership.
/// </summary>
internal sealed class RepairWorkspace : IDisposable
{
    internal const string OwnershipMarkerName = ".cuetools-repair-owner";

    private readonly string _parentDirectory;
    private readonly string _sourceName;
    private readonly string _ownerToken;
    private bool _ownsStaging;
    private string? _validatedCuePath;
    private string[]? _validatedAudioPaths;

    private RepairWorkspace(string parentDirectory, string sourceName, string stagingDirectory,
        string ownerToken)
    {
        _parentDirectory = parentDirectory;
        _sourceName = sourceName;
        StagingDirectory = stagingDirectory;
        _ownerToken = ownerToken;
        _ownsStaging = true;
    }

    public string StagingDirectory { get; }

    public static RepairWorkspace Create(string sourcePath)
    {
        string fullSource = Path.GetFullPath(sourcePath);
        string? parent = Path.GetDirectoryName(fullSource);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("The source parent directory does not exist.");

        string sourceName = Path.GetFileNameWithoutExtension(fullSource);
        if (string.IsNullOrWhiteSpace(sourceName))
            sourceName = "album";

        string staging;
        do
        {
            string stagingId = Guid.NewGuid().ToString("N");
            staging = Path.Combine(
                parent,
                ".cuetools-repair-" + stagingId + ".staging");
        }
        while (Directory.Exists(staging) || File.Exists(staging));

        Directory.CreateDirectory(staging);
        string ownerToken = Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(Path.Combine(staging, OwnershipMarkerName), ownerToken);
            return new RepairWorkspace(parent, sourceName, staging, ownerToken);
        }
        catch
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
            throw;
        }
    }

    internal static void RequireContainedPath(
        string stagingDirectory,
        string path,
        string description)
    {
        string relative = Path.GetRelativePath(
            Path.GetFullPath(stagingDirectory),
            Path.GetFullPath(path));
        if (relative == "."
            || Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The " + description + " escaped the repair staging directory.");
        }
    }

    internal static void RequireSafeStagingDirectory(string stagingDirectory)
    {
        if (!Directory.Exists(stagingDirectory))
            throw new DirectoryNotFoundException("The repair staging directory no longer exists.");
        if ((File.GetAttributes(stagingDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException(
                "Repair staging is a link or reparse point and cannot be used safely.");
    }

    public void RequireNonemptyOutputs(
        string stagedCuePath,
        IReadOnlyList<string> expectedAudioPaths)
    {
        RequireOwnership();
        if (expectedAudioPaths == null || expectedAudioPaths.Count == 0)
            throw new InvalidOperationException("The repair engine did not declare any audio output.");

        RequireDistinctAudioPaths(expectedAudioPaths);
        RequireNonemptyOwnedFile(stagedCuePath, "repaired cue");
        string[] audioPaths = expectedAudioPaths
            .ToArray();
        foreach (string path in audioPaths)
            RequireNonemptyOwnedFile(path, "repaired audio");
        _validatedCuePath = stagedCuePath;
        _validatedAudioPaths = audioPaths;
    }

    internal static void RequireDistinctAudioPaths(
        IReadOnlyList<string> expectedAudioPaths)
    {
        if (expectedAudioPaths == null)
            throw new ArgumentNullException(nameof(expectedAudioPaths));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in expectedAudioPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException(
                    "The repair engine generated an empty audio destination.");
            if (!seen.Add(Path.GetFullPath(path)))
                throw new InvalidOperationException(
                    "Source filenames collide after conversion to repaired FLAC names.");
        }
    }

    public string Publish()
    {
        if (_validatedCuePath == null || _validatedAudioPaths == null)
            throw new InvalidOperationException("Repair output was not validated before publication.");

        RequireOwnership();
        // Recheck after independent verification so a changed or swapped staging tree cannot be
        // published on the strength of an earlier validation.
        RequireNonemptyOutputs(_validatedCuePath, _validatedAudioPaths);
        RequireSafeStagingDirectory(StagingDirectory);
        if (ContainsReparsePoint())
            throw new InvalidOperationException(
                "Repair staging contains a link or reparse point and cannot be published safely.");

        for (int suffix = 1; suffix <= 1000; suffix++)
        {
            string name = suffix == 1
                ? _sourceName + " - repaired"
                : _sourceName + " - repaired (" + suffix + ")";
            string candidate = Path.Combine(_parentDirectory, name);

            if (Directory.Exists(candidate) || File.Exists(candidate))
                continue;

            try
            {
                Directory.Move(StagingDirectory, candidate);
                _ownsStaging = false;
                try { File.Delete(Path.Combine(candidate, OwnershipMarkerName)); }
                catch
                {
                    // Publication is already atomic and complete. A harmless hidden ownership
                    // marker must not turn committed success into a reported failure.
                }
                return candidate;
            }
            catch (IOException) when (Directory.Exists(candidate) || File.Exists(candidate))
            {
                // Another publisher won this candidate after our existence check.
            }
        }

        throw new IOException("Could not choose a unique repaired-copy directory.");
    }

    public void Dispose()
    {
        if (!_ownsStaging || !Directory.Exists(StagingDirectory))
            return;

        RequireOwnership();

        if (ContainsReparsePoint())
            throw new InvalidOperationException(
                "Repair staging contains a link or reparse point and cannot be deleted safely.");
        Directory.Delete(StagingDirectory, recursive: true);
        _ownsStaging = false;
    }

    private void RequireNonemptyOwnedFile(string path, string description)
    {
        string fullPath = Path.GetFullPath(path);
        if (!IsWithinStaging(fullPath, allowRoot: false))
            throw new InvalidOperationException(
                "The " + description + " escaped the repair staging directory.");
        if (!File.Exists(fullPath))
            throw new InvalidOperationException("The " + description + " was not produced.");
        RejectReparsePoints(fullPath);
        if (new FileInfo(fullPath).Length == 0)
            throw new InvalidOperationException("The " + description + " is empty.");
    }

    private bool IsWithinStaging(string path, bool allowRoot)
    {
        string relative = Path.GetRelativePath(StagingDirectory, Path.GetFullPath(path));
        return (allowRoot || relative != ".")
            && !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private bool IsOwnedStagingDirectory()
    {
        string fullStage = Path.GetFullPath(StagingDirectory);
        string? parent = Path.GetDirectoryName(fullStage);
        string name = Path.GetFileName(fullStage);
        return string.Equals(parent, Path.GetFullPath(_parentDirectory), StringComparison.OrdinalIgnoreCase)
            && name.StartsWith(".cuetools-repair-", StringComparison.Ordinal)
            && name.EndsWith(".staging", StringComparison.Ordinal);
    }

    private void RequireOwnership()
    {
        if (!IsOwnedStagingDirectory())
            throw new InvalidOperationException(
                "Refusing to use a repair directory outside its owner.");
        RequireSafeStagingDirectory(StagingDirectory);

        string marker = Path.Combine(StagingDirectory, OwnershipMarkerName);
        if (!File.Exists(marker)
            || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0
            || !string.Equals(File.ReadAllText(marker), _ownerToken,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repair staging ownership could not be proven.");
        }
    }

    private void RejectReparsePoints(string fullPath)
    {
        string current = fullPath;
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(
                    "Repair output contains a link or reparse point and cannot be published safely.");
            if (string.Equals(current, StagingDirectory, StringComparison.OrdinalIgnoreCase))
                return;
            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException("Repair output path has no staging parent.");
        }
    }

    private bool ContainsReparsePoint()
    {
        try
        {
            var pending = new Stack<string>();
            pending.Push(StagingDirectory);
            while (pending.Count != 0)
            {
                string current = pending.Pop();
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return true;
                foreach (string entry in Directory.EnumerateFileSystemEntries(
                    current, "*", SearchOption.TopDirectoryOnly))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        return true;
                    if ((attributes & FileAttributes.Directory) != 0)
                        pending.Push(entry);
                }
            }
            return false;
        }
        catch
        {
            // A concurrently replaced or unreadable tree is not safe to publish or recursively delete.
            return true;
        }
    }
}
