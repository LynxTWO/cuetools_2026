using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CUETools.Wpf.Services;

/// <summary>
/// Owns one Test &amp; Copy temporary tree and keeps a process-held lease open while a held read can
/// still be accepted or discarded. Cleanup trusts the exact directory shape, ownership marker,
/// lease state, containment, and a reparse-free tree. A name or old timestamp alone never authorizes
/// recursive deletion.
/// </summary>
internal sealed class TestCopyStagingWorkspace : IDisposable
{
    internal const string DirectoryPrefix = ".cuetools-testcopy-";
    internal const string OwnershipMarkerName = ".cuetools-testcopy-owner";
    internal const string LeaseFileName = ".cuetools-testcopy-lease";
    internal const string OwnershipMagic = "CUETOOLS_TESTCOPY_STAGE_V1";
    internal const string LeaseMagic = "CUETOOLS_TESTCOPY_LEASE_V1";

    private readonly string _rootDirectory;
    private readonly string _ownerToken;
    private FileStream? _lease;
    private bool _preserve;
    private bool _disposed;

    private TestCopyStagingWorkspace(string rootDirectory, string workspaceDirectory,
        string ownerToken, FileStream lease)
    {
        _rootDirectory = rootDirectory;
        WorkspaceDirectory = workspaceDirectory;
        _ownerToken = ownerToken;
        _lease = lease;
        CopyBaseDirectory = Path.Combine(workspaceDirectory, "copy");
        ThirdBaseDirectory = Path.Combine(workspaceDirectory, "third");
    }

    internal static string DefaultRootDirectory =>
        Path.Combine(Path.GetTempPath(), "cuetc");

    internal string WorkspaceDirectory { get; }
    internal string CopyBaseDirectory { get; }
    internal string ThirdBaseDirectory { get; }

    internal static TestCopyStagingWorkspace Create(string? rootDirectory = null)
    {
        string root = Path.GetFullPath(rootDirectory ?? DefaultRootDirectory);
        Directory.CreateDirectory(root);
        RequirePlainDirectory(root, "The Test & Copy staging root");

        string ownerToken = Guid.NewGuid().ToString("N");
        string workspace = Path.Combine(root, DirectoryPrefix + ownerToken);
        Directory.CreateDirectory(workspace);
        try
        {
            RequireDirectChild(root, workspace);
            RequirePlainDirectory(workspace, "The Test & Copy workspace");
            WriteDurableText(Path.Combine(workspace, OwnershipMarkerName),
                OwnershipText(ownerToken));
            Directory.CreateDirectory(Path.Combine(workspace, "copy"));
            Directory.CreateDirectory(Path.Combine(workspace, "third"));

            string leasePath = Path.Combine(workspace, LeaseFileName);
            var lease = new FileStream(leasePath, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.DeleteOnClose);
            try
            {
                using var writer = new StreamWriter(lease, new UTF8Encoding(false), 1024,
                    leaveOpen: true);
                writer.WriteLine(LeaseMagic);
                writer.WriteLine(ownerToken);
                writer.Flush();
                lease.Flush(true);
                lease.Position = 0;
                return new TestCopyStagingWorkspace(root, workspace, ownerToken, lease);
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }
        catch
        {
            // Cleanup still goes through the ownership validator. If marker creation failed, leave
            // the empty lookalike alone instead of authorizing a recursive delete without proof.
            TryDeleteOwnedWorkspace(root, workspace, ownerToken, out _);
            throw;
        }
    }

    /// <summary>
    /// Close the live lease but retain the owned workspace for manual recovery after a failed final
    /// copy. The normal startup sweep can reclaim it after the age threshold.
    /// </summary>
    internal void PreserveForRecovery()
    {
        if (_disposed) return;
        _preserve = true;
        CloseLease();
    }

    internal void DeleteOwned()
    {
        if (_disposed) return;
        _disposed = true;
        CloseLease();
        if (!_preserve)
            TryDeleteOwnedWorkspace(_rootDirectory, WorkspaceDirectory, _ownerToken, out _);
    }

    public void Dispose() => DeleteOwned();

    /// <summary>
    /// Delete a caller-supplied workspace only when its parent, exact name, marker token, lease
    /// state, and complete tree prove it is one of ours.
    /// </summary>
    internal static bool TryDeleteOwnedWorkspace(string workspaceDirectory)
    {
        if (string.IsNullOrWhiteSpace(workspaceDirectory))
            return false;
        string workspace;
        try { workspace = Path.GetFullPath(workspaceDirectory); }
        catch { return false; }
        string? root = Path.GetDirectoryName(workspace);
        return !string.IsNullOrEmpty(root)
            && TryReadOwnership(root, workspace, out string token)
            && TryDeleteOwnedWorkspace(root, workspace, token, out _);
    }

    internal readonly struct SweepResult
    {
        internal SweepResult(int directories, long bytes)
        {
            Directories = directories;
            Bytes = bytes;
        }

        internal int Directories { get; }
        internal long Bytes { get; }
    }

    /// <summary>
    /// Reclaim marker-proven orphan workspaces older than <paramref name="minimumAge"/>. A live
    /// lease wins even when every timestamp has been forged old.
    /// </summary>
    internal static SweepResult SweepOrphans(string? rootDirectory, TimeSpan minimumAge,
        DateTime? utcNow = null)
    {
        if (minimumAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumAge));

        string root;
        try
        {
            root = Path.GetFullPath(rootDirectory ?? DefaultRootDirectory);
            if (!Directory.Exists(root))
                return new SweepResult(0, 0);
            RequirePlainDirectory(root, "The Test & Copy staging root");
        }
        catch
        {
            return new SweepResult(0, 0);
        }

        string[] candidates;
        try
        {
            candidates = Directory.GetDirectories(root, DirectoryPrefix + "*",
                SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return new SweepResult(0, 0);
        }

        DateTime cutoff = (utcNow ?? DateTime.UtcNow) - minimumAge;
        int deleted = 0;
        long bytes = 0;
        foreach (string candidate in candidates)
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(candidate) > cutoff)
                    continue;
                if (!TryReadOwnership(root, candidate, out string token))
                    continue;
                if (TryDeleteOwnedWorkspace(root, candidate, token, out long removedBytes))
                {
                    deleted++;
                    bytes += removedBytes;
                }
            }
            catch
            {
                // One malformed or concurrently changed candidate cannot widen cleanup scope.
            }
        }

        return new SweepResult(deleted, bytes);
    }

    private static bool TryDeleteOwnedWorkspace(string rootDirectory, string workspaceDirectory,
        string expectedToken, out long removedBytes)
    {
        removedBytes = 0;
        FileStream? cleanupLease = null;
        try
        {
            if (!TryReadOwnership(rootDirectory, workspaceDirectory, out string actualToken) ||
                !string.Equals(actualToken, expectedToken, StringComparison.Ordinal) ||
                ContainsReparsePoint(workspaceDirectory))
                return false;

            // The live workspace opens this file without write sharing. A cleanup writer therefore
            // succeeds only after that process-held lease is gone. FileShare.Delete lets the exact
            // proven tree be removed while this anti-race handle remains open.
            string leasePath = Path.Combine(workspaceDirectory, LeaseFileName);
            cleanupLease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.Delete);

            // Revalidate after lease acquisition so a marker or tree swap during the open fails
            // closed. The open lease also stops another participating sweep from entering.
            if (!TryReadOwnership(rootDirectory, workspaceDirectory, out actualToken) ||
                !string.Equals(actualToken, expectedToken, StringComparison.Ordinal) ||
                ContainsReparsePoint(workspaceDirectory))
                return false;

            removedBytes = MeasureFiles(workspaceDirectory);
            Directory.Delete(workspaceDirectory, recursive: true);
            return true;
        }
        catch
        {
            removedBytes = 0;
            return false;
        }
        finally
        {
            cleanupLease?.Dispose();
        }
    }

    private static bool TryReadOwnership(string rootDirectory, string workspaceDirectory,
        out string ownerToken)
    {
        ownerToken = "";
        try
        {
            string root = Path.GetFullPath(rootDirectory);
            string workspace = Path.GetFullPath(workspaceDirectory);
            RequirePlainDirectory(root, "The Test & Copy staging root");
            RequireDirectChild(root, workspace);
            RequirePlainDirectory(workspace, "The Test & Copy workspace");

            string name = Path.GetFileName(workspace);
            if (!name.StartsWith(DirectoryPrefix, StringComparison.Ordinal))
                return false;
            string token = name.Substring(DirectoryPrefix.Length);
            if (!Guid.TryParseExact(token, "N", out _))
                return false;

            string marker = Path.Combine(workspace, OwnershipMarkerName);
            if (!File.Exists(marker) ||
                (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0)
                return false;
            string[] lines = File.ReadAllLines(marker);
            if (lines.Length != 2 ||
                !string.Equals(lines[0], OwnershipMagic, StringComparison.Ordinal) ||
                !string.Equals(lines[1], token, StringComparison.Ordinal))
                return false;

            ownerToken = token;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RequireDirectChild(string rootDirectory, string workspaceDirectory)
    {
        string root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string workspace = Path.GetFullPath(workspaceDirectory);
        if (!string.Equals(Path.GetDirectoryName(workspace), root,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException(
                "The Test & Copy workspace escaped its staging root.");
    }

    private static void RequirePlainDirectory(string path, string description)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0)
            throw new IOException(description + " is not a directory.");
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException(description + " is a link or reparse point.");
    }

    private static bool ContainsReparsePoint(string rootDirectory)
    {
        try
        {
            var pending = new Stack<string>();
            pending.Push(rootDirectory);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                FileAttributes currentAttributes = File.GetAttributes(current);
                if ((currentAttributes & FileAttributes.ReparsePoint) != 0)
                    return true;
                foreach (string entry in Directory.EnumerateFileSystemEntries(current, "*",
                    SearchOption.TopDirectoryOnly))
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
            // Unreadable or concurrently replaced trees are not safe cleanup targets.
            return true;
        }
    }

    private static long MeasureFiles(string rootDirectory)
    {
        long bytes = 0;
        foreach (string file in Directory.EnumerateFiles(rootDirectory, "*",
            SearchOption.AllDirectories))
            bytes = checked(bytes + new FileInfo(file).Length);
        return bytes;
    }

    private static void WriteDurableText(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
        writer.Flush();
        stream.Flush(true);
    }

    private static string OwnershipText(string ownerToken) =>
        OwnershipMagic + Environment.NewLine + ownerToken;

    private void CloseLease()
    {
        FileStream? lease = _lease;
        _lease = null;
        lease?.Dispose();
    }
}
