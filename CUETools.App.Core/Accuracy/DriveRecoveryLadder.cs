using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Accuracy;

public enum DriveRecoveryLadderState
{
    /// <summary>Waiting for the human to perform the current rung.</summary>
    AwaitingUser = 0,
    Verifying,
    Cured,
    Uncured,
    /// <summary>Terminal, and NOT a rung failure: the probe could not open the
    /// device for lack of permission, so no rung was ever tested.</summary>
    PermissionsBlocked,
    /// <summary>Terminal: dismissed before any verdict.</summary>
    Abandoned,
}

/// <summary>
/// The guided physical recovery ladder (SLICE-011, D-060/D-061/D-062).
///
/// The human performs each rung (unplug the cable, then cut power); this class
/// owns only the order, the verdicts, and the incident record. It holds no
/// reference to the failed transaction by design: D-062 says a cured drive gets
/// a fresh run, never a resumed one, and a state machine that cannot see the
/// old operation cannot accidentally resume it.
///
/// It also owns no clock. All waiting lives in the probe, so this class stays
/// deterministic under test and cannot become timing-flaky in CI.
/// </summary>
public sealed class DriveRecoveryLadder
{
    private readonly string _driveSignature;
    private readonly string _trigger;
    private readonly string _triggerContext;
    private readonly IDriveRecoveryProbe _probe;
    private readonly DriveRecoveryIncidentStore? _store;
    private readonly List<string> _rungOrder;
    private readonly List<string> _rungsAttempted = new();

    private DriveRecoveryFingerprint? _fingerprint;
    private int _rungIndex;
    private bool _incidentWritten;

    public DriveRecoveryLadder(
        string driveSignature,
        char drive,
        string trigger,
        string triggerContext,
        IDriveRecoveryProbe probe,
        DriveRecoveryIncidentStore? store)
    {
        _driveSignature = (driveSignature ?? "").Trim();
        Drive = char.ToUpperInvariant(drive);
        _trigger = trigger ?? "";
        _triggerContext = triggerContext ?? "";
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        // An empty signature cannot key the store: run the ladder, persist
        // nothing, rather than create an unkeyed bucket.
        _store = _driveSignature.Length > 0 ? store : null;

        IReadOnlyList<DriveRecoveryIncident> history = Array.Empty<DriveRecoveryIncident>();
        if (_store != null)
        {
            try
            {
                history = _store.GetHistory(_driveSignature);
            }
            catch (InvalidDataException)
            {
                // A corrupt store must not strand a user with a stuck drive. Use
                // the default order and say so; never repair, rename, or delete
                // the file - the store's own contract left it unchanged.
                HistoryUnreadable = true;
            }
        }
        _rungOrder = new List<string>(RecoveryLadderPolicy.RungOrder(history));
        ProvenCure = RecoveryLadderPolicy.ProvenCure(history) ?? "";
    }

    public char Drive { get; }
    public IReadOnlyList<string> RungOrder => _rungOrder;
    public IReadOnlyList<string> RungsAttempted => _rungsAttempted;
    public DriveRecoveryLadderState State { get; private set; } = DriveRecoveryLadderState.AwaitingUser;
    public string CuringRung { get; private set; } = "";
    /// <summary>The rung this drive's history already proved, or "" - the dialog
    /// uses it to explain why the order leads where it does.</summary>
    public string ProvenCure { get; }
    public char ResolvedDrive { get; private set; }
    public string LastDetail { get; private set; } = "";
    public bool HistoryUnreadable { get; }
    /// <summary>False when a cure could not be remembered (no signature, or the
    /// store refused the write). The dialog says so rather than implying memory.</summary>
    public bool IncidentRecorded { get; private set; }

    public string? CurrentRung =>
        _rungIndex >= 0 && _rungIndex < _rungOrder.Count ? _rungOrder[_rungIndex] : null;

    public bool IsTerminal =>
        State == DriveRecoveryLadderState.Cured ||
        State == DriveRecoveryLadderState.Uncured ||
        State == DriveRecoveryLadderState.PermissionsBlocked ||
        State == DriveRecoveryLadderState.Abandoned;

    /// <summary>
    /// Capture the drive's identity before the user touches the hardware. False
    /// when identity cannot be established: without it a returning drive cannot
    /// be told apart from its neighbours, and probing the wrong one could report
    /// a cure for a drive that is still wedged.
    /// </summary>
    public bool Begin()
    {
        _fingerprint = _probe.Snapshot(Drive);
        return _fingerprint != null;
    }

    /// <summary>
    /// Verify the rung the user just performed. Advances to the next rung on a
    /// negative verdict, ends the ladder when none remain.
    /// </summary>
    public async Task<DriveRecoveryLadderState> VerifyCurrentRungAsync(
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (IsTerminal || CurrentRung == null)
            return State;
        if (_fingerprint == null && !Begin())
        {
            // No identity: report honestly rather than probing a guess.
            LastDetail = "drive identity unavailable";
            State = DriveRecoveryLadderState.Uncured;
            WriteIncident();
            return State;
        }

        string rung = CurrentRung!;
        _rungsAttempted.Add(rung);
        State = DriveRecoveryLadderState.Verifying;

        DriveRecoveryProbeReport report;
        try
        {
            report = await _probe.VerifyRungAsync(_fingerprint!, timeout, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A probe that failed is not a cure. Keep the ladder usable.
            report = new DriveRecoveryProbeReport
            {
                Result = DriveRecoveryProbeResult.Unverified,
                Detail = "probe failed: " + ex.GetType().Name,
            };
        }

        LastDetail = report.Detail ?? "";

        switch (report.Result)
        {
            case DriveRecoveryProbeResult.Responsive:
            case DriveRecoveryProbeResult.NoDisc:
                CuringRung = rung;
                ResolvedDrive = report.ResolvedDrive;
                State = DriveRecoveryLadderState.Cured;
                WriteIncident();
                return State;

            case DriveRecoveryProbeResult.PermissionDenied:
                // No rung was actually tested. Recording an uncured incident
                // here would break this drive's proven-cure streak over a
                // permissions problem, so nothing is persisted.
                State = DriveRecoveryLadderState.PermissionsBlocked;
                return State;

            default:
                _rungIndex++;
                if (CurrentRung == null)
                {
                    State = DriveRecoveryLadderState.Uncured;
                    WriteIncident();
                }
                else
                {
                    State = DriveRecoveryLadderState.AwaitingUser;
                }
                return State;
        }
    }

    /// <summary>
    /// Jump to any rung in the ladder (D-061 keeps every rung reachable even
    /// when a proven cure leads). Ignored once terminal or for an unknown rung.
    /// </summary>
    public bool SkipToRung(string rung)
    {
        if (IsTerminal)
            return false;
        int index = _rungOrder.IndexOf(rung);
        if (index < 0)
            return false;
        _rungIndex = index;
        State = DriveRecoveryLadderState.AwaitingUser;
        return true;
    }

    /// <summary>Dismissed before a verdict. Recorded, because an abandoned
    /// ladder is still an incident this drive had.</summary>
    public void Abandon()
    {
        if (IsTerminal)
            return;
        State = DriveRecoveryLadderState.Abandoned;
        WriteIncident();
    }

    private void WriteIncident()
    {
        if (_incidentWritten || _store == null)
            return;
        _incidentWritten = true;
        try
        {
            _store.Append(_driveSignature, new DriveRecoveryIncident
            {
                TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Trigger = _trigger,
                TriggerContext = _triggerContext,
                RungsAttempted = new List<string>(_rungsAttempted),
                CuringRung = CuringRung,
            });
            IncidentRecorded = true;
        }
        catch (InvalidDataException)
        {
            // The cure verdict stands; only the memory of it is lost.
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
