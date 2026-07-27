using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CUETools.Wpf.Services;

/// <summary>
/// Reserves one album destination across processes, stages the complete album beside that
/// destination, and publishes it with one same-volume directory rename.
///
/// A reservation is a small, deterministically named sibling file held open for the life of the
/// transaction. Any existing sentinel is treated as occupied; deleting it by path after inspection
/// would permit a concurrent replacement to become the deletion target. A numbered sibling keeps
/// progress possible without touching an object this process does not own. The final album
/// directory remains absent until <see cref="Publish"/>.
/// </summary>
public sealed class AlbumOutputTransaction : IDisposable
{
    internal const string ReservationMagic = "CUETOOLS_OUTPUT_RESERVATION_V1";
    internal const string CompletionMarkerName = ".cuetools-complete";
    internal const string OwnershipMarkerName = ".cuetools-stage-owner";
    internal const string ProofPendingMarkerName =
        ".cuetools-output-proof-pending";
    internal const string ProofFailureMarkerName =
        ".cuetools-output-proof-failed";
    internal const string StageOwnershipMagic = "CUETOOLS_OUTPUT_STAGE_V1";
    internal const string ProofPendingMagic =
        "CUETOOLS_OUTPUT_PROOF_PENDING_V1";

    private FileStream? _reservation;
    private readonly string _baseDirectory;
    private readonly string _ownerToken;
    private bool _published;
    private bool _publicationFinalized;
    private bool _preserveStaging;
    private bool _disposed;

    private AlbumOutputTransaction(string baseDirectory, string relativeDirectory,
        string destinationDirectory,
        string stagingDirectory, string reservationPath, FileStream reservation,
        string ownerToken)
    {
        _baseDirectory = baseDirectory;
        RelativeDirectory = relativeDirectory;
        DestinationDirectory = destinationDirectory;
        StagingDirectory = stagingDirectory;
        ReservationPath = reservationPath;
        _reservation = reservation;
        _ownerToken = ownerToken;
    }

    public string RelativeDirectory { get; }
    public string DestinationDirectory { get; }
    public string StagingDirectory { get; private set; }
    internal string ReservationPath { get; }
    public bool IsPublished => _publicationFinalized;

    /// <summary>
    /// Atomically reserve the requested relative album directory, or a numbered sibling when that
    /// destination already exists or is reserved by another process.
    /// </summary>
    public static AlbumOutputTransaction Reserve(string baseDirectory, string requestedRelativeDirectory,
        Action<string>? onNote = null)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("An output base directory is required.", nameof(baseDirectory));
        if (string.IsNullOrWhiteSpace(requestedRelativeDirectory))
            throw new ArgumentException("An album directory is required.", nameof(requestedRelativeDirectory));

        string baseFull = Path.GetFullPath(baseDirectory);
        Directory.CreateDirectory(baseFull);
        RequireSafeDirectoryAncestry(baseFull, baseFull);

        for (int n = 1; n <= 999; n++)
        {
            string candidate = n == 1
                ? requestedRelativeDirectory
                : requestedRelativeDirectory + " (" + n + ")";
            string destination = ResolveContainedPath(baseFull, candidate);
            string? parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(parent))
                throw new IOException("The album destination has no parent directory.");
            RequireSafeDirectoryAncestry(baseFull, parent, requireTarget: false);
            Directory.CreateDirectory(parent);
            // Check again after creation. An existing junction must never redirect staging outside
            // the selected output tree, and a concurrently replaced parent fails closed.
            RequireSafeDirectoryAncestry(baseFull, parent);

            string id = ReservationId(destination);
            string reservationPath = Path.Combine(parent, ".cuetools-reserve-" + id);
            FileStream? reservation = TryAcquireReservation(reservationPath);
            if (reservation == null)
                continue;

            try
            {
                // A crashed proof-bound publication retains both its owner and pending markers.
                // Recover it only while holding the matching destination reservation. A normal
                // completed album or a lookalike remains untouched and selects a numbered sibling.
                if (Directory.Exists(destination) || File.Exists(destination))
                {
                    if (!TryQuarantinePendingPublication(destination, parent, id) ||
                        Directory.Exists(destination) || File.Exists(destination))
                    {
                        ReleaseReservation(ref reservation);
                        continue;
                    }
                }

                // A normal process exit removes its reservation with DeleteOnClose. Any matching
                // stage that predates this newly created lock is therefore an interrupted run.
                QuarantineOrphanedStages(parent, id);

                string staging = Path.Combine(parent,
                    ".cuetools-stage-" + id + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                RequireSafeDirectoryAncestry(baseFull, staging);
                string ownerToken = Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllText(Path.Combine(staging, OwnershipMarkerName),
                        StageOwnershipText(id, ownerToken));
                }
                catch
                {
                    try { Directory.Delete(staging, true); } catch { }
                    throw;
                }

                if (n > 1)
                {
                    try
                    {
                        onNote?.Invoke(
                            "output folder is occupied or reserved - writing to \"" +
                            candidate + "\" instead");
                    }
                    catch
                    {
                        // This notification is advisory. Its sink cannot invalidate an otherwise
                        // healthy reservation or strand an owned stage before the caller receives it.
                    }
                }

                return new AlbumOutputTransaction(baseFull, candidate, destination, staging,
                    reservationPath, reservation, ownerToken);
            }
            catch
            {
                ReleaseReservation(ref reservation);
                throw;
            }
        }

        throw new IOException("Could not reserve a unique album output directory.");
    }

    /// <summary>
    /// Require a populated staging directory, write the completion marker, and make the entire album
    /// visible with one directory rename. The staging directory is a sibling of the destination, so
    /// the move stays on one volume.
    /// </summary>
    public string Publish()
    {
        string destination = PublishPendingValidation();
        CompletePublication();
        return destination;
    }

    /// <summary>
    /// Moves the complete stage into place but retains the reservation and ownership marker while
    /// the caller validates proof handles against the destination name.
    /// </summary>
    internal string PublishPendingValidation()
    {
        ThrowIfDisposed();
        if (_published)
            throw new InvalidOperationException("This album transaction has already been published.");
        RequireSafeDirectoryAncestry(_baseDirectory,
            Path.GetDirectoryName(DestinationDirectory)
                ?? throw new IOException("The album destination has no parent directory."));
        RequireSafeDirectoryAncestry(_baseDirectory, StagingDirectory);
        RequireOwnership();
        if (!Directory.Exists(StagingDirectory))
            throw new DirectoryNotFoundException("The album staging directory is missing.");
        RequireNoReparsePoints();
        if (!HasPayloadFiles())
            throw new InvalidDataException("Refusing to publish an empty album.");
        if (Directory.Exists(DestinationDirectory) || File.Exists(DestinationDirectory))
            throw new IOException("The reserved album destination became occupied before publication.");

        string marker = Path.Combine(StagingDirectory, CompletionMarkerName);
        // CreateNew is part of the ownership proof. Overwriting a pre-existing name here could
        // follow a reparse point introduced after the tree scan or silently bless foreign data as
        // a completed CUETools transaction.
        using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.WriteLine("CUETOOLS_OUTPUT_COMPLETE_V1");
            writer.Flush();
            stream.Flush(true);
        }

        string pendingMarker = Path.Combine(
            StagingDirectory,
            ProofPendingMarkerName);
        using (var stream = new FileStream(
            pendingMarker,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read))
        using (var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false),
            1024,
            leaveOpen: true))
        {
            writer.Write(ProofPendingText(
                ReservationId(DestinationDirectory),
                _ownerToken));
            writer.Flush();
            stream.Flush(true);
        }

        Directory.Move(StagingDirectory, DestinationDirectory);
        _published = true;
        StagingDirectory = DestinationDirectory;
        return DestinationDirectory;
    }

    /// <summary>Commits a pending publication only after its destination-bound checks pass.</summary>
    internal void CompletePublication()
    {
        ThrowIfDisposed();
        if (!_published)
            throw new InvalidOperationException(
                "This album transaction has not been moved into place.");
        if (_publicationFinalized)
            return;

        _publicationFinalized = true;
        try { File.Delete(Path.Combine(DestinationDirectory, ProofPendingMarkerName)); }
        catch
        {
            // Removing either transaction marker prevents crash recovery from mistaking this
            // already validated publication for a pending one.
        }
        try { File.Delete(Path.Combine(DestinationDirectory, OwnershipMarkerName)); }
        catch
        {
            // Publication already succeeded. A harmless hidden marker must not misreport failure.
        }
        try
        {
            ReleaseReservation();
        }
        catch
        {
            // The directory rename is the commit point. A DeleteOnClose/handle cleanup failure
            // after that point must not make callers report a failed rip or invite a duplicate
            // retry when the complete album is already visible.
        }
    }

    /// <summary>
    /// Marks and moves a publication whose destination-bound proof failed into an explicit
    /// incomplete sibling. Failure to move still leaves the failure marker at the destination and
    /// never turns the operation into a reported success.
    /// </summary>
    internal string QuarantinePublishedProofFailure()
    {
        ThrowIfDisposed();
        if (!_published || _publicationFinalized)
            throw new InvalidOperationException(
                "Only a pending publication can be quarantined.");

        string retained = DestinationDirectory;
        try
        {
            string marker = Path.Combine(
                DestinationDirectory,
                ProofFailureMarkerName);
            using (var stream = new FileStream(
                marker,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read))
            using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                1024,
                leaveOpen: true))
            {
                writer.WriteLine("CUETOOLS_OUTPUT_PROOF_FAILED_V1");
                writer.Flush();
                stream.Flush(true);
            }
        }
        catch
        {
            // The caller still reports failure. The same-volume rename below is the stronger
            // quarantine signal when another process did not lock the destination.
        }

        string? parent = Path.GetDirectoryName(DestinationDirectory);
        if (!string.IsNullOrEmpty(parent))
        {
            string incomplete = Path.Combine(
                parent,
                ".cuetools-incomplete-published-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.Move(DestinationDirectory, incomplete);
                retained = incomplete;
                StagingDirectory = incomplete;
            }
            catch
            {
                StagingDirectory = DestinationDirectory;
            }
        }

        _preserveStaging = true;
        try { ReleaseReservation(); }
        catch
        {
            // The retained directory is already explicitly failed or incomplete.
        }
        return retained;
    }

    /// <summary>
    /// Preserve a failed, nonempty stage under an explicit incomplete name. This is for a completed
    /// or partly completed optical read that may be costly to repeat. Empty stages are left for normal
    /// disposal.
    /// </summary>
    public string PreserveIncomplete()
    {
        ThrowIfDisposed();
        if (_published)
            return _preserveStaging
                ? StagingDirectory
                : DestinationDirectory;
        RequireSafeDirectoryAncestry(_baseDirectory, StagingDirectory);
        RequireOwnership();
        if (!HasPayloadFiles())
            return "";

        string? parent = Path.GetDirectoryName(StagingDirectory);
        if (string.IsNullOrEmpty(parent))
        {
            _preserveStaging = true;
            return StagingDirectory;
        }

        string incomplete = Path.Combine(parent,
            ".cuetools-incomplete-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.Move(StagingDirectory, incomplete);
            StagingDirectory = incomplete;
        }
        catch
        {
            // The owned stage is still safer retained than deleted after a costly disc read.
        }
        _preserveStaging = true;
        return StagingDirectory;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            // Keep the destination reservation held through cleanup. Releasing first lets a new
            // transaction quarantine this still-live stage between our ownership check and delete.
            if (!_published && !_preserveStaging)
            {
                try
                {
                    if (Directory.Exists(StagingDirectory) && HasOwnership() &&
                        !ContainsReparsePoint())
                        Directory.Delete(StagingDirectory, true);
                }
                catch
                {
                    // Best effort. The dot-prefixed name still identifies an incomplete owned stage.
                }
            }
        }
        finally
        {
            ReleaseReservation();
        }
    }

    private static string ResolveContainedPath(string baseFull, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(baseFull, relative));
        string prefix = baseFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The album path escapes the selected output directory.");
        return full;
    }

    /// <summary>
    /// Reject a selected base or existing child directory that is a link, junction, or other
    /// reparse point. Lexical containment alone is not enough: an existing junction under the base
    /// can redirect a sibling stage and its eventual rename outside the directory the user chose.
    /// </summary>
    private static void RequireSafeDirectoryAncestry(string baseFull, string targetDirectory,
        bool requireTarget = true)
    {
        string basePath = Path.GetFullPath(baseFull);
        string targetPath = Path.GetFullPath(targetDirectory);
        string relative = Path.GetRelativePath(basePath, targetPath);
        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new IOException("The album path escapes the selected output directory.");

        RequireSafeDirectory(basePath, required: true);
        if (string.Equals(relative, ".", StringComparison.Ordinal))
            return;

        string current = basePath;
        string[] parts = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < parts.Length; index++)
        {
            current = Path.Combine(current, parts[index]);
            RequireSafeDirectory(current, required: requireTarget);
        }
    }

    private static void RequireSafeDirectory(string path, bool required)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0)
                throw new IOException("An album output ancestor is not a directory.");
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(
                    "An album output ancestor is a link or reparse point.");
        }
        catch (FileNotFoundException) when (!required)
        {
        }
        catch (DirectoryNotFoundException) when (!required)
        {
        }
    }

    private bool HasPayloadFiles()
    {
        string marker = Path.Combine(StagingDirectory, OwnershipMarkerName);
        var pending = new Stack<string>();
        pending.Push(StagingDirectory);
        while (pending.Count != 0)
        {
            string current = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(current, "*",
                SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(entry, marker, StringComparison.OrdinalIgnoreCase))
                    continue;
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
                if ((attributes & FileAttributes.Directory) == 0)
                    return true;
                pending.Push(entry);
            }
        }
        return false;
    }

    private bool HasOwnership()
    {
        try
        {
            if (!Directory.Exists(StagingDirectory))
                return false;
            RequireSafeDirectoryAncestry(_baseDirectory, StagingDirectory);
            string marker = Path.Combine(StagingDirectory, OwnershipMarkerName);
            return (File.GetAttributes(StagingDirectory) & FileAttributes.ReparsePoint) == 0
                && File.Exists(marker)
                && (File.GetAttributes(marker) & FileAttributes.ReparsePoint) == 0
                && string.Equals(File.ReadAllText(marker),
                    StageOwnershipText(ReservationId(DestinationDirectory), _ownerToken),
                    StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private void RequireOwnership()
    {
        if (!HasOwnership())
            throw new InvalidOperationException(
                "Album staging ownership could not be proven.");
    }

    private static string ReservationId(string destination)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(destination.ToUpperInvariant()));
        return Convert.ToHexString(digest, 0, 10).ToLowerInvariant();
    }

    private static FileStream? TryAcquireReservation(string path)
    {
        // CreateNew is the ownership boundary. Once it reports an existing object, never inspect
        // and later delete that path: another process could swap a foreign file between those
        // operations. DeleteOnClose removes reservations we actually create, while an unexpected
        // leftover merely occupies this candidate and Reserve() advances to a numbered sibling.
        return TryCreateReservation(path);
    }

    private static void QuarantineOrphanedStages(string parent, string reservationId)
    {
        string pattern = ".cuetools-stage-" + reservationId + "-*";
        string[] stages;
        try { stages = Directory.GetDirectories(parent, pattern, SearchOption.TopDirectoryOnly); }
        catch { return; }

        foreach (string stage in stages)
        {
            if (!HasRecoverableStageMarker(stage, reservationId))
                continue;
            string incomplete = Path.Combine(parent,
                ".cuetools-incomplete-recovered-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                "-" + Guid.NewGuid().ToString("N"));
            try { Directory.Move(stage, incomplete); }
            catch
            {
                // Leave it under the already explicit stage name rather than risking its contents.
            }
        }
    }

    private static bool TryQuarantinePendingPublication(
        string destination,
        string parent,
        string reservationId)
    {
        if (!HasRecoverableStageMarker(destination, reservationId) ||
            !HasMatchingProofPendingMarker(destination, reservationId))
            return false;

        string incomplete = Path.Combine(
            parent,
            ".cuetools-incomplete-published-recovered-" +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.Move(destination, incomplete);
            return true;
        }
        catch
        {
            // Leave the pending destination and choose another name. It is safer than moving or
            // deleting a directory whose ownership can no longer be proven at the operation.
            return false;
        }
    }

    private static bool HasRecoverableStageMarker(string stage, string reservationId)
    {
        try
        {
            if ((File.GetAttributes(stage) & FileAttributes.ReparsePoint) != 0)
                return false;
            string marker = Path.Combine(stage, OwnershipMarkerName);
            if (!File.Exists(marker) ||
                (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0)
                return false;
            string[] lines = File.ReadAllLines(marker);
            return lines.Length == 3
                && string.Equals(lines[0], StageOwnershipMagic, StringComparison.Ordinal)
                && string.Equals(lines[1], reservationId, StringComparison.Ordinal)
                && Guid.TryParseExact(lines[2], "N", out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasMatchingProofPendingMarker(
        string stage,
        string reservationId)
    {
        try
        {
            string ownerPath = Path.Combine(stage, OwnershipMarkerName);
            string pendingPath = Path.Combine(stage, ProofPendingMarkerName);
            if (!File.Exists(pendingPath) ||
                (File.GetAttributes(pendingPath) & FileAttributes.ReparsePoint) != 0)
                return false;

            string[] ownerLines = File.ReadAllLines(ownerPath);
            string[] pendingLines = File.ReadAllLines(pendingPath);
            return ownerLines.Length == 3
                && pendingLines.Length == 3
                && string.Equals(pendingLines[0], ProofPendingMagic,
                    StringComparison.Ordinal)
                && string.Equals(pendingLines[1], reservationId,
                    StringComparison.Ordinal)
                && string.Equals(pendingLines[2], ownerLines[2],
                    StringComparison.Ordinal)
                && Guid.TryParseExact(pendingLines[2], "N", out _);
        }
        catch
        {
            return false;
        }
    }

    private static string StageOwnershipText(string reservationId, string ownerToken)
    {
        return StageOwnershipMagic + Environment.NewLine + reservationId +
            Environment.NewLine + ownerToken;
    }

    private static string ProofPendingText(string reservationId, string ownerToken)
    {
        return ProofPendingMagic + Environment.NewLine + reservationId +
            Environment.NewLine + ownerToken;
    }

    private void RequireNoReparsePoints()
    {
        if (ContainsReparsePoint())
            throw new IOException("Refusing to publish an album stage containing a reparse point.");
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
            // An unreadable or concurrently replaced stage is not safe to delete or publish.
            return true;
        }
    }

    private static FileStream? TryCreateReservation(string path)
    {
        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.Read, 4096, FileOptions.DeleteOnClose);
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
            {
                writer.WriteLine(ReservationMagic);
                writer.WriteLine(Environment.ProcessId);
                writer.Flush();
            }
            stream.Flush(true);
            stream.Position = 0;
            return stream;
        }
        catch (IOException)
        {
            stream?.Dispose();
            return null;
        }
        catch
        {
            stream?.Dispose();
            throw;
        }
    }

    private void ReleaseReservation()
    {
        FileStream? reservation = _reservation;
        if (reservation == null) return;
        _reservation = null;
        ReleaseReservation(ref reservation);
    }

    private static void ReleaseReservation(ref FileStream? reservation)
    {
        try { reservation?.Dispose(); }
        finally
        {
            reservation = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AlbumOutputTransaction));
    }
}
