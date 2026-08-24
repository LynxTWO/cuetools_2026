namespace CUETools.Wpf.Services;

// Mutation-only compile seam. Test-MutationHarness.ps1 verifies both constants against their
// production declarations before Stryker runs.
public static class RepairEvidence
{
    public const string ReceiptFileName = "repair.verify";
}

public static class AlbumOutputTransaction
{
    public const string CompletionMarkerName = ".cuetools-complete";
}
