using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bwg.Scsi;

namespace CUETools.Wpf.Services;

/// <summary>
/// Linux implementation of the guided-recovery rung probe (SLICE-011).
///
/// It answers one question with no privileges and no hardware mutation: does
/// this drive answer a table-of-contents read again? That is exactly the check
/// run by hand on 2026-08-14 while a wedged drive was stuck, where the drive
/// answered INQUIRY normally but rejected TEST UNIT READY, READ TOC, and
/// READ CD with one canned IllegalRequest/24/00 in 4 ms
/// (docs/review/2026-08-14-usb-wedge-finding.md).
///
/// The probe never resets anything: the human performs the physical rung. It
/// re-finds the drive by sysfs identity because a drive that returns from a
/// replug can enumerate at a different sr node, which moves its letter.
/// </summary>
public sealed class LinuxDriveRecoveryProbe : IDriveRecoveryProbe
{
    /// <summary>CDROMREADTOCHDR from linux/cdrom.h: two bytes out, first and
    /// last track. The lightest command that proves the mechanism responds.</summary>
    private const uint CDROMREADTOCHDR = 0x5305;

    private const int ENOMEDIUM = 123;
    private const int ENODEV = 19;
    private const int EACCES = 13;
    private const int EPERM = 1;
    private const int ENOENT = 2;

    private readonly Func<int, bool> _delayMs;

    /// <summary>Production constructor: real waiting between polls.</summary>
    public LinuxDriveRecoveryProbe()
        : this(ms => { Thread.Sleep(ms); return true; })
    {
    }

    /// <summary>Test seam: the poll delay is injected so a suite never sleeps.
    /// Returning false ends the watch immediately.</summary>
    internal LinuxDriveRecoveryProbe(Func<int, bool> delayMs)
    {
        _delayMs = delayMs;
    }

    public bool CanVerify => OperatingSystem.IsLinux();

    public DriveRecoveryFingerprint? Snapshot(char drive)
    {
        if (!OperatingSystem.IsLinux())
            return null;
        int n = char.ToUpperInvariant(drive) - 'A';
        if (n < 0 || n > 25)
            return null;
        string sr = "sr" + n;
        if (!Directory.Exists("/sys/block/" + sr))
            return null;
        return new DriveRecoveryFingerprint
        {
            Letter = char.ToUpperInvariant(drive),
            SrNode = sr,
            Vendor = ReadSysfs("/sys/block/" + sr + "/device/vendor"),
            Model = ReadSysfs("/sys/block/" + sr + "/device/model"),
            Revision = ReadSysfs("/sys/block/" + sr + "/device/rev"),
            Serial = ReadUsbSerial(sr),
        };
    }

    public Task<DriveRecoveryProbeReport> VerifyRungAsync(
        DriveRecoveryFingerprint fingerprint,
        TimeSpan timeout,
        CancellationToken ct = default) =>
        Task.Run(() => VerifyRung(fingerprint, timeout, ct), ct);

    private DriveRecoveryProbeReport VerifyRung(
        DriveRecoveryFingerprint fingerprint,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux())
            return Report(DriveRecoveryProbeResult.Unverified, '\0', false, "not linux");

        // A rung starts with the user unplugging something, so the node is
        // expected to vanish. Absence is progress, not a verdict.
        bool sawAbsent = false;
        string lastDetail = "no device";
        int elapsedMs = 0;
        int budgetMs = timeout <= TimeSpan.Zero ? 0 : (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);

        while (true)
        {
            if (ct.IsCancellationRequested)
                return Report(DriveRecoveryProbeResult.Unverified, '\0', sawAbsent, "cancelled");

            string? sr = ResolveNode(fingerprint);
            if (sr == null)
            {
                sawAbsent = true;
                lastDetail = "device absent";
            }
            else
            {
                DriveRecoveryProbeReport probe = ProbeNode(sr, sawAbsent);
                // Only a decided verdict ends the watch. A drive mid-reset can
                // answer ENODEV/ENOMEDIUM transiently while it settles.
                if (probe.Result == DriveRecoveryProbeResult.Responsive ||
                    probe.Result == DriveRecoveryProbeResult.NoDisc ||
                    probe.Result == DriveRecoveryProbeResult.PermissionDenied)
                    return probe;
                lastDetail = probe.Detail;
            }

            if (elapsedMs >= budgetMs)
                break;
            int step = Math.Min(500, budgetMs - elapsedMs);
            if (step <= 0 || !_delayMs(step))
                break;
            elapsedMs += step;
        }

        // Timed out. A drive still present and still rejecting is the wedge; a
        // drive that never came back is absent, which is not the same claim.
        string? finalNode = ResolveNode(fingerprint);
        if (finalNode == null)
            return Report(DriveRecoveryProbeResult.DeviceAbsent, '\0', sawAbsent, lastDetail);
        return Report(
            DriveRecoveryProbeResult.StillUnresponsive,
            LetterForNode(finalNode),
            sawAbsent,
            lastDetail);
    }

    /// <summary>
    /// Issue the TOC-header read on /dev/srN (the block node, never the sg
    /// alias: an sg node answers ENOTTY, which would be misread as a wedge).
    /// </summary>
    private DriveRecoveryProbeReport ProbeNode(string sr, bool reEnumerated)
    {
        char letter = LetterForNode(sr);
        string path = "/dev/" + sr;
        int fd = LinuxSg.OpenDevice(path);
        if (fd < 0)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == EACCES || err == EPERM)
                return Report(
                    DriveRecoveryProbeResult.PermissionDenied,
                    letter,
                    reEnumerated,
                    "open errno=" + err);
            return Report(
                DriveRecoveryProbeResult.DeviceAbsent,
                letter,
                reEnumerated,
                "open errno=" + err);
        }

        try
        {
            IntPtr buffer = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(buffer, 0);
                int rc = LinuxSg.SendBufferIoctl(fd, CDROMREADTOCHDR, buffer);
                if (rc >= 0)
                {
                    byte first = Marshal.ReadByte(buffer, 0);
                    byte last = Marshal.ReadByte(buffer, 1);
                    return Report(
                        DriveRecoveryProbeResult.Responsive,
                        letter,
                        reEnumerated,
                        "toc first=" + first + " last=" + last);
                }
                int err = Marshal.GetLastWin32Error();
                // The drive answered that the tray is empty: it is alive, which
                // is what this rung asks. The disc is the user's business.
                if (err == ENOMEDIUM)
                    return Report(
                        DriveRecoveryProbeResult.NoDisc,
                        letter,
                        reEnumerated,
                        "toc errno=" + err);
                if (err == EACCES || err == EPERM)
                    return Report(
                        DriveRecoveryProbeResult.PermissionDenied,
                        letter,
                        reEnumerated,
                        "toc errno=" + err);
                if (err == ENODEV || err == ENOENT)
                    return Report(
                        DriveRecoveryProbeResult.DeviceAbsent,
                        letter,
                        reEnumerated,
                        "toc errno=" + err);
                // Everything else, EIO above all, is the observed stuck state.
                return Report(
                    DriveRecoveryProbeResult.StillUnresponsive,
                    letter,
                    reEnumerated,
                    "toc errno=" + err);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            LinuxSg.CloseDevice(fd);
        }
    }

    /// <summary>
    /// Find the fingerprinted mechanism's current sr node. Prefers the original
    /// node when its identity still matches, then searches every sr node for an
    /// exact identity match. Refuses to guess when more than one node matches:
    /// probing the wrong mechanism would report a cure for a drive that is
    /// still wedged.
    /// </summary>
    private static string? ResolveNode(DriveRecoveryFingerprint fingerprint)
    {
        if (fingerprint.SrNode.Length > 0 &&
            Directory.Exists("/sys/block/" + fingerprint.SrNode) &&
            Matches(fingerprint, fingerprint.SrNode))
            return fingerprint.SrNode;

        if (!fingerprint.IsIdentifiable)
            return null;

        string? found = null;
        for (int n = 0; n <= 25; n++)
        {
            string sr = "sr" + n;
            if (!Directory.Exists("/sys/block/" + sr))
                continue;
            if (!Matches(fingerprint, sr))
                continue;
            if (found != null)
                return null;
            found = sr;
        }
        return found;
    }

    /// <summary>
    /// Every identity component must match. Serial is deliberately NOT treated
    /// as an override: enclosures ship placeholder serials that are identical
    /// across units (measured 2026-08-15 on this matrix - one enclosure reports
    /// 0123456789ABCDEF), so a serial-only rule would call two different drives
    /// the same drive. Requiring the whole tuple is strictly more selective, and
    /// ResolveNode refuses to guess when it still matches more than one node.
    /// </summary>
    private static bool Matches(DriveRecoveryFingerprint fingerprint, string sr)
    {
        string device = "/sys/block/" + sr + "/device/";
        return string.Equals(
                   fingerprint.Vendor,
                   ReadSysfs(device + "vendor"),
                   StringComparison.Ordinal) &&
               string.Equals(
                   fingerprint.Model,
                   ReadSysfs(device + "model"),
                   StringComparison.Ordinal) &&
               string.Equals(
                   fingerprint.Revision,
                   ReadSysfs(device + "rev"),
                   StringComparison.Ordinal) &&
               string.Equals(
                   fingerprint.Serial,
                   ReadUsbSerial(sr),
                   StringComparison.Ordinal);
    }

    private static char LetterForNode(string sr)
    {
        int n;
        if (sr.Length > 2 && int.TryParse(sr.Substring(2), out n) && n >= 0 && n <= 25)
            return (char)('A' + n);
        return '\0';
    }

    /// <summary>Sysfs files vanish mid-read during a replug; every read is
    /// wrapped exactly like LinuxSg.QueryMaxTransferLength.</summary>
    private static string ReadSysfs(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>
    /// Walk up from the block device to the USB device that owns it and read
    /// its serial. Absent for non-USB drives, which is why identity falls back
    /// to vendor/model/revision.
    /// </summary>
    private static string ReadUsbSerial(string sr)
    {
        try
        {
            // Two-step resolution, and it has to be exactly this. /sys/block/srN
            // is itself a symlink, so resolving .../device in one hop does
            // literal path arithmetic from /sys/block and lands at "/" plus the
            // target name (measured: "/6:0:0:0"). Resolve the block node first,
            // then join the device link's own relative target onto that real
            // path, which is what the kernel means.
            string blockNode = "/sys/block/" + sr;
            if (!Directory.Exists(blockNode))
                return "";
            FileSystemInfo? blockTarget = Directory.ResolveLinkTarget(blockNode, returnFinalTarget: false);
            string blockDir = blockTarget?.FullName ?? blockNode;
            string deviceLink = Path.Combine(blockDir, "device");
            if (!Directory.Exists(deviceLink))
                return "";
            string relative = new DirectoryInfo(deviceLink).LinkTarget ?? "";
            string deviceDir = relative.Length > 0
                ? Path.GetFullPath(Path.Combine(blockDir, relative))
                : deviceLink;
            var current = new DirectoryInfo(deviceDir);
            for (int depth = 0; depth < 12 && current != null; depth++)
            {
                string serial = Path.Combine(current.FullName, "serial");
                if (File.Exists(Path.Combine(current.FullName, "idVendor")) &&
                    File.Exists(serial))
                    return ReadSysfs(serial);
                current = current.Parent;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return "";
    }

    private static DriveRecoveryProbeReport Report(
        DriveRecoveryProbeResult result,
        char letter,
        bool reEnumerated,
        string detail) =>
        new DriveRecoveryProbeReport
        {
            Result = result,
            ResolvedDrive = letter,
            ReEnumerated = reEnumerated,
            Detail = detail,
        };
}
