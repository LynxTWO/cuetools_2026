using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CUETools.Wpf.Services;

/// <summary>
/// Exclusive, cross-process ownership of an optical drive.
///
/// A rip must not merely rely on the process-local SCSI gate: two CUETools windows can
/// otherwise open the same hardware concurrently. Each lease holds both the current drive
/// letter and, when Windows exposes it, the underlying storage device number. The letter
/// prevents two jobs racing while identity is resolved; the device number prevents aliases
/// from claiming the same physical mechanism.
///
/// Nested acquisition is permitted only in the same logical operation context. This is
/// required because Test &amp; Copy owns the drive across calibration and 2-3 child reads.
/// Another UI thread in the same process receives the same denial as another process.
/// </summary>
internal class OpticalDriveLease : IDisposable
{
    private static readonly object Gate = new();
    // DriveService/RipService device operations are synchronous on one worker thread. Keep
    // ownership thread-bound: AsyncLocal flows into newly spawned work and would let an
    // unrelated UI/background operation inherit another job's hardware authority.
    [ThreadStatic]
    private static Guid? _threadOwner;
    private static readonly Dictionary<string, HeldFile> Held =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string[] _paths;
    private readonly Guid? _previousOwner;
    private int _disposed;

    private OpticalDriveLease(
        string[] paths,
        Guid? previousOwner,
        char drive,
        string physicalIdentity)
    {
        _paths = paths;
        _previousOwner = previousOwner;
        Drive = drive;
        PhysicalIdentity = physicalIdentity;
    }

    internal char Drive { get; }
    internal string PhysicalIdentity { get; }

    internal static OpticalDriveLease? TryAcquire(
        char drive,
        IDiagnosticLog log)
    {
        char normalized = char.ToUpperInvariant(drive);
        string root = DefaultRoot();
        string letterKey = "letter-" + normalized;

        // Claim the letter before asking Windows for the physical device number. If another
        // CUETools job already owns this drive, no SCSI/storage query touches its live handle.
        OpticalDriveLease? letter = TryAcquireKeys(
            normalized,
            "",
            root,
            new[] { letterKey });
        if (letter == null)
        {
            log.Warn("drive", $"drive {normalized}: already owned by another CUETools job");
            return null;
        }

        string physical = OpticalDriveIdentity.TryResolve(normalized);
        if (string.IsNullOrEmpty(physical))
            return letter;

        OpticalDriveLease? complete = TryAcquireKeys(
            normalized,
            physical,
            root,
            new[] { "device-" + physical });
        if (complete == null)
        {
            letter.Dispose();
            log.Warn("drive",
                $"drive {normalized}: physical device is already owned by another CUETools job");
            return null;
        }

        return new CompositeOpticalDriveLease(letter, complete);
    }

    /// <summary>Test seam: acquire one caller-supplied key under an isolated root.</summary>
    internal static OpticalDriveLease? TryAcquireForTest(
        string key,
        string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A lease key is required.", nameof(key));
        return TryAcquireKeys(
            '\0',
            key,
            rootDirectory,
            new[] { "test-" + key });
    }

    internal static string GetLockPathForTest(
        string key,
        string rootDirectory) =>
        LockPath(rootDirectory, "test-" + key);

    private static OpticalDriveLease? TryAcquireKeys(
        char drive,
        string physicalIdentity,
        string rootDirectory,
        IReadOnlyList<string> keys)
    {
        string fullRoot = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(fullRoot);
        var paths = new string[keys.Count];
        for (int i = 0; i < keys.Count; i++)
            paths[i] = LockPath(fullRoot, keys[i]);

        Guid? previousOwner = _threadOwner;
        Guid owner = previousOwner ?? Guid.NewGuid();
        var opened = new List<(string Path, FileStream Stream)>();

        lock (Gate)
        {
            foreach (string path in paths)
            {
                if (Held.TryGetValue(path, out HeldFile? sameProcess))
                {
                    if (sameProcess.Owner != owner)
                    {
                        CloseOpened(opened);
                        return null;
                    }
                    continue;
                }

                try
                {
                    var stream = new FileStream(
                        path,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.Read);
                    if (OperatingSystem.IsLinux())
                    {
                        // Linux: the FileShare mode is not enforced across
                        // processes, so exclusion comes from an explicit
                        // advisory lock; a conflict throws and is treated
                        // exactly like a Windows sharing violation.
                        // (FileStream.Lock is unsupported on macOS, which
                        // this project does not target.)
                        try
                        {
                            stream.Lock(0, 1);
                        }
                        catch (IOException)
                        {
                            stream.Dispose();
                            CloseOpened(opened);
                            return null;
                        }
                    }
                    WriteOwner(stream, drive, physicalIdentity);
                    opened.Add((path, stream));
                }
                catch (IOException ex) when (IsSharingViolation(ex))
                {
                    CloseOpened(opened);
                    return null;
                }
                catch
                {
                    CloseOpened(opened);
                    throw;
                }
            }

            foreach ((string path, FileStream stream) in opened)
                Held.Add(path, new HeldFile(owner, stream));
            foreach (string path in paths)
                Held[path].References++;
        }

        _threadOwner = owner;
        return new OpticalDriveLease(
            paths,
            previousOwner,
            drive,
            physicalIdentity);
    }

    private static void CloseOpened(
        List<(string Path, FileStream Stream)> opened)
    {
        foreach ((_, FileStream stream) in opened)
            stream.Dispose();
    }

    private static void WriteOwner(
        FileStream stream,
        char drive,
        string physicalIdentity)
    {
        string text =
            "CUETools2026 optical drive lease" + Environment.NewLine +
            "pid=" + Environment.ProcessId + Environment.NewLine +
            "startedUtc=" + DateTime.UtcNow.ToString("O") + Environment.NewLine +
            (drive == '\0' ? "" : "drive=" + drive + Environment.NewLine) +
            (string.IsNullOrEmpty(physicalIdentity)
                ? ""
                : "device=" + physicalIdentity + Environment.NewLine);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        stream.Position = 0;
        stream.SetLength(0);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static string DefaultRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CUETools2026",
            "drive-leases");

    private static string LockPath(string rootDirectory, string key)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string hash = Convert.ToHexString(digest, 0, 12).ToLowerInvariant();
        return Path.Combine(rootDirectory, "drive-" + hash + ".lock");
    }

    private static bool IsSharingViolation(IOException ex)
    {
        int code = ex.HResult & 0xffff;
        return code == 32 || code == 33;
    }

    public virtual void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        lock (Gate)
        {
            foreach (string path in _paths)
            {
                HeldFile held = Held[path];
                held.References--;
                if (held.References == 0)
                {
                    Held.Remove(path);
                    held.Stream.Dispose();
                }
            }
        }
        _threadOwner = _previousOwner;
    }

    private sealed class HeldFile
    {
        internal HeldFile(Guid owner, FileStream stream)
        {
            Owner = owner;
            Stream = stream;
        }

        internal Guid Owner { get; }
        internal FileStream Stream { get; }
        internal int References { get; set; }
    }

    /// <summary>
    /// One public acquisition owns two internal keys. Subclassing keeps callers on one
    /// IDisposable while preserving the same logical-owner context until both are released.
    /// </summary>
    private sealed class CompositeOpticalDriveLease : OpticalDriveLease
    {
        private readonly OpticalDriveLease _letter;
        private readonly OpticalDriveLease _device;
        private int _compositeDisposed;

        internal CompositeOpticalDriveLease(
            OpticalDriveLease letter,
            OpticalDriveLease device)
            : base(
                Array.Empty<string>(),
                _threadOwner,
                letter.Drive,
                device.PhysicalIdentity)
        {
            _letter = letter;
            _device = device;
        }

        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _compositeDisposed, 1) != 0)
                return;
            _device.Dispose();
            _letter.Dispose();
        }
    }

    private static class OpticalDriveIdentity
    {
        private const uint IoctlStorageGetDeviceNumber = 0x002D1080;
        private const uint ShareRead = 0x00000001;
        private const uint ShareWrite = 0x00000002;
        private const uint ShareDelete = 0x00000004;
        private const uint OpenExisting = 3;
        private static readonly IntPtr InvalidHandle = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct StorageDeviceNumber
        {
            internal uint DeviceType;
            internal uint DeviceNumber;
            internal uint PartitionNumber;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            IntPtr device,
            uint controlCode,
            IntPtr input,
            uint inputSize,
            out StorageDeviceNumber output,
            uint outputSize,
            out uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        internal static string TryResolve(char drive)
        {
            if (!OperatingSystem.IsWindows())
            {
                // Linux letters map 1:1 to kernel sr numbers (the transport's
                // pure function). The block device's MAJ:MIN is the stable
                // physical identity for the mechanism, so two windows aiming
                // at the same hardware collide on "device-sr-MAJ:MIN"
                // regardless of node aliases.
                int n = char.ToUpperInvariant(drive) - 'A';
                try
                {
                    string devPath = "/sys/block/sr" + n + "/dev";
                    if (n >= 0 && n <= 25 && File.Exists(devPath))
                        return "sr-" + File.ReadAllText(devPath).Trim();
                }
                catch (IOException)
                {
                }
                return "";
            }

            IntPtr handle = CreateFileW(
                $@"\\.\{drive}:",
                0,
                ShareRead | ShareWrite | ShareDelete,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);
            if (handle == InvalidHandle)
                return "";
            try
            {
                if (!DeviceIoControl(
                        handle,
                        IoctlStorageGetDeviceNumber,
                        IntPtr.Zero,
                        0,
                        out StorageDeviceNumber number,
                        (uint)Marshal.SizeOf<StorageDeviceNumber>(),
                        out uint returned,
                        IntPtr.Zero) ||
                    returned < (uint)Marshal.SizeOf<StorageDeviceNumber>())
                    return "";

                return number.DeviceType.ToString("x8") + "-" +
                    number.DeviceNumber.ToString("x8");
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
