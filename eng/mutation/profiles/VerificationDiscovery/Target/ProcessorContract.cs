using System;
using System.Collections.Generic;

namespace CUETools.Processor;

// Mutation-only compile seam for the optional configured-format path. The discovery tests use
// this shape directly, and the harness contract gate verifies the production member names.
public sealed class CUEConfig
{
    public Dictionary<string, CUEToolsFormat> formats { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CUEToolsFormat
{
    public bool allowLossless;
    public MutationDecoder? decoder;
}

public sealed class MutationDecoder
{
    public bool IsValid { get; set; }
}
