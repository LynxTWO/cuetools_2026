using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed class ArtifactManifest
{
    public int SchemaVersion { get; set; }
    public string ManifestId { get; set; } = "";
    public string ProductVersion { get; set; } = "";
    public string VersionAssembly { get; set; } = "";
    public List<RequiredFile> RequiredFiles { get; set; } = new();
    public List<string> ForbiddenFiles { get; set; } = new();
    public bool RequireExactFiles { get; set; }
    public TrustManifestProbe? TrustManifestProbe { get; set; }
    public PluginProbe? PluginProbe { get; set; }
}

internal sealed class RequiredFile
{
    public string Path { get; set; } = "";
    public long MinimumBytes { get; set; } = 1;
    public string PeMachine { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

internal sealed class PluginProbe
{
    public string ProcessorAssembly { get; set; } = "";
    public string PluginDirectory { get; set; } = "plugins";
    public List<string> ExpectedPluginFiles { get; set; } = new();
    public List<ExpectedRegistration> ExpectedRegistrations { get; set; } = new();
    public List<NativeRegistrationProbe> NativeProbes { get; set; } = new();
}

internal sealed class TrustManifestProbe
{
    public string PluginDirectory { get; set; } = "plugins";
    public string ManifestFile { get; set; } = "CUETools.PluginManifest.v1";
    public string ExpectedRuntimeArchitecture { get; set; } = "";
    public List<string> ExpectedManifestFiles { get; set; } = new();
    public List<string> ExpectedRuntimeFiles { get; set; } = new();
}

internal sealed class ExpectedRegistration
{
    public string Kind { get; set; } = "";
    public string Type { get; set; } = "";
}

internal sealed class NativeRegistrationProbe
{
    public string Kind { get; set; } = "";
    public string Type { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Member { get; set; } = "";
}

internal static class Program
{
    private const string DevelopmentPluginOverride = "CUETOOLS_ALLOW_UNMANIFESTED_PLUGINS";

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: ArtifactValidator <artifact-directory> <manifest.json>");
            return 2;
        }

        try
        {
            string artifactRoot = Path.GetFullPath(args[0]);
            string manifestPath = Path.GetFullPath(args[1]);
            ValidateRegularDirectory(artifactRoot, "artifact root");
            if (!File.Exists(manifestPath))
                throw new InvalidDataException($"Artifact contract does not exist: {manifestPath}");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            ArtifactManifest manifest = JsonSerializer.Deserialize<ArtifactManifest>(
                File.ReadAllText(manifestPath), options)
                ?? throw new InvalidDataException("Artifact contract is empty.");
            if (manifest.SchemaVersion != 1)
                throw new InvalidDataException($"Unsupported artifact contract schema {manifest.SchemaVersion}.");
            if (string.IsNullOrWhiteSpace(manifest.ManifestId) ||
                string.IsNullOrWhiteSpace(manifest.ProductVersion) ||
                string.IsNullOrWhiteSpace(manifest.VersionAssembly))
                throw new InvalidDataException("Artifact contract identity/version is missing.");
            if (manifest.RequiredFiles.Count == 0)
                throw new InvalidDataException("Artifact contract contains no required files.");

            string versionAssemblyPath = ResolveContainedPath(artifactRoot, manifest.VersionAssembly);
            ValidateRegularFile(versionAssemblyPath, manifest.VersionAssembly);
            Version? assemblyVersion = AssemblyName.GetAssemblyName(versionAssemblyPath).Version;
            if (assemblyVersion == null ||
                !string.Equals(
                    assemblyVersion.ToString(3),
                    manifest.ProductVersion,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Version assembly '{manifest.VersionAssembly}' is '{assemblyVersion}', " +
                    $"not contract version '{manifest.ProductVersion}'.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RequiredFile required in manifest.RequiredFiles)
            {
                string path = ResolveContainedPath(artifactRoot, required.Path);
                if (!seen.Add(Path.GetFullPath(path)))
                    throw new InvalidDataException($"Duplicate required path: {required.Path}");
                ValidateRegularFile(path, required.Path);
                long length = new FileInfo(path).Length;
                if (length < required.MinimumBytes)
                    throw new InvalidDataException(
                        $"Required file '{required.Path}' is {length} bytes; minimum is {required.MinimumBytes}.");
                if (!string.IsNullOrWhiteSpace(required.PeMachine))
                    ValidatePeMachine(path, required.Path, required.PeMachine);
                if (!string.IsNullOrWhiteSpace(required.Sha256))
                {
                    if (!IsSha256(required.Sha256))
                        throw new InvalidDataException(
                            $"Required file '{required.Path}' has an invalid SHA-256 contract.");
                    string actualSha256 = ComputeSha256(path);
                    if (!string.Equals(
                            actualSha256,
                            required.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            $"Required file '{required.Path}' SHA-256 is {actualSha256}, " +
                            $"not {required.Sha256}.");
                }
            }

            var forbiddenSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string forbidden in manifest.ForbiddenFiles)
            {
                string path = ResolveContainedPath(artifactRoot, forbidden);
                string fullPath = Path.GetFullPath(path);
                if (!forbiddenSeen.Add(fullPath))
                    throw new InvalidDataException($"Duplicate forbidden path: {forbidden}");
                if (seen.Contains(fullPath))
                    throw new InvalidDataException(
                        $"Artifact path is both required and forbidden: {forbidden}");
                if (PathEntryExists(path))
                    throw new InvalidDataException(
                        $"Forbidden artifact path exists: {forbidden}");
            }

            int artifactFileCount = 0;
            if (manifest.RequireExactFiles)
            {
                string[] actualFiles = EnumerateRegularArtifactFiles(artifactRoot);
                string[] expectedFiles = manifest.RequiredFiles
                    .Select(required => Path.GetRelativePath(
                            artifactRoot,
                            ResolveContainedPath(artifactRoot, required.Path))
                        .Replace('\\', '/'))
                    .ToArray();
                AssertExactArtifactPaths(expectedFiles, actualFiles);
                artifactFileCount = actualFiles.Length;
            }

            (int trustEntries, int runtimeEntries) = manifest.TrustManifestProbe == null
                ? (0, 0)
                : ValidateTrustManifest(artifactRoot, manifest.TrustManifestProbe);
            int registrations = 0;
            int nativeProbes = 0;
            if (manifest.PluginProbe != null)
                registrations = ValidateProductionPluginDiscovery(
                    artifactRoot,
                    manifest.PluginProbe,
                    out nativeProbes);

            Console.WriteLine(
                $"Artifact contract PASS: id={manifest.ManifestId}, version={manifest.ProductVersion}, " +
                $"requiredFiles={manifest.RequiredFiles.Count}, " +
                $"forbiddenFiles={manifest.ForbiddenFiles.Count}, exactFiles={artifactFileCount}, " +
                $"trustEntries={trustEntries}, " +
                $"runtimeTrustEntries={runtimeEntries}, pluginRegistrations={registrations}, " +
                $"nativePluginProbes={nativeProbes}");
            return 0;
        }
        catch (Exception ex)
        {
            Exception cause = ex.GetBaseException();
            Console.Error.WriteLine(
                $"Artifact contract FAIL: {cause.GetType().Name}: {cause.Message}");
            return 1;
        }
    }

    private static void ValidatePeMachine(
        string path,
        string relativePath,
        string expectedMachine)
    {
        ushort expected = expectedMachine switch
        {
            "x86" => 0x014c,
            "x64" => 0x8664,
            _ => throw new InvalidDataException(
                $"Required file '{relativePath}' has unsupported PE machine contract " +
                $"'{expectedMachine}'.")
        };

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5a4d)
            throw new InvalidDataException(
                $"Required file '{relativePath}' is not a valid PE image.");
        stream.Position = 0x3c;
        int peOffset = reader.ReadInt32();
        if (peOffset < 0x40 || peOffset > stream.Length - 6)
            throw new InvalidDataException(
                $"Required file '{relativePath}' has an invalid PE header offset.");
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550)
            throw new InvalidDataException(
                $"Required file '{relativePath}' has an invalid PE signature.");
        ushort actual = reader.ReadUInt16();
        if (actual != expected)
        {
            string actualName = actual switch
            {
                0x014c => "x86",
                0x8664 => "x64",
                _ => $"0x{actual:X4}"
            };
            throw new InvalidDataException(
                $"Required file '{relativePath}' PE machine is {actualName}; " +
                $"contract requires {expectedMachine}.");
        }
    }

    private static (int Entries, int RuntimeEntries) ValidateTrustManifest(
        string artifactRoot,
        TrustManifestProbe probe)
    {
        string pluginRoot = ResolveContainedPath(artifactRoot, probe.PluginDirectory);
        ValidateRegularDirectory(pluginRoot, "plugin directory");
        string manifestPath = ResolveContainedPath(pluginRoot, probe.ManifestFile);
        ValidateRegularFile(manifestPath, $"{probe.PluginDirectory}/{probe.ManifestFile}");

        string[] lines = File.ReadAllLines(manifestPath);
        if (lines.Length == 0 || lines.Length > 128)
            throw new InvalidDataException("Plugin trust manifest has an invalid entry count.");

        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? previousPath = null;
        foreach (string line in lines)
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 2 || !IsSha256(fields[0]))
                throw new InvalidDataException("Plugin trust manifest contains an invalid record.");
            string relativePath = fields[1];
            ValidatePluginRelativePath(relativePath);
            if (previousPath != null &&
                StringComparer.Ordinal.Compare(previousPath, relativePath) >= 0)
                throw new InvalidDataException(
                    "Plugin trust manifest paths are not unique and ordinally sorted.");
            previousPath = relativePath;
            if (!entries.TryAdd(relativePath, fields[0]))
                throw new InvalidDataException(
                    $"Plugin trust manifest has duplicate path '{relativePath}'.");

            string filePath = ResolveContainedPath(pluginRoot, relativePath);
            ValidateRegularFile(filePath, $"{probe.PluginDirectory}/{relativePath}");
            string actualHash = ComputeSha256(filePath);
            if (!string.Equals(actualHash, fields[0], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Plugin trust hash does not match '{relativePath}'.");
        }

        string[] actualCandidates = EnumerateRuntimeDllCandidates(pluginRoot);
        AssertExactPaths(
            "packaged runtime DLL candidates",
            entries.Keys,
            actualCandidates);
        AssertExactPaths(
            "contracted trust-manifest entries",
            probe.ExpectedManifestFiles,
            entries.Keys);

        string architecture = Type.GetType("Mono.Runtime", throwOnError: false) != null
            ? "mono"
            : IntPtr.Size == 8 ? "x64" : "win32";
        if (!string.Equals(
            architecture,
            probe.ExpectedRuntimeArchitecture,
            StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Artifact contract requires a {probe.ExpectedRuntimeArchitecture} validation host, " +
                $"but the current runtime is {architecture}.");
        string[] runtimeEntries = entries.Keys
            .Where(path =>
            {
                int slash = path.IndexOf('/');
                return slash < 0 ||
                    (slash == architecture.Length &&
                     path.StartsWith(architecture + "/", StringComparison.OrdinalIgnoreCase) &&
                     path.IndexOf('/', slash + 1) < 0);
            })
            .ToArray();
        AssertExactPaths(
            $"contracted {architecture} runtime plugin selection",
            probe.ExpectedRuntimeFiles,
            runtimeEntries);
        return (entries.Count, runtimeEntries.Length);
    }

    private static string[] EnumerateRuntimeDllCandidates(string pluginRoot)
    {
        var candidates = new List<string>();
        candidates.AddRange(
            Directory.GetFiles(pluginRoot, "*.dll", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name != null)!);
        foreach (string architecture in new[] { "mono", "win32", "x64" })
        {
            string directory = Path.Combine(pluginRoot, architecture);
            if (!Directory.Exists(directory))
                continue;
            ValidateRegularDirectory(directory, $"plugin architecture directory '{architecture}'");
            candidates.AddRange(
                Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                    .Select(path => architecture + "/" + Path.GetFileName(path)));
        }
        return candidates.ToArray();
    }

    private static void ValidatePluginRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\'))
            throw new InvalidDataException(
                $"Plugin trust manifest has invalid path '{relativePath}'.");
        string[] segments = relativePath.Split('/');
        if (segments.Length is < 1 or > 2 ||
            segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException(
                $"Plugin trust manifest has invalid path '{relativePath}'.");
        if (segments.Length == 2 &&
            segments[0] != "mono" &&
            segments[0] != "win32" &&
            segments[0] != "x64")
            throw new InvalidDataException(
                $"Plugin trust manifest has unknown architecture path '{relativePath}'.");
        string fileName = segments[^1];
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Plugin trust manifest has non-DLL path '{relativePath}'.");
    }

    private static void AssertExactPaths(
        string description,
        IEnumerable<string> expectedPaths,
        IEnumerable<string> actualPaths)
    {
        string[] expected = expectedPaths.ToArray();
        string[] actual = actualPaths.ToArray();
        Array.Sort(expected, StringComparer.Ordinal);
        Array.Sort(actual, StringComparer.Ordinal);
        if (expected.SequenceEqual(actual, StringComparer.Ordinal))
            return;
        string[] missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
        string[] unexpected = actual.Except(expected, StringComparer.Ordinal).ToArray();
        throw new InvalidDataException(
            $"{description} differ: missing=[{string.Join(", ", missing)}], " +
            $"unexpected=[{string.Join(", ", unexpected)}].");
    }

    private static void AssertExactArtifactPaths(
        IEnumerable<string> expectedPaths,
        IEnumerable<string> actualPaths)
    {
        string[] expected = expectedPaths.ToArray();
        string[] actual = actualPaths.ToArray();
        Array.Sort(expected, StringComparer.OrdinalIgnoreCase);
        Array.Sort(actual, StringComparer.OrdinalIgnoreCase);
        if (expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
            return;
        string[] missing = expected
            .Except(actual, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] unexpected = actual
            .Except(expected, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        throw new InvalidDataException(
            $"Exact artifact files differ: missing=[{string.Join(", ", missing)}], " +
            $"unexpected=[{string.Join(", ", unexpected)}].");
    }

    private static string[] EnumerateRegularArtifactFiles(string artifactRoot)
    {
        var pending = new Stack<DirectoryInfo>();
        var files = new List<string>();
        pending.Push(new DirectoryInfo(artifactRoot));
        while (pending.Count > 0)
        {
            DirectoryInfo directory = pending.Pop();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    $"Artifact directory must not be a reparse point: {directory.FullName}");

            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        $"Artifact must not contain a reparse point: {entry.FullName}");
                if (entry is DirectoryInfo childDirectory)
                {
                    pending.Push(childDirectory);
                    continue;
                }
                if (entry is not FileInfo)
                    throw new InvalidDataException(
                        $"Artifact contains an unsupported filesystem entry: {entry.FullName}");

                string relativePath = Path.GetRelativePath(artifactRoot, entry.FullName)
                    .Replace('\\', '/');
                files.Add(relativePath);
            }
        }
        return files.ToArray();
    }

    private static bool IsSha256(string value)
    {
        return value.Length == 64 && value.All(
            character =>
                character is >= '0' and <= '9' ||
                character is >= 'a' and <= 'f' ||
                character is >= 'A' and <= 'F');
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.Open(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using SHA256 sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private static int ValidateProductionPluginDiscovery(
        string artifactRoot,
        PluginProbe probe,
        out int nativeProbeCount)
    {
        string processorPath = ResolveContainedPath(artifactRoot, probe.ProcessorAssembly);
        ValidateRegularFile(processorPath, probe.ProcessorAssembly);
        string pluginRoot = ResolveContainedPath(artifactRoot, probe.PluginDirectory);
        ValidateRegularDirectory(pluginRoot, "plugin directory");

        var expectedPluginPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relativePath in probe.ExpectedPluginFiles)
        {
            string fullPath = ResolveContainedPath(pluginRoot, relativePath);
            ValidateRegularFile(fullPath, $"{probe.PluginDirectory}/{relativePath}");
            expectedPluginPaths.Add(Path.GetFullPath(fullPath));
            _ = AssemblyName.GetAssemblyName(fullPath);
        }
        if (expectedPluginPaths.Count == 0)
            throw new InvalidDataException("Plugin probe contains no expected plugin files.");

        string[] actualPlugins = Directory.GetFiles(pluginRoot, "CUETools.*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] unexpectedPlugins = actualPlugins
            .Where(path => !expectedPluginPaths.Contains(path))
            .ToArray();
        if (unexpectedPlugins.Length != 0)
            throw new InvalidDataException(
                "Artifact has uncontracted top-level plugins: " +
                string.Join(", ", unexpectedPlugins.Select(Path.GetFileName)));

        ResolveEventHandler resolver = (_, eventArgs) =>
        {
            var name = new AssemblyName(eventArgs.Name).Name + ".dll";
            string rootCandidate = Path.Combine(artifactRoot, name);
            if (File.Exists(rootCandidate))
                return Assembly.LoadFrom(rootCandidate);
            string pluginCandidate = Path.Combine(pluginRoot, name);
            return File.Exists(pluginCandidate) ? Assembly.LoadFrom(pluginCandidate) : null;
        };

        string? oldOverride = Environment.GetEnvironmentVariable(DevelopmentPluginOverride);
        Environment.SetEnvironmentVariable(DevelopmentPluginOverride, null);
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            Assembly processor = Assembly.LoadFrom(processorPath);
            Type registry = processor.GetType("CUETools.Processor.CUEProcessorPlugins", throwOnError: true)
                ?? throw new TypeLoadException("CUETools.Processor.CUEProcessorPlugins");
            var actual = new Dictionary<string, object>(StringComparer.Ordinal);
            AddRegistrations(registry, "encs", "encoder", actual);
            AddRegistrations(registry, "decs", "decoder", actual);
            AddTypeRegistration(registry, "hdcd", "hdcd", actual);

            foreach (ExpectedRegistration expected in probe.ExpectedRegistrations)
            {
                if (expected.Kind != "encoder" &&
                    expected.Kind != "decoder" &&
                    expected.Kind != "hdcd")
                    throw new InvalidDataException($"Unknown plugin registration kind '{expected.Kind}'.");
                string key = expected.Kind + ":" + expected.Type;
                if (!actual.TryGetValue(key, out object? registration))
                    throw new InvalidDataException($"Production plugin discovery did not register '{key}'.");
                Type registrationType = registration as Type ?? registration.GetType();
                string registrationPath = Path.GetFullPath(registrationType.Assembly.Location);
                if (!expectedPluginPaths.Contains(registrationPath))
                    throw new InvalidDataException(
                        $"Production plugin registration '{key}' was loaded from " +
                        $"'{registrationPath}', not the packaged plugin directory.");
            }
            if (probe.ExpectedRegistrations.Count == 0)
                throw new InvalidDataException("Plugin probe contains no expected registrations.");

            foreach (NativeRegistrationProbe nativeProbe in probe.NativeProbes)
            {
                string key = nativeProbe.Kind + ":" + nativeProbe.Type;
                if (!actual.TryGetValue(key, out object? registration))
                    throw new InvalidDataException(
                        $"Native plugin probe references an absent registration '{key}'.");
                switch (nativeProbe.Operation)
                {
                    case "nonEmptyStringProperty":
                        if (registration is Type)
                            throw new InvalidDataException(
                                $"Native property probe '{key}' requires a registration instance.");
                        PropertyInfo property = registration.GetType().GetProperty(
                            nativeProbe.Member,
                            BindingFlags.Public | BindingFlags.Instance)
                            ?? throw new MissingMemberException(
                                registration.GetType().FullName,
                                nativeProbe.Member);
                        string? value = property.GetValue(registration)?.ToString();
                        if (string.IsNullOrWhiteSpace(value))
                            throw new InvalidDataException(
                                $"Native property probe '{key}.{nativeProbe.Member}' returned no value.");
                        break;
                    case "hdcdConstructor":
                        if (registration is not Type hdcdType)
                            throw new InvalidDataException(
                                $"HDCD native probe '{key}' requires a registered Type.");
                        object? instance = null;
                        try
                        {
                            instance = Activator.CreateInstance(
                                hdcdType,
                                new object[] { 2, 44100, 24, false });
                            if (instance == null)
                                throw new InvalidDataException(
                                    $"HDCD native probe '{key}' returned no instance.");
                        }
                        finally
                        {
                            if (instance != null)
                            {
                                MethodInfo close = hdcdType.GetMethod(
                                    "Close",
                                    BindingFlags.Public | BindingFlags.Instance)
                                    ?? throw new MissingMethodException(hdcdType.FullName, "Close");
                                close.Invoke(instance, null);
                            }
                        }
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Unknown native plugin probe operation '{nativeProbe.Operation}'.");
                }
            }
            nativeProbeCount = probe.NativeProbes.Count;
            return probe.ExpectedRegistrations.Count;
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            Environment.SetEnvironmentVariable(DevelopmentPluginOverride, oldOverride);
        }
    }

    private static void AddRegistrations(
        Type registry,
        string fieldName,
        string kind,
        Dictionary<string, object> registrations)
    {
        FieldInfo field = registry.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingFieldException(registry.FullName, fieldName);
        if (field.GetValue(null) is not IEnumerable values)
            throw new InvalidDataException($"Plugin registry field '{fieldName}' is not enumerable.");
        foreach (object? value in values)
        {
            if (value != null && value.GetType().FullName is string typeName)
            {
                string key = kind + ":" + typeName;
                if (!registrations.TryAdd(key, value))
                    throw new InvalidDataException(
                        $"Plugin registry contains duplicate registration '{key}'.");
            }
        }
    }

    private static void AddTypeRegistration(
        Type registry,
        string fieldName,
        string kind,
        Dictionary<string, object> registrations)
    {
        FieldInfo field = registry.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingFieldException(registry.FullName, fieldName);
        if (field.GetValue(null) is Type value && value.FullName is string typeName)
        {
            string key = kind + ":" + typeName;
            if (!registrations.TryAdd(key, value))
                throw new InvalidDataException(
                    $"Plugin registry contains duplicate registration '{key}'.");
        }
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Artifact path must be relative: '{relativePath}'.");
        string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Artifact path escapes the root: '{relativePath}'.");
        return result;
    }

    private static bool PathEntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void ValidateRegularDirectory(string path, string description)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists)
            throw new DirectoryNotFoundException($"Missing {description}: {path}");
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{description} must not be a reparse point: {path}");
    }

    private static void ValidateRegularFile(string path, string description)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"Missing required file '{description}'.", path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Required file must not be a reparse point: {description}");
    }
}
