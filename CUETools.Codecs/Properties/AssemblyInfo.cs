using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CUETools.TestCodecs")]
[assembly: InternalsVisibleTo("CUETools.Processor")]
[assembly: InternalsVisibleTo("CUETools.Wpf.Tests")]
// The native codec wrappers preload through the shared NativePreload helper.
[assembly: InternalsVisibleTo("CUETools.Codecs.libFLAC")]
[assembly: InternalsVisibleTo("CUETools.Codecs.libwavpack")]
[assembly: InternalsVisibleTo("CUETools.Codecs.MACLib")]
