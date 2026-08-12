namespace CUETools.Codecs
{
    /// <summary>
    /// Encoder settings whose encoder can re-decode and verify its own output
    /// during encoding. The lossless-verification migration in CUEConfig sets
    /// this through the interface instead of reflecting on per-codec property
    /// names (historically DoVerify or Verify), which broke under trimmed and
    /// AOT hosts where unreferenced property members are removed. Implementers
    /// map this explicitly onto their existing serialized property so the
    /// settings serialization surface is unchanged.
    /// </summary>
    public interface IVerifyOnEncodeSettings
    {
        bool VerifyOnEncode { get; set; }
    }
}
