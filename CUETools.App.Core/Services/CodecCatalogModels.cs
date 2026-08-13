using System;
using CUETools.Codecs;

namespace CUETools.Wpf.Services;

public enum CodecReadiness
{
    Ready,
    SetupRequired,
    LoadFailed
}

public sealed class CodecHealth
{
    public static CodecHealth Ready(string origin, string detail = "") => new()
    {
        Readiness = CodecReadiness.Ready,
        Origin = origin,
        Detail = detail,
    };

    public static CodecHealth SetupRequired(string origin, string detail) => new()
    {
        Readiness = CodecReadiness.SetupRequired,
        Origin = origin,
        Detail = detail,
    };

    public static CodecHealth LoadFailed(string origin, string detail) => new()
    {
        Readiness = CodecReadiness.LoadFailed,
        Origin = origin,
        Detail = detail,
    };

    public CodecReadiness Readiness { get; init; }
    public string Origin { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool IsReady => Readiness == CodecReadiness.Ready;
    public string Status => Readiness switch
    {
        CodecReadiness.Ready => "Ready",
        CodecReadiness.SetupRequired => "Setup required",
        _ => "Load failed",
    };
}

/// <summary>
/// One concrete format face and encoder implementation. The extension remains the engine contract;
/// lossless/lossy and implementation identity make the user's selection unambiguous.
/// </summary>
public sealed class CodecChoice
{
    internal AudioEncoderSettingsViewModel Encoder { get; init; } = null!;

    public string StableId { get; init; } = "";
    public string Extension { get; init; } = "";
    public string ExtensionLabel => "." + Extension;
    public string FormatName { get; init; } = "";
    public string Implementation { get; init; } = "";
    public bool Lossless { get; init; }
    public string Category => Lossless ? "Lossless" : "Lossy";
    public int CategoryOrder => Lossless ? 0 : 1;
    public CodecHealth Health { get; init; } = new();
    public bool CanSelect => Health.IsReady;
    public string Status => Health.Status;
    public string Origin => Health.Origin;
    public string HealthDetail => Health.Detail;
    public string Description { get; init; } = "";
    public string History { get; init; } = "";
    public string BestUse { get; init; } = "";
    public string Distribution { get; init; } = "";
    public int RecommendedRank { get; init; }
    public int CompressionRank { get; init; }
    public int EfficiencyRank { get; init; }
    public string CompactLabel => FormatName + " (" + ExtensionLabel + ")";
    public string AccessibleLabel => CompactLabel + ", " + Implementation + ", " + Status;
}
