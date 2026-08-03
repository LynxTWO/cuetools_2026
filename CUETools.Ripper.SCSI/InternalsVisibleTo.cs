using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TestRipper")]
// The modern ripper suite exercises the same shipping vote helper, so concealment's per-byte
// confidence map is asserted against the production implementation rather than a copy (R119).
[assembly: InternalsVisibleTo("CUETools.Ripper.Tests")]
