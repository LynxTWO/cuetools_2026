namespace CUETools.Codecs
{
    /// <summary>
    /// Encoder settings that expose an "extra processing" effort level beyond
    /// the base compression mode (WavPack's -x1..-x6). The archival-defaults
    /// pass in the encoder catalog raises this to the implementer's maximum
    /// through the interface instead of naming the codec type, so the shared
    /// app core does not need a reference to each codec assembly. Implementers
    /// map this onto their existing serialized property so the settings
    /// serialization surface is unchanged.
    /// </summary>
    public interface IExtraModeSettings
    {
        int ExtraMode { get; set; }

        /// <summary>The largest valid ExtraMode for this encoder.</summary>
        int MaxExtraMode { get; }
    }
}
