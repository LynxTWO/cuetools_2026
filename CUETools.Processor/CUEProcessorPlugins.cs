using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
                string manifestPath = Path.Combine(
                    plugins_path, PluginTrustManifest.ManifestFileName);
                if (File.Exists(manifestPath))
                {
                    // Manifest validation is deliberately outside a best-effort catch. A packaged
                    // plugin trust failure must be visible instead of looking like an optional
                    // codec that happened not to load.
                    string arch = PluginTrustManifest.GetRuntimeArchitecture();
                    IList<ApprovedPlugin> approvedPlugins =
                        PluginTrustManifest.ReadApprovedPlugins(plugins_path);
                    PluginTrustManifest.PreloadApprovedNativeDependencies(
                        approvedPlugins, arch);
                    foreach (ApprovedPlugin plugin in approvedPlugins)
                    {
                        if (!PluginTrustManifest.IsForRuntimeArchitecture(plugin, arch))
                            continue;
                        if (!PluginTrustManifest.IsLoadableManagedPlugin(plugin))
                            continue;
                        Assembly assembly = PluginTrustManifest.LoadApprovedAssembly(plugin);
                        AddPlugin(assembly, plugin.RelativePath);
                    }
                }
                else if (PluginTrustManifest.IsLocalDevelopmentModeEnabled())
                {
                    System.Diagnostics.Trace.WriteLine(
                        "WARNING: loading unmanifested plugins because " +
                        PluginTrustManifest.LocalDevelopmentEnvironmentVariable + "=1");
                    AddPluginDirectory(plugins_path);
                    string arch = PluginTrustManifest.GetRuntimeArchitecture();
                    string archPath = Path.Combine(plugins_path, arch);
                    if (Directory.Exists(archPath))
                        AddPluginDirectory(archPath);
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine(
                        "Skipped unmanifested plugin directory. Set " +
                        PluginTrustManifest.LocalDevelopmentEnvironmentVariable +
                        "=1 only for local development.");
                }
            }
        }

        private static void AddPluginDirectory(string plugins_path)
        {
            // Loose directory enumeration is reachable only through the explicit local-development
            // switch. Packaged paths use PluginTrustManifest and fail closed before loading.
            foreach (string plugin_path in Directory.GetFiles(plugins_path, "CUETools.*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    AddPlugin(plugin_path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        "Unmanifested development plugin load failed (" +
                        ex.GetType().Name + ").");
                }
            }
        }
  
        // Plugin types run in-process with the app's privileges. The packaged path binds bytes to
        // the build-generated allowlist, but that hash manifest is not a publisher signature and
        // cannot protect an install tree whose plugin and manifest files are both writable by an
        // attacker. Do not widen discovery or add downloads without a separate authenticity policy.
        private static void AddPlugin(string plugin_path)
        {
            // Load from the explicit path (backlog R16): on .NET (Core) the default load context
            // does not probe the plugins subdirectory, so Assembly.Load(name) threw
            // FileNotFoundException for a DLL living only under plugins\ and the codec silently
            // failed to register (falling back to an absent external exe at encode time). LoadFrom
            // keeps working on .NET Framework; if the same assembly identity is already loaded from
            // the app base (a project reference), LoadFrom returns that one - no duplicate types.
            Assembly assembly = Assembly.LoadFrom(plugin_path);
            AddPlugin(assembly, Path.GetFileName(plugin_path));
        }

        private static void AddPlugin(Assembly assembly, string displayPath)
        {
            System.Diagnostics.Trace.WriteLine(
                "Loaded plugin " + displayPath + " (" + assembly.FullName + ")");
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
                    System.Diagnostics.Trace.WriteLine(
                        "Plugin type registration failed (" +
                        ex.GetType().Name + ").");
                }
            }
        }
    }
}
