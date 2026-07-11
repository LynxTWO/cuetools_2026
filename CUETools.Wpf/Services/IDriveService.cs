using System;
using System.Collections.Generic;
using CUETools.Wpf.Models;

namespace CUETools.Wpf.Services;

/// <summary>Wraps optical-drive enumeration and TOC reading (the CUETools.Ripper SCSI stack).</summary>
public interface IDriveService
{
    IReadOnlyList<char> GetDrives();

    /// <summary>Open the drive and read its table of contents, or null if there is no
    /// readable audio disc (empty tray, data disc, or drive not ready). <paramref name="onStatus"/>
    /// reports the metadata-lookup step live ("Looking up album via CTDB...", "...via Freedb...").</summary>
    DiscInfo? ReadDisc(char drive, Action<string>? onStatus = null);
}
