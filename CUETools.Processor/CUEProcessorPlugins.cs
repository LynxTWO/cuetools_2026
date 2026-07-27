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
        private static readonly object outputVerificationTypesGate =
            new object();
        private static readonly HashSet<Type> trustedOutputVerificationTypes =
            new HashSet<Type>();

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
                        AddPlugin(
                            assembly,
                            plugin.RelativePath,
                            trustOutputVerificationContracts: true);
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

            // Preserve the public plugin contract without mixing user-approved code into the
            // immutable package manifest. Install-CUEToolsPlugin.ps1 creates this per-user exact
            // hash manifest. A rejected user set contributes no registrar-owned entries; approved
            // plugin code still runs in-process and is not a sandbox. A broken packaged set above
            // remains a fatal integrity failure.
            string userPluginsPath =
                PluginTrustManifest.GetUserPluginDirectory();
            if (Directory.Exists(userPluginsPath))
            {
                PluginRegistrySnapshot userRegistry =
                    PluginRegistrySnapshot.Capture();
                try
                {
                    string userManifestPath = Path.Combine(
                        userPluginsPath,
                        PluginTrustManifest.ManifestFileName);
                    if (!File.Exists(userManifestPath))
                        throw new PluginTrustException(
                            "The user plugin directory has no approval manifest.");

                    string arch = PluginTrustManifest.GetRuntimeArchitecture();
                    IList<ApprovedPlugin> approvedUserPlugins =
                        PluginTrustManifest.ReadApprovedPlugins(userPluginsPath);
                    PluginTrustManifest.PreloadApprovedNativeDependencies(
                        approvedUserPlugins, arch);
                    var userPluginTypes = new List<Type>();
                    foreach (ApprovedPlugin plugin in approvedUserPlugins)
                    {
                        if (!PluginTrustManifest.IsForRuntimeArchitecture(
                                plugin, arch) ||
                            !PluginTrustManifest.IsLoadableManagedPlugin(plugin))
                            continue;
                        Assembly assembly =
                            PluginTrustManifest.LoadApprovedAssembly(plugin);
                        userPluginTypes.AddRange(assembly.GetExportedTypes());
                    }
                    // Discovery and construction finish in an isolated batch before any global
                    // registry commit. The snapshot also restores registry collection membership
                    // and field bindings after a constructor/module-initializer failure. It cannot
                    // roll back mutation inside an existing settings object or any other in-process
                    // side effect; installing the approved hash is the code-execution trust grant.
                    AddPluginTypesTransactionally(
                        userPluginTypes,
                        "approved user plugin set");
                }
                catch (Exception ex)
                {
                    userRegistry.Restore();
                    System.Diagnostics.Trace.WriteLine(
                        "User plugin set rejected (" +
                        ex.GetType().Name + ").");
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
            AddPlugin(
                assembly,
                Path.GetFileName(plugin_path),
                trustOutputVerificationContracts: false);
        }

        private static void AddPlugin(
            Assembly assembly,
            string displayPath,
            bool trustOutputVerificationContracts)
        {
            System.Diagnostics.Trace.WriteLine(
                "Loaded plugin " + displayPath + " (" + assembly.FullName + ")");
            var batch = new PluginRegistrationBatch();
            AddPluginTypes(
                assembly.GetExportedTypes(),
                displayPath,
                batch,
                strict: false,
                trustOutputVerificationContracts:
                    trustOutputVerificationContracts);
            batch.Commit();
        }

        internal static void AddPluginTypesTransactionally(
            IEnumerable<Type> types,
            string displayPath)
        {
            PluginRegistrySnapshot registry =
                PluginRegistrySnapshot.Capture();
            try
            {
                var batch = new PluginRegistrationBatch();
                AddPluginTypes(
                    types,
                    displayPath,
                    batch,
                    strict: true,
                    trustOutputVerificationContracts: false);
                batch.Commit();
            }
            catch
            {
                registry.Restore();
                throw;
            }
        }

        private static void AddPluginTypes(
            IEnumerable<Type> types,
            string displayPath,
            PluginRegistrationBatch batch,
            bool strict,
            bool trustOutputVerificationContracts)
        {
            foreach (Type type in types)
            {
                try
                {
                    if (!type.IsClass || type.IsAbstract) continue;
                    // Match the actual shared contract, not merely an interface with the same
                    // short name. The latter could instantiate an incompatible plugin and append
                    // null to the registry after the failed "as" cast.
                    if (typeof(IAudioDecoderSettings).IsAssignableFrom(type))
                    {
                        batch.Decoders.Add(
                            (IAudioDecoderSettings)Activator.CreateInstance(type));
                    }
                    if (typeof(IAudioEncoderSettings).IsAssignableFrom(type))
                    {
                        batch.Encoders.Add(
                            (IAudioEncoderSettings)Activator.CreateInstance(type));
                        if (trustOutputVerificationContracts &&
                            IsFlacclOutputVerificationSettingsType(type))
                        {
                            batch.TrustedOutputVerificationTypes.Add(type);
                        }
                    }
                    CompressionProviderClass archclass = Attribute.GetCustomAttribute(type, typeof(CompressionProviderClass)) as CompressionProviderClass;
                    if (archclass != null &&
                        IsCompressionProviderType(type))
                    {
                        batch.CompressionProviders.Add(type);
                        if (!batch.CompressionFormats.Contains(archclass.Extension))
                            batch.CompressionFormats.Add(archclass.Extension);
                    }
                    if (IsHdcdFilterType(type))
                    {
                        batch.Hdcd = type;
                    }
                    if (typeof(ICDRipper).IsAssignableFrom(type))
                    {
                        batch.Ripper = type;
                    }
                }
                catch (Exception ex)
                {
                    if (strict)
                        throw new InvalidOperationException(
                            "Plugin type registration failed for " +
                            displayPath + ": " + type.FullName,
                            ex);
                    System.Diagnostics.Trace.WriteLine(
                        "Plugin type registration failed (" +
                        ex.GetType().Name + ").");
                }
            }
        }

        private sealed class PluginRegistrationBatch
        {
            internal readonly List<IAudioEncoderSettings> Encoders =
                new List<IAudioEncoderSettings>();
            internal readonly List<IAudioDecoderSettings> Decoders =
                new List<IAudioDecoderSettings>();
            internal readonly List<Type> CompressionProviders =
                new List<Type>();
            internal readonly List<string> CompressionFormats =
                new List<string>();
            internal readonly List<Type> TrustedOutputVerificationTypes =
                new List<Type>();
            internal Type Hdcd;
            internal Type Ripper;

            internal void Commit()
            {
                encs.AddRange(Encoders);
                decs.AddRange(Decoders);
                arcp.AddRange(CompressionProviders);
                foreach (string format in CompressionFormats)
                    if (!arcp_fmt.Contains(format))
                        arcp_fmt.Add(format);
                if (Hdcd != null)
                    hdcd = Hdcd;
                if (Ripper != null)
                    ripper = Ripper;
                lock (outputVerificationTypesGate)
                {
                    foreach (Type type in TrustedOutputVerificationTypes)
                        trustedOutputVerificationTypes.Add(type);
                }
            }
        }

        private sealed class PluginRegistrySnapshot
        {
            private readonly List<IAudioEncoderSettings> _encoderRegistry;
            private readonly List<IAudioEncoderSettings> _encoders;
            private readonly List<IAudioDecoderSettings> _decoderRegistry;
            private readonly List<IAudioDecoderSettings> _decoders;
            private readonly List<Type> _compressionRegistry;
            private readonly List<Type> _compressionProviders;
            private readonly List<string> _formatRegistry;
            private readonly List<string> _compressionFormats;
            private readonly Type _hdcd;
            private readonly Type _ripper;
            private readonly HashSet<Type> _trustedOutputVerificationTypes;

            private PluginRegistrySnapshot()
            {
                _encoderRegistry = encs;
                _encoders = new List<IAudioEncoderSettings>(encs);
                _decoderRegistry = decs;
                _decoders = new List<IAudioDecoderSettings>(decs);
                _compressionRegistry = arcp;
                _compressionProviders = new List<Type>(arcp);
                _formatRegistry = arcp_fmt;
                _compressionFormats = new List<string>(arcp_fmt);
                _hdcd = hdcd;
                _ripper = ripper;
                lock (outputVerificationTypesGate)
                {
                    _trustedOutputVerificationTypes =
                        new HashSet<Type>(trustedOutputVerificationTypes);
                }
            }

            internal static PluginRegistrySnapshot Capture()
            {
                return new PluginRegistrySnapshot();
            }

            internal void Restore()
            {
                encs = _encoderRegistry;
                encs.Clear();
                encs.AddRange(_encoders);
                decs = _decoderRegistry;
                decs.Clear();
                decs.AddRange(_decoders);
                arcp = _compressionRegistry;
                arcp.Clear();
                arcp.AddRange(_compressionProviders);
                arcp_fmt = _formatRegistry;
                arcp_fmt.Clear();
                arcp_fmt.AddRange(_compressionFormats);
                hdcd = _hdcd;
                ripper = _ripper;
                lock (outputVerificationTypesGate)
                {
                    trustedOutputVerificationTypes.Clear();
                    trustedOutputVerificationTypes.UnionWith(
                        _trustedOutputVerificationTypes);
                }
            }
        }

        /// <summary>
        /// Returns true only for the exact settings type instance enrolled while loading the
        /// build-generated package manifest. Names alone are not a verification capability:
        /// another load context can contain an assembly and type with the same identities.
        /// </summary>
        public static bool HasTrustedOutputVerificationContract(Type settingsType)
        {
            if (settingsType == null)
                return false;
            lock (outputVerificationTypesGate)
                return trustedOutputVerificationTypes.Contains(settingsType);
        }

        private static bool IsFlacclOutputVerificationSettingsType(Type type)
        {
            if (!String.Equals(
                    type.FullName,
                    "CUETools.Codecs.FLACCL.EncoderSettings",
                    StringComparison.Ordinal) ||
                !String.Equals(
                    type.Assembly.GetName().Name,
                    "CUETools.Codecs.FLACCL",
                    StringComparison.Ordinal))
                return false;

            PropertyInfo verify = type.GetProperty(
                "DoVerify",
                BindingFlags.Instance | BindingFlags.Public);
            return verify != null &&
                verify.CanRead &&
                verify.GetMethod != null &&
                verify.GetIndexParameters().Length == 0 &&
                verify.PropertyType == typeof(bool);
        }

        internal static IDisposable RegisterOutputVerificationTypeForTest(
            Type type,
            bool packagedManifestSource)
        {
            if (type == null)
                throw new ArgumentNullException("type");
            PluginRegistrySnapshot registry =
                PluginRegistrySnapshot.Capture();
            try
            {
                var batch = new PluginRegistrationBatch();
                AddPluginTypes(
                    new[] { type },
                    "output-verification-registration-test",
                    batch,
                    strict: true,
                    trustOutputVerificationContracts:
                        packagedManifestSource);
                batch.Commit();
                return new TestOutputVerificationRegistrationScope(registry);
            }
            catch
            {
                registry.Restore();
                throw;
            }
        }

        private sealed class TestOutputVerificationRegistrationScope :
            IDisposable
        {
            private readonly PluginRegistrySnapshot _registry;
            private bool _disposed;

            internal TestOutputVerificationRegistrationScope(
                PluginRegistrySnapshot registry)
            {
                _registry = registry;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _registry.Restore();
            }
        }

        internal static bool IsCompressionProviderType(Type type)
        {
            return type != null &&
                typeof(ICompressionProvider).IsAssignableFrom(type);
        }

        internal static bool IsHdcdFilterType(Type type)
        {
            return type != null &&
                type.Name == "HDCDDotNet" &&
                typeof(IAudioDest).IsAssignableFrom(type) &&
                typeof(IAudioFilter).IsAssignableFrom(type) &&
                typeof(IFormattable).IsAssignableFrom(type) &&
                type.GetConstructor(new[]
                {
                    typeof(int),
                    typeof(int),
                    typeof(int),
                    typeof(bool),
                }) != null;
        }
    }
}
