namespace CUETools.Processor;

// Mutation-only compile seam. The harness contract gate checks these calculated members against
// CUEMetadata.cs, and Stryker excludes this file from mutation.
public sealed class CUEMetadata
{
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Year { get; set; } = "";
    public string DiscNumber { get; set; } = "";
    public string TotalDiscs { get; set; } = "";
    public string DiscName { get; set; } = "";

    public string DiscNumber01
    {
        get
        {
            if (uint.TryParse(TotalDiscs, out uint total) &&
                uint.TryParse(DiscNumber, out uint disc) &&
                total > 9 && disc > 0)
                return disc.ToString("00");
            return DiscNumber;
        }
    }

    public string DiscNumberAndTotal =>
        TotalDiscs != "" && TotalDiscs != "1"
            ? DiscNumber01 + "/" + TotalDiscs
            : DiscNumber != "" && DiscNumber != "1" ? DiscNumber01 : "";

    public string DiscNumberAndName =>
        DiscNumberAndTotal == ""
            ? ""
            : DiscNumberAndTotal + (DiscName != "" ? " - " + DiscName : "");
}
