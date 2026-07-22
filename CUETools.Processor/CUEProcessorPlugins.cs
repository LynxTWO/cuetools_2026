using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CUETools.Codecs;
using CUETools.Compression;
using CUETools.Ripper;

namespace CUETools.Processor
{
    public static class CUEProcessorPlugins
    {
        public static List<IAudioEncoderSettings> encs;
        public static List<IAudioDecoderSettings> decs;
        public static List<Type> arcp;
        public static List<string> arcp_fmt;
        public static Type hdcd;
        public static Type ripper;

        static CUEProcessorPlugins()
        {
            encs = new List<IAudioEncoderSettings>();
            decs = new List<IAudioDecoderSettings>();
            arcp = new List<Type>();
            arcp_fmt = new List<string>();

            encs.Add(new Codecs.WAV.EncoderSettings());
            decs.Add(new Codecs.WAV.DecoderSettings());

            //ApplicationSecurityInfo asi = new ApplicationSecurityInfo(AppDomain.CurrentDomain.ActivationContext);
            //string arch = asi.ApplicationId.ProcessorArchitecture;
            //ActivationContext is null most of the time :(

            string plugins_path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "plugins");
            if (Directory.Exists(plugins_path))
            {
                AddPluginDirectory(plugins_path);
                string arch = Type.GetType("Mono.Runtime", false) != null ? "mono" : Marshal.SizeOf(typeof(IntPtr)) == 8 ? "x64" : "win32";
                plugins_path = Path.Combine(plugins_path, arch);
                if (Directory.Exists(plugins_path))
                    AddPluginDirectory(plugins_path);
            }
        }

        private static void AddPluginDirectory(string plugins_path)
        {
            foreach (string plugin_path in Directory.GetFiles(plugins_path, "CUETools.*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    AddPlugin(plugin_path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(ex.Message);
                }
            }
        }
  
        // Trust boundary: every CUETools.*.dll in the plugins folder is loaded and its
        // exported types are instantiated by reflection, with no signature, publisher, or
        // origin check. Anything that can write to <exe>\plugins runs in-process with the
        // app's privileges. Load failures are swallowed to Trace on purpose so one bad DLL
        // does not break startup, which also means a tampered plugin fails silently. Do not
        // widen the search beyond CUETools.*.dll or add auto-download without gating this.
        private static void AddPlugin(string plugin_path)
        {
            // Load from the explicit path (backlog R16): on .NET (Core) the default load context
            // does not probe the plugins subdirectory, so Assembly.Load(name) threw
            // FileNotFoundException for a DLL living only under plugins\ and the codec silently
            // failed to register (falling back to an absent external exe at encode time). LoadFrom
            // keeps working on .NET Framework; if the same assembly identity is already loaded from
            // the app base (a project reference), LoadFrom returns that one - no duplicate types.
            Assembly assembly = Assembly.LoadFrom(plugin_path);
            System.Diagnostics.Trace.WriteLine("Loaded " + assembly.FullName);
            foreach (Type type in assembly.GetExportedTypes())
            {
                try
                {
                    if (!type.IsClass || type.IsAbstract) continue;
                    if (type.GetInterface(typeof(IAudioDecoderSettings).Name) != null)
                    {
                        decs.Add(Activator.CreateInstance(type) as IAudioDecoderSettings);
                    }
                    if (type.GetInterface(typeof(IAudioEncoderSettings).Name) != null)
                    {
                        encs.Add(Activator.CreateInstance(type) as IAudioEncoderSettings);
                    }
                    CompressionProviderClass archclass = Attribute.GetCustomAttribute(type, typeof(CompressionProviderClass)) as CompressionProviderClass;
                    if (archclass != null)
                    {
                        arcp.Add(type);
                        if (!arcp_fmt.Contains(archclass.Extension))
                            arcp_fmt.Add(archclass.Extension);
                    }
                    if (type.Name == "HDCDDotNet")
                    {
                        hdcd = type;
                    }
                    if (type.GetInterface(typeof(ICDRipper).Name) != null)
                    {
                        ripper = type;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(ex.Message);
                }
            }
        }
    }
}
