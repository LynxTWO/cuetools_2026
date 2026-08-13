using System;
using System.Collections.Generic;
using System.IO;

namespace CUETools.Wpf.Services;

/// <summary>
/// Post-encode output validation shared by rip and convert: after finalization
/// returns, every path the engine declared must exist inside the staging
/// transaction and carry bytes. This catches a codec that returned success
/// after omitting or truncating a track, and rejects paths that escape the
/// transaction or cross a link/reparse point. Moved from RipService when
/// convert joined the shared app core; RipService delegates here.
/// </summary>
public static class EncodedOutputValidation
{
    public static void ValidateEncodedOutputs(string[]? paths, string stagingDirectory)
    {
        if (paths == null || paths.Length == 0)
            throw new InvalidDataException("The encoder reported no expected audio outputs.");

        string root = Path.GetFullPath(stagingDirectory);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(root) ||
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The encoder staging directory is not a regular directory.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("An expected encoded audio file is missing.");
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "An expected encoded audio path escaped the output transaction.");
            if (!seen.Add(full))
                throw new InvalidDataException(
                    "The encoder reported a duplicate audio output path.");
            RequireNoReparsePointAncestry(root,
                Path.GetDirectoryName(full) ??
                    throw new InvalidDataException(
                        "An expected encoded audio path has no parent directory."));
            if (!File.Exists(full))
                throw new InvalidDataException("An expected encoded audio file is missing.");
            FileAttributes attributes = File.GetAttributes(full);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException(
                    "An expected encoded audio output is not a regular file.");
            if (new FileInfo(full).Length <= 0)
                throw new InvalidDataException("An encoded audio file is empty.");
        }
    }

    private static void RequireNoReparsePointAncestry(string root, string targetDirectory)
    {
        string relative = Path.GetRelativePath(root, targetDirectory);
        string current = root;
        foreach (string part in relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "An expected encoded audio path crosses a link or reparse point.");
        }
    }
}
