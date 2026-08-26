using System.Collections.Generic;
using System.Text.Json.Serialization;
using CUETools.Wpf.Accuracy;

namespace CUETools.Wpf.Services
{
    /// <summary>
    /// Dependency contract for the source-generated serializer context. Production declares this in
    /// CUETools.App.Core\Services\StoreJsonContext.cs and also registers the calibration, rip-history,
    /// and drive-recovery roots. Linking that file here would pull HistoryStore and CUETools.Wpf.Models
    /// into the isolated graph, which is exactly what the mutation profiles exist to avoid.
    /// This contract registers only the two roots the linked production sources resolve through it:
    /// VerifyHistory writes a single record with StoreJsonContext.Default.VerifyRecord, and the store
    /// round-trips Dictionary&lt;string, List&lt;VerifyRecord&gt;&gt;. Test-MutationHarness.ps1 pins the
    /// production declaration and both registrations before any mutant runs.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(VerifyRecord))]
    [JsonSerializable(typeof(Dictionary<string, List<VerifyRecord>>))]
    internal sealed partial class StoreJsonContext : JsonSerializerContext
    {
    }
}
