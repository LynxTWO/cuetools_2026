using System;
using System.Collections.Generic;
using CUETools.Ripper.SCSI;

namespace CUETools.Wpf.Models;

/// <summary>
/// Bindable view of everything the drive reports about itself (from <see cref="DriveInspector"/>)
/// plus the AccurateRip read offset. WPF binds to properties, not fields, so this wraps the
/// plain-data <see cref="DriveCapabilities"/> and adds the offset and a few display strings.
/// Built with no disc required - identity and capabilities come from INQUIRY / GET CONFIGURATION.
/// </summary>
public sealed class DriveDetails
{
    public bool Valid { get; init; }
    public string Error { get; init; } = "";
    public char Letter { get; init; }

    public string Vendor { get; init; } = "";
    public string Product { get; init; } = "";
    public string Model { get; init; } = "";            // "ASUS BW-16D1HT"
    public string Firmware { get; init; } = "";
    public string ProductRevision { get; init; } = "";
    public string ARName { get; init; } = "";
    public byte ScsiVersion { get; init; }
    public bool Removable { get; init; }
    public int MaxTransferBytes { get; init; }

    public int Offset { get; init; }
    public bool OffsetKnown { get; init; }
    public string OffsetText => OffsetKnown ? (Offset >= 0 ? "+" + Offset : Offset.ToString()) : "--";

    public string CurrentProfile { get; init; } = "";
    public IReadOnlyList<string> SupportedProfiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DriveFeatureRow> Features { get; init; } = Array.Empty<DriveFeatureRow>();

    public bool CanReadCD { get; init; }
    public bool CanReadDVD { get; init; }
    public bool CanReadBD { get; init; }
    public bool CanWriteCD { get; init; }
    public bool CanWriteDVD { get; init; }
    public bool CanWriteBD { get; init; }
    public bool C2ErrorPointers { get; init; }
    public bool CdText { get; init; }

    public int MaxReadKBps { get; init; }
    public int MaxReadCdX { get; init; }
    public int MaxWriteKBps { get; init; }
    public int MaxWriteCdX { get; init; }

    public DriveTrayState Tray { get; init; }
    public bool MediaPresent { get; init; }

    // --- display helpers -------------------------------------------------------------

    public string DriveLetterText => Letter == '\0' ? "no drive" : Letter + ":";

    public string ReadsSummary => Join(CanReadCD ? "CD" : null, CanReadDVD ? "DVD" : null, CanReadBD ? "Blu-ray" : null, "reads nothing");
    public string WritesSummary => Join(CanWriteCD ? "CD" : null, CanWriteDVD ? "DVD" : null, CanWriteBD ? "Blu-ray" : null, "read-only");

    public string ReadSpeedText => MaxReadCdX > 0 ? "~" + MaxReadCdX + "x CD  (" + MaxReadKBps + " KB/s)" : "not reported";
    public string WriteSpeedText => MaxWriteCdX > 0 ? "~" + MaxWriteCdX + "x CD  (" + MaxWriteKBps + " KB/s)" : "not reported";
    public string MaxTransferText => MaxTransferBytes > 0 ? (MaxTransferBytes / 1024) + " KB" : "unknown";

    public string TrayText => Tray switch
    {
        DriveTrayState.Open => "tray open",
        DriveTrayState.ClosedWithDisc => "disc loaded",
        DriveTrayState.ClosedNoDisc => "closed, empty",
        _ => "unknown"
    };

    private static string Join(string a, string b, string c, string none)
    {
        var parts = new List<string>(3);
        if (a != null) parts.Add(a);
        if (b != null) parts.Add(b);
        if (c != null) parts.Add(c);
        return parts.Count == 0 ? none : string.Join(", ", parts);
    }

    /// <summary>Map the SCSI-layer snapshot to the bindable model, folding in the AR offset.</summary>
    public static DriveDetails From(DriveCapabilities c, int offset, bool offsetKnown)
    {
        var features = new List<DriveFeatureRow>();
        foreach (var f in c.Features)
            features.Add(new DriveFeatureRow { Name = Pretty(f.Name), Current = f.Current, CodeHex = "0x" + f.Code.ToString("X4") });

        return new DriveDetails
        {
            Valid = c.Valid,
            Error = c.Error,
            Letter = c.Letter,
            Vendor = c.Vendor,
            Product = c.Product,
            Model = c.DisplayName,
            Firmware = c.Firmware,
            ProductRevision = c.ProductRevision,
            ARName = c.ARName,
            ScsiVersion = c.ScsiVersion,
            Removable = c.Removable,
            MaxTransferBytes = c.MaxTransferBytes,
            Offset = offset,
            OffsetKnown = offsetKnown,
            CurrentProfile = c.CurrentProfileName,
            SupportedProfiles = c.SupportedProfiles,
            Features = features,
            CanReadCD = c.CanReadCD,
            CanReadDVD = c.CanReadDVD,
            CanReadBD = c.CanReadBD,
            CanWriteCD = c.CanWriteCD,
            CanWriteDVD = c.CanWriteDVD,
            CanWriteBD = c.CanWriteBD,
            C2ErrorPointers = c.C2ErrorPointers,
            CdText = c.CdText,
            MaxReadKBps = c.MaxReadKBps,
            MaxReadCdX = c.MaxReadCdX,
            MaxWriteKBps = c.MaxWriteKBps,
            MaxWriteCdX = c.MaxWriteCdX,
            Tray = c.Tray,
            MediaPresent = c.MediaPresent
        };
    }

    // Split the CamelCase SCSI feature names into readable words ("CDRead" -> "CD Read").
    private static string Pretty(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char ch = name[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(name[i - 1]) && name[i - 1] != '_') sb.Append(' ');
            sb.Append(ch == '_' ? ' ' : ch);
        }
        return sb.ToString();
    }
}

/// <summary>One GET CONFIGURATION feature row for the drive-info screen.</summary>
public sealed class DriveFeatureRow
{
    public string Name { get; init; } = "";
    public bool Current { get; init; }
    public string CodeHex { get; init; } = "";
}
