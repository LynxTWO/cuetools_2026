// ****************************************************************************
//
// DriveInspector - read everything an optical drive will tell us about itself,
// without needing an audio disc in the tray.
//
// CDDriveReader.Open() reads the TOC and throws on an empty tray or a data disc,
// so it cannot answer "what drive is this?" when there is nothing to rip. This
// class talks to Bwg.Scsi.Device directly and issues only the identity/capability
// commands (INQUIRY, GET CONFIGURATION, GET PERFORMANCE, GET EVENT STATUS
// NOTIFICATION), none of which require media. It also exposes real tray control
// (START STOP UNIT eject/load) and a tray/media state query for auto-detect.
//
// Windows-only (SCSI pass-through). Every method is defensive: a missing drive,
// a spun-down unit, or a refused command yields Valid=false + Error, never a throw.
//
// ****************************************************************************

using System;
using System.Collections.Generic;
using Bwg.Logging;
using Bwg.Scsi;

namespace CUETools.Ripper.SCSI
{
    /// <summary>Physical tray/media state as reported by GET EVENT STATUS NOTIFICATION.</summary>
    public enum DriveTrayState
    {
        Unknown = 0,
        ClosedNoDisc = 1,
        Open = 2,
        ClosedWithDisc = 3
    }

    /// <summary>One GET CONFIGURATION feature descriptor, flattened for display.</summary>
    public sealed class DriveFeatureInfo
    {
        public string Name;
        public int Code;
        public bool Current;
        public byte Version;

        public DriveFeatureInfo(string name, int code, bool current, byte version)
        {
            Name = name;
            Code = code;
            Current = current;
            Version = version;
        }
    }

    /// <summary>
    /// A plain-data snapshot of an optical drive: identity, capabilities, speeds, and the
    /// current tray/media state. Built by <see cref="DriveInspector.Query"/>. Consumers (the
    /// WPF app) depend on this class, not on Bwg.Scsi, so the SCSI layer stays encapsulated.
    /// </summary>
    public sealed class DriveCapabilities
    {
        public char Letter;
        public bool Valid;
        public string Error = "";

        // SCSI INQUIRY - drive identity (available with an empty tray)
        public string Vendor = "";
        public string Product = "";
        public string Firmware = "";
        public string ProductRevision = "";
        public byte ScsiVersion;
        public byte ResponseDataFormat;
        public bool Removable;
        public int MaxTransferBytes;

        // GET CONFIGURATION - profiles + features
        public int CurrentProfile;
        public string CurrentProfileName = "";
        public List<string> SupportedProfiles = new List<string>();
        public List<DriveFeatureInfo> Features = new List<DriveFeatureInfo>();

        // derived capability flags (from profiles + the CD Read feature)
        public bool CanReadCD;
        public bool CanReadDVD;
        public bool CanReadBD;
        public bool CanWriteCD;
        public bool CanWriteDVD;
        public bool CanWriteBD;
        public bool C2ErrorPointers;   // CD Read feature, byte 0 bit 1
        public bool CdText;            // CD Read feature, byte 0 bit 0

        // GET PERFORMANCE - fastest advertised read/write (KB/s)
        public int MaxReadKBps;
        public int MaxWriteKBps;

        // GET EVENT STATUS NOTIFICATION - tray + media
        public DriveTrayState Tray = DriveTrayState.Unknown;
        public bool MediaPresent;

        /// <summary>AccurateRip drive-name key: "VENDOR   - Product" (Vendor padded to 8).</summary>
        public string ARName
        {
            get
            {
                string v = Vendor == null ? "" : Vendor.Trim();
                string p = Product == null ? "" : Product.Trim();
                if (v.Length < 8) v = v.PadRight(8, ' ');
                return v + " - " + p;
            }
        }

        /// <summary>"Vendor Product" with single spaces, for a heading.</summary>
        public string DisplayName
        {
            get
            {
                string v = Vendor == null ? "" : Vendor.Trim();
                string p = Product == null ? "" : Product.Trim();
                string s = (v + " " + p).Trim();
                return s.Length == 0 ? "Unknown drive" : s;
            }
        }

        // CD 1x = 176.4 KB/s. Round to the nearest whole multiple for a friendly readout.
        public int MaxReadCdX { get { return MaxReadKBps > 0 ? (int)Math.Round(MaxReadKBps / 176.4) : 0; } }
        public int MaxWriteCdX { get { return MaxWriteKBps > 0 ? (int)Math.Round(MaxWriteKBps / 176.4) : 0; } }
    }

    /// <summary>Static SCSI queries for drive identity, capabilities, and tray control.</summary>
    public static class DriveInspector
    {
        /// <summary>Read the full capability snapshot. Never throws; on failure returns a
        /// DriveCapabilities with Valid=false and Error set.</summary>
        public static DriveCapabilities Query(char drive)
        {
            DriveCapabilities caps = new DriveCapabilities();
            caps.Letter = drive;
            Device dev = null;
            try
            {
                dev = new Device(new Logger());
                if (!dev.Open(drive)) { caps.Error = "cannot open drive"; return caps; }

                InquiryResult inq;
                if (dev.Inquiry(out inq) != Device.CommandStatus.Success || inq == null || !inq.Valid)
                {
                    caps.Error = "INQUIRY failed";
                    return caps;
                }
                caps.Vendor = Safe(inq.VendorIdentification);
                caps.Product = Safe(inq.ProductIdentification);
                caps.Firmware = Safe(inq.FirmwareVersion);
                caps.ProductRevision = Safe(inq.ProductRevision);
                caps.ScsiVersion = inq.Version;
                caps.ResponseDataFormat = inq.ResponseDataFormat;
                caps.Removable = inq.RemovableMediumBit;
                caps.MaxTransferBytes = dev.MaximumTransferLength;
                caps.Valid = true;

                ReadConfiguration(dev, caps);
                ReadSpeeds(dev, caps);
                ReadTrayState(dev, caps);
            }
            catch (Exception ex)
            {
                caps.Error = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                try { if (dev != null) dev.Close(); } catch { }
            }
            return caps;
        }

        /// <summary>Fast tray/media state query (no INQUIRY, no configuration read).</summary>
        public static DriveTrayState GetTray(char drive)
        {
            Device dev = null;
            try
            {
                dev = new Device(new Logger());
                if (!dev.Open(drive)) return DriveTrayState.Unknown;
                DriveCapabilities caps = new DriveCapabilities();
                ReadTrayState(dev, caps);
                return caps.Tray;
            }
            catch { return DriveTrayState.Unknown; }
            finally { try { if (dev != null) dev.Close(); } catch { } }
        }

        /// <summary>True when a disc is loaded and the tray is closed.</summary>
        public static bool IsMediaPresent(char drive)
        {
            return GetTray(drive) == DriveTrayState.ClosedWithDisc;
        }

        /// <summary>Slide the tray out (SCSI START STOP UNIT / eject).</summary>
        public static void OpenTray(char drive) { SetTray(drive, true); }

        /// <summary>Pull the tray in (SCSI START STOP UNIT / load).</summary>
        public static void CloseTray(char drive) { SetTray(drive, false); }

        private static void SetTray(char drive, bool open)
        {
            Device dev = null;
            try
            {
                dev = new Device(new Logger());
                if (!dev.Open(drive)) return;
                dev.StartStopUnit(true, Device.PowerControl.NoChange,
                    open ? Device.StartState.EjectDisk : Device.StartState.LoadDisk);
            }
            catch { /* best-effort tray control */ }
            finally { try { if (dev != null) dev.Close(); } catch { } }
        }

        // --- helpers -----------------------------------------------------------------

        private static void ReadConfiguration(Device dev, DriveCapabilities caps)
        {
            try
            {
                FeatureList list;
                if (dev.GetConfiguration(Device.GetConfigType.AllFeatures, 0, out list) != Device.CommandStatus.Success
                    || list == null)
                    return;

                caps.CurrentProfile = list.Profile;
                caps.CurrentProfileName = ProfileName(list.Profile);

                foreach (Feature f in list.Features)
                {
                    caps.Features.Add(new DriveFeatureInfo(f.Name, (int)f.Code, f.Current, f.Version));

                    if (f.Code == Feature.FeatureType.ProfileList)
                        ParseProfileList(f.Data, caps);
                    else if (f.Code == Feature.FeatureType.CDRead && f.Data != null && f.Data.Length >= 1)
                    {
                        caps.CdText = (f.Data[0] & 0x01) != 0;
                        caps.C2ErrorPointers = (f.Data[0] & 0x02) != 0;
                    }
                }
                DeriveMediaCaps(caps);
            }
            catch { /* leave configuration fields at their defaults */ }
        }

        // ProfileList feature data is a run of 4-byte records: [profile hi][profile lo][flags][rsvd].
        private static void ParseProfileList(byte[] data, DriveCapabilities caps)
        {
            if (data == null) return;
            for (int i = 0; i + 3 < data.Length; i += 4)
            {
                int profile = (data[i] << 8) | data[i + 1];
                string name = ProfileName(profile);
                if (name.Length > 0 && !caps.SupportedProfiles.Contains(name))
                    caps.SupportedProfiles.Add(name);
            }
        }

        private static void DeriveMediaCaps(DriveCapabilities caps)
        {
            foreach (string p in caps.SupportedProfiles)
            {
                if (p.StartsWith("CD")) { caps.CanReadCD = true; if (p != "CD-ROM") caps.CanWriteCD = true; }
                else if (p.StartsWith("DVD") || p.StartsWith("HD DVD"))
                {
                    caps.CanReadDVD = true;
                    if (p.IndexOf('R') >= 0 && p != "DVD-ROM" && p != "HD DVD-ROM") caps.CanWriteDVD = true;
                }
                else if (p.StartsWith("BD"))
                {
                    caps.CanReadBD = true;
                    if (p != "BD-ROM") caps.CanWriteBD = true;
                }
            }
            // A drive that reports no CD profile but does read CDs (older units) still reads CD.
            if (!caps.CanReadCD && caps.CurrentProfileName.StartsWith("CD")) caps.CanReadCD = true;
        }

        private static void ReadSpeeds(Device dev, DriveCapabilities caps)
        {
            try
            {
                SpeedDescriptorList list;
                if (dev.GetSpeed(out list) != Device.CommandStatus.Success || list == null) return;
                foreach (SpeedDescriptor s in list)
                {
                    if (s.ReadSpeed > caps.MaxReadKBps) caps.MaxReadKBps = s.ReadSpeed;
                    if (s.WriteSpeed > caps.MaxWriteKBps) caps.MaxWriteKBps = s.WriteSpeed;
                }
            }
            catch { /* some drives refuse GET PERFORMANCE with no media - leave speeds at 0 */ }
        }

        private static void ReadTrayState(Device dev, DriveCapabilities caps)
        {
            try
            {
                EventStatusNotification status;
                if (dev.GetEventStatusNotification(true, Device.NotificationClass.Media, out status) != Device.CommandStatus.Success
                    || status == null || status.EventData == null || status.EventData.Length < 2)
                    return;

                // Media event data byte 1: bit0 = tray/door open, bit1 = media present.
                byte b = status.EventData[1];
                bool open = (b & 0x01) != 0;
                bool present = (b & 0x02) != 0;
                caps.MediaPresent = present;
                caps.Tray = open ? DriveTrayState.Open
                    : present ? DriveTrayState.ClosedWithDisc
                    : DriveTrayState.ClosedNoDisc;
            }
            catch { /* leave Tray = Unknown */ }
        }

        private static string Safe(string s) { return s == null ? "" : s.Trim(new char[] { ' ', '\0' }); }

        // Common MMC profile numbers -> friendly names.
        private static string ProfileName(int profile)
        {
            switch (profile)
            {
                case 0x0008: return "CD-ROM";
                case 0x0009: return "CD-R";
                case 0x000A: return "CD-RW";
                case 0x0010: return "DVD-ROM";
                case 0x0011: return "DVD-R";
                case 0x0012: return "DVD-RAM";
                case 0x0013: return "DVD-RW";
                case 0x0014: return "DVD-RW";
                case 0x0015: return "DVD-R DL";
                case 0x0016: return "DVD-R DL";
                case 0x001A: return "DVD+RW";
                case 0x001B: return "DVD+R";
                case 0x002A: return "DVD+RW DL";
                case 0x002B: return "DVD+R DL";
                case 0x0040: return "BD-ROM";
                case 0x0041: return "BD-R";
                case 0x0042: return "BD-R";
                case 0x0043: return "BD-RE";
                case 0x0050: return "HD DVD-ROM";
                case 0x0051: return "HD DVD-R";
                case 0x0052: return "HD DVD-RAM";
                case 0x0000: return "";
                default: return "";
            }
        }
    }
}
