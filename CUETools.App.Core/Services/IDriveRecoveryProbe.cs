using System;
using System.Threading;
using System.Threading.Tasks;

namespace CUETools.Wpf.Services;

/// <summary>
/// One rung verification's verdict (SLICE-011). Unverified is deliberately the
/// default value: a platform with no implementation, a dismissed dialog, and a
/// probe that could not decide all report it, and the ladder then records the
/// incident as uncured. Reporting a cure on an unimplemented path would tell a
/// user their still-wedged drive is fixed.
/// </summary>
public enum DriveRecoveryProbeResult
{
    Unverified = 0,
    /// <summary>The drive answered, so it is alive.</summary>
    Responsive,
    /// <summary>The drive answered but reports no disc. Still a cure: the ladder
    /// is about the drive, not the media.</summary>
    NoDisc,
    /// <summary>The drive is present and still rejecting the probe command.</summary>
    StillUnresponsive,
    /// <summary>The device node could not be opened for lack of permission. Not a
    /// wedge verdict, and never a rung failure.</summary>
    PermissionDenied,
    /// <summary>No device node matched the fingerprint before the timeout.</summary>
    DeviceAbsent,
}

/// <summary>Result of verifying one recovery rung. Scrubbed: hardware identity
/// and errno only, never disc or album content.</summary>
public sealed class DriveRecoveryProbeReport
{
    public DriveRecoveryProbeResult Result { get; init; }
    /// <summary>The letter the drive answers on now; '\0' when unresolved. A drive
    /// can return from a replug at a different sr node, which moves its letter.</summary>
    public char ResolvedDrive { get; init; }
    /// <summary>True when the device disappeared and came back during the watch.</summary>
    public bool ReEnumerated { get; init; }
    public string Detail { get; init; } = "";
}

/// <summary>
/// A drive's unprivileged identity, captured BEFORE the user touches the
/// hardware so the same mechanism can be found again if it returns at a
/// different device node.
/// </summary>
public sealed class DriveRecoveryFingerprint
{
    public char Letter { get; init; }
    public string SrNode { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Model { get; init; } = "";
    public string Revision { get; init; } = "";
    public string Serial { get; init; } = "";

    /// <summary>True when enough identity exists to re-find this drive among others.</summary>
    public bool IsIdentifiable =>
        Vendor.Length > 0 || Model.Length > 0 || Serial.Length > 0;
}

/// <summary>
/// Verifies a guided recovery rung by observing the drive (SLICE-011, D-060).
/// The app never resets hardware and never elevates: the human performs the
/// physical rung and this seam reports only what the drive does afterwards.
///
/// This is an OBSERVATION, not an operation (ADC-CUETOOLS-009): a platform with
/// no implementation reports Unverified and the ladder records the incident as
/// uncured. It must never throw - its caller is a dialog - and it must never
/// report a cure it did not observe.
/// </summary>
public interface IDriveRecoveryProbe
{
    /// <summary>False where no implementation exists. The recovery affordance is
    /// offered only when this is true, so no head shows a dialog whose rungs can
    /// never be verified.</summary>
    bool CanVerify { get; }

    /// <summary>Identity snapshot, or null when identity cannot be established.</summary>
    DriveRecoveryFingerprint? Snapshot(char drive);

    /// <summary>
    /// Wait for the fingerprinted drive to return and answer a table-of-contents
    /// read, or until the timeout expires. All waiting lives here, so the ladder
    /// advances only on a returned verdict and owns no clock of its own.
    /// </summary>
    Task<DriveRecoveryProbeReport> VerifyRungAsync(
        DriveRecoveryFingerprint fingerprint,
        TimeSpan timeout,
        CancellationToken ct = default);
}

/// <summary>
/// The honest no-op for platforms with no probe implementation (Windows this
/// slice, macOS entirely). Reports that it cannot verify, so the affordance
/// never appears, and returns Unverified if called anyway.
/// </summary>
public sealed class UnsupportedDriveRecoveryProbe : IDriveRecoveryProbe
{
    public bool CanVerify => false;

    public DriveRecoveryFingerprint? Snapshot(char drive) => null;

    public Task<DriveRecoveryProbeReport> VerifyRungAsync(
        DriveRecoveryFingerprint fingerprint,
        TimeSpan timeout,
        CancellationToken ct = default) =>
        Task.FromResult(new DriveRecoveryProbeReport
        {
            Result = DriveRecoveryProbeResult.Unverified,
            Detail = "no recovery probe on this platform",
        });
}
