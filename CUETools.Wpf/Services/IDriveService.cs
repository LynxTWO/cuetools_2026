using System;
using System.Collections.Generic;
using CUETools.Ripper.SCSI;
using CUETools.Wpf.Models;

namespace CUETools.Wpf.Services;

/// <summary>Wraps optical-drive enumeration and TOC reading (the CUETools.Ripper SCSI stack).</summary>
public interface IDriveService
{
    IReadOnlyList<char> GetDrives();

    /// <summary>The drive the user is working with, shared across pages for this session (not persisted).
    /// '\0' until a page sets it. The Rip page sets it from its own picker; the Drive &amp; Read page
    /// detects and CALIBRATES this drive. Both used to act on GetDrives()[0] independently, so on a
    /// two-drive machine you could calibrate drive 1 while ripping drive 2 - the rip's lookup by drive
    /// signature then found nothing and cache defeat was silently skipped, quietly weakening the
    /// "secure re-read" guarantee. One shared value makes that mismatch impossible.</summary>
    char SelectedDrive { get; set; }

    /// <summary>True while a rip, verify or Test &amp; Copy holds the drive open. The Drive &amp; Read page
    /// must not Detect or Calibrate then: the ripper's handle is opened with FILE_SHARE_READ only, so the
    /// probe fails on a sharing violation and the page reported it as "needs a disc" - advice that invites
    /// the user to open the tray DURING a rip, which the rip code itself warns can crash the drive layer.
    /// The mirror case is just as bad: starting a rip during a calibration makes the rip throw.</summary>
    bool RipInProgress { get; }

    /// <summary>Open the drive and read its table of contents, or null if there is no
    /// readable audio disc (empty tray, data disc, or drive not ready). <paramref name="onStatus"/>
    /// reports the metadata-lookup step live ("Looking up album via CTDB...", "...via Freedb...").</summary>
    DiscInfo? ReadDisc(char drive, Action<string>? onStatus = null);

    /// <summary>Everything the drive reports about itself (identity, capabilities, speeds) plus the
    /// AccurateRip read offset. Works with an empty tray. Blocking SCSI - call off the UI thread.</summary>
    DriveDetails GetDriveDetails(char drive);

    /// <summary>Physical tray/media state (open, closed-empty, closed-with-disc). Fast SCSI query.</summary>
    DriveTrayState GetTrayState(char drive);

    /// <summary>Open the drive tray (works with or without a disc loaded).</summary>
    void OpenTray(char drive);

    /// <summary>Close the drive tray / load the disc.</summary>
    void CloseTray(char drive);
}
