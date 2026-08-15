//
// Linux SCSI Generic (SG_IO) interop for the Linux port. This file compiles
// into every TargetFramework but only executes when the runtime reports Unix;
// the Windows transport in WinDev/Device is untouched by anything here.
//
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Bwg.Scsi
{
    /// <summary>
    /// Linux SG_IO support: device naming, open/close, and the sg_io_hdr
    /// structure consumed by Device.SendCommandLinux.
    ///
    /// Drive letters map 1:1 to kernel sr numbers (A = /dev/sr0, B = /dev/sr1,
    /// ...). CUETools.Ripper.CDDrivesList applies the same pure function when
    /// enumerating drives, so the mapping needs no shared state; keep the two
    /// sites in agreement.
    /// </summary>
    public static class LinuxSg
    {
        /// <summary>
        /// True exactly when the Linux SG_IO transport applies (the platform
        /// split used by WinDev and Device). On modern TFMs this asks the
        /// runtime precisely: PlatformID.Unix alone also matches macOS, which
        /// has no /dev/srN or SG_IO and must fail closed in WinDev.Open
        /// instead of walking the Linux path.
        /// </summary>
        public static bool IsLinux
        {
            get
            {
#if NETFRAMEWORK
                // Legacy Windows-only TFMs (net20/net47): keep the historical
                // check; RuntimeInformation is not available on net20.
                return Environment.OSVersion.Platform == PlatformID.Unix;
#else
                return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
#endif
            }
        }

        // From scsi/sg.h.
        internal const int SG_IO = 0x2285;
        internal const int SG_DXFER_NONE = -1;
        internal const int SG_DXFER_TO_DEV = -2;
        internal const int SG_DXFER_FROM_DEV = -3;
        /// <summary>driver_status bit meaning "sense data is valid" (DRIVER_SENSE).</summary>
        internal const int SG_DRIVER_SENSE = 0x08;

        // Linux generic open(2) flags (asm-generic/fcntl.h).
        private const int O_RDONLY = 0x0000;
        private const int O_RDWR = 0x0002;
        private const int O_NONBLOCK = 0x0800;

        /// <summary>
        /// sg_io_hdr from scsi/sg.h. Sequential layout with IntPtr pointer
        /// fields reproduces the kernel struct for the build's native ABI.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct SgIoHdr
        {
            public int interface_id;        // always 'S'
            public int dxfer_direction;
            public byte cmd_len;
            public byte mx_sb_len;
            public ushort iovec_count;
            public uint dxfer_len;
            public IntPtr dxferp;
            public IntPtr cmdp;
            public IntPtr sbp;
            public uint timeout;            // milliseconds
            public uint flags;
            public int pack_id;
            public IntPtr usr_ptr;
            public byte status;
            public byte masked_status;
            public byte msg_status;
            public byte sb_len_wr;
            public ushort host_status;
            public ushort driver_status;
            public int resid;
            public uint duration;
            public uint info;
        }

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int sys_open(string path, int flags);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int sys_close(int fd);

        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        private static extern int sys_ioctl(int fd, UIntPtr request, ref SgIoHdr hdr);

        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        private static extern int sys_ioctl_bare(int fd, UIntPtr request, IntPtr arg);

        /// <summary>
        /// Open a device node for SG_IO. The sg driver requires O_RDWR to issue
        /// commands; fall back to read-only (valid for SG_IO on sr nodes) when
        /// write access is denied. Returns the fd, or -1 with errno available
        /// via Marshal.GetLastWin32Error().
        /// </summary>
        public static int OpenDevice(string path)
        {
            int fd = sys_open(path, O_RDWR | O_NONBLOCK);
            if (fd < 0)
                fd = sys_open(path, O_RDONLY | O_NONBLOCK);
            return fd;
        }

        /// <summary>
        /// Close a device fd obtained from OpenDevice.
        /// </summary>
        public static int CloseDevice(int fd)
        {
            return sys_close(fd);
        }

        /// <summary>
        /// Issue the SG_IO ioctl. Returns the raw ioctl result (negative on
        /// failure, errno via Marshal.GetLastWin32Error()).
        /// </summary>
        internal static int SendSgIo(int fd, ref SgIoHdr hdr)
        {
            return sys_ioctl(fd, (UIntPtr)SG_IO, ref hdr);
        }

        /// <summary>
        /// Issue an argument-less ioctl (e.g. the CDROM tray requests) on an
        /// open fd. Returns the raw ioctl result.
        /// </summary>
        public static int SendBareIoctl(int fd, uint request)
        {
            return sys_ioctl_bare(fd, (UIntPtr)request, IntPtr.Zero);
        }

        /// <summary>
        /// The block-device node for a drive letter (/dev/srN), or null when
        /// the sr device does not exist. The CDROM ioctls (tray control) talk
        /// to this node, not the sg alias.
        /// </summary>
        public static string SrPathForLetter(char letter)
        {
            int n = char.ToUpperInvariant(letter) - 'A';
            if (n < 0 || n > 25 || !Directory.Exists("/sys/block/sr" + n))
                return null;
            return "/dev/sr" + n;
        }

        /// <summary>
        /// Map a drive letter to a device node. Prefers the sg node bound to
        /// /dev/srN (found via /sys/block/srN/device/scsi_generic, which avoids
        /// needing readlink); falls back to /dev/srN itself - SG_IO works on
        /// both. Returns null when the sr device does not exist.
        /// </summary>
        public static string DevicePathForLetter(char letter)
        {
            int n = char.ToUpperInvariant(letter) - 'A';
            if (n < 0 || n > 25)
                return null;
            string sys = "/sys/block/sr" + n;
            if (!Directory.Exists(sys))
                return null;
            try
            {
                string generic = sys + "/device/scsi_generic";
                if (Directory.Exists(generic))
                {
                    string[] entries = Directory.GetDirectories(generic);
                    if (entries.Length == 1)
                    {
                        string dev = "/dev/" + Path.GetFileName(entries[0]);
                        if (File.Exists(dev))
                            return dev;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return "/dev/sr" + n;
        }

        /// <summary>
        /// The block-layer transfer limit in bytes for the sr device behind an
        /// opened device path (/dev/srN or its sg alias), from
        /// /sys/block/srN/queue/max_sectors_kb. Returns -1 when it cannot be
        /// determined; the caller applies its own fallback and cap.
        /// </summary>
        public static int QueryMaxTransferLength(string devicePath)
        {
            try
            {
                string sr = SrNodeForDevicePath(devicePath);
                if (sr == null)
                    return -1;
                string text = File.ReadAllText("/sys/block/" + sr + "/queue/max_sectors_kb").Trim();
                int kb;
                if (!int.TryParse(text, out kb) || kb <= 0)
                    return -1;
                return kb * 1024;
            }
            catch (IOException)
            {
                return -1;
            }
            catch (UnauthorizedAccessException)
            {
                return -1;
            }
        }

        /// <summary>
        /// Resolve a device path (/dev/srN or /dev/sgX) to its srN sysfs node
        /// name, or null if it is not an sr-backed device.
        /// </summary>
        private static string SrNodeForDevicePath(string devicePath)
        {
            string name = Path.GetFileName(devicePath);
            if (name.StartsWith("sr"))
                return Directory.Exists("/sys/block/" + name) ? name : null;
            if (!name.StartsWith("sg"))
                return null;
            for (int n = 0; n < 26; n++)
            {
                string generic = "/sys/block/sr" + n + "/device/scsi_generic/" + name;
                if (Directory.Exists(generic))
                    return "sr" + n;
            }
            return null;
        }
    }
}
