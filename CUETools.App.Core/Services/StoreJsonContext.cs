using System.Collections.Generic;
using System.Text.Json.Serialization;
using CUETools.Wpf.Accuracy;

namespace CUETools.Wpf.Services;

/// <summary>
/// Source-generated serializer context for every root type GzJson persists
/// (the calibration table, the verify history, and the rip history rows).
/// Reflection-based System.Text.Json is unavailable under trimmed/AOT heads,
/// and a PublishAot project disables it even in JIT builds, so GzJson
/// resolves type info here first; hosts with dynamic code keep a reflection
/// fallback for any type outside this closed set.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, DriveCalibration>))]
[JsonSerializable(typeof(Dictionary<string, List<VerifyRecord>>))]
[JsonSerializable(typeof(List<HistoryStore.Row>))]
[JsonSerializable(typeof(Dictionary<string, List<DriveRecoveryIncident>>))]
internal sealed partial class StoreJsonContext : JsonSerializerContext
{
}
