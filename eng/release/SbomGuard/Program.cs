using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class Program
{
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false
    };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 &&
                string.Equals(args[0], "canonicalize", StringComparison.Ordinal))
            {
                CanonicalizeFile(args[1]);
                return 0;
            }

            if (args.Length == 5 &&
                string.Equals(args[0], "validate-spdx", StringComparison.Ordinal))
            {
                SpdxSummary summary = ValidateSpdx(
                    args[1],
                    args[2],
                    args[3],
                    args[4]);
                Console.WriteLine(
                    $"SPDX artifact inventory PASS: {summary.FileCount} files, " +
                    $"{summary.PackageCount} root package, exact SHA-256 closure.");
                return 0;
            }

            if (args.Length == 4 &&
                string.Equals(args[0], "validate-cyclonedx", StringComparison.Ordinal))
            {
                CycloneSummary summary = ValidateCycloneDx(
                    args[1],
                    args[2],
                    args[3]);
                Console.WriteLine(
                    $"CycloneDX dependency graph PASS: {summary.ComponentCount} " +
                    $"components, {summary.DependencyCount} dependency nodes.");
                return 0;
            }

            if (args.Length == 1 &&
                string.Equals(args[0], "self-test", StringComparison.Ordinal))
            {
                RunSelfTest();
                Console.WriteLine("SBOM guard self-test passed: 8 checks.");
                return 0;
            }

            Console.Error.WriteLine(
                "Usage:\n" +
                "  SbomGuard canonicalize <json-path>\n" +
                "  SbomGuard validate-spdx <artifact-dir> <spdx-path> <package-name> <version>\n" +
                "  SbomGuard validate-cyclonedx <cdx-path> <package-name> <version>\n" +
                "  SbomGuard self-test");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SBOM guard failed: {ex.Message}");
            return 1;
        }
    }

    private static void CanonicalizeFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new InvalidDataException($"JSON input does not exist: {fullPath}");
        RejectReparsePoint(fullPath, "JSON input");

        JsonNode root = ParseNode(File.ReadAllText(fullPath));
        JsonNode canonical = Canonicalize(root);
        string json = canonical.ToJsonString(IndentedJson)
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        File.WriteAllText(fullPath, json, new UTF8Encoding(false));
    }

    private static JsonNode Canonicalize(JsonNode node)
    {
        if (node is JsonObject sourceObject)
        {
            var result = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> property in
                     sourceObject.OrderBy(
                         static property => property.Key,
                         StringComparer.Ordinal))
            {
                result.Add(
                    property.Key,
                    property.Value is null ? null : Canonicalize(property.Value));
            }
            return result;
        }

        if (node is JsonArray sourceArray)
        {
            var items = new List<(string Key, JsonNode? Node)>();
            foreach (JsonNode? item in sourceArray)
            {
                JsonNode? canonicalItem = item is null ? null : Canonicalize(item);
                string key = canonicalItem?.ToJsonString(CompactJson) ?? "null";
                items.Add((key, canonicalItem));
            }
            items.Sort(static (left, right) =>
                StringComparer.Ordinal.Compare(left.Key, right.Key));
            var result = new JsonArray();
            foreach ((string _, JsonNode? item) in items)
                result.Add(item);
            return result;
        }

        return node.DeepClone();
    }

    private static SpdxSummary ValidateSpdx(
        string artifactDirectory,
        string spdxPath,
        string expectedPackageName,
        string expectedPackageVersion)
    {
        string artifactRoot = Path.GetFullPath(artifactDirectory);
        if (!Directory.Exists(artifactRoot))
            throw new InvalidDataException($"Artifact directory does not exist: {artifactRoot}");
        RejectReparsePoint(artifactRoot, "Artifact root");

        JsonObject document = RequireObject(
            ParseNode(File.ReadAllText(Path.GetFullPath(spdxPath))),
            "SPDX document");
        RequireString(document, "spdxVersion", "SPDX-2.2");
        RequireString(document, "dataLicense", "CC0-1.0");
        RequireString(document, "SPDXID", "SPDXRef-DOCUMENT");

        JsonObject creationInfo = RequireObjectProperty(document, "creationInfo");
        RequireArrayProperty(creationInfo, "creators");
        JsonArray documentDescribes = RequireArrayProperty(
            document,
            "documentDescribes");
        if (documentDescribes.Count != 1 ||
            !string.Equals(
                RequireStringValue(documentDescribes[0], "documentDescribes[0]"),
                "SPDXRef-RootPackage",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "SPDX documentDescribes must contain only SPDXRef-RootPackage.");
        }

        JsonArray packages = RequireArrayProperty(document, "packages");
        if (packages.Count != 1)
            throw new InvalidDataException("SPDX must contain exactly one root package.");
        JsonObject rootPackage = RequireObject(packages[0], "packages[0]");
        RequireString(rootPackage, "SPDXID", "SPDXRef-RootPackage");
        RequireString(rootPackage, "name", expectedPackageName);
        RequireString(rootPackage, "versionInfo", expectedPackageVersion);
        if (!RequireBoolean(rootPackage, "filesAnalyzed"))
            throw new InvalidDataException("SPDX root package must analyze files.");
        RequireArrayProperty(rootPackage, "licenseInfoFromFiles");
        RequireArrayProperty(rootPackage, "externalRefs");
        JsonArray packageFileIds = RequireArrayProperty(rootPackage, "hasFiles");

        JsonArray relationships = RequireArrayProperty(document, "relationships");
        if (relationships.Count != 1)
            throw new InvalidDataException("SPDX must contain one document relationship.");
        JsonObject relationship = RequireObject(
            relationships[0],
            "relationships[0]");
        RequireString(relationship, "spdxElementId", "SPDXRef-DOCUMENT");
        RequireString(relationship, "relationshipType", "DESCRIBES");
        RequireString(
            relationship,
            "relatedSpdxElement",
            "SPDXRef-RootPackage");

        Dictionary<string, FileRecord> artifactFiles = ReadArtifactFiles(artifactRoot);
        JsonArray files = RequireArrayProperty(document, "files");
        if (files.Count != artifactFiles.Count)
        {
            throw new InvalidDataException(
                $"SPDX file count {files.Count} does not match artifact file " +
                $"count {artifactFiles.Count}.");
        }

        var spdxIds = new HashSet<string>(StringComparer.Ordinal);
        var spdxPaths = new HashSet<string>(StringComparer.Ordinal);
        var caseFoldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? fileNode in files)
        {
            JsonObject file = RequireObject(fileNode, "SPDX file");
            string fileName = RequireStringValue(file["fileName"], "fileName");
            string relativePath = NormalizeSpdxFileName(fileName);
            if (!spdxPaths.Add(relativePath) || !caseFoldedPaths.Add(relativePath))
                throw new InvalidDataException($"SPDX contains a duplicate file path: {relativePath}");
            if (!artifactFiles.TryGetValue(relativePath, out FileRecord? artifactFile))
                throw new InvalidDataException($"SPDX names a file outside the artifact: {relativePath}");

            string spdxId = RequireStringValue(file["SPDXID"], "file SPDXID");
            if (!spdxIds.Add(spdxId))
                throw new InvalidDataException($"SPDX contains a duplicate file id: {spdxId}");
            JsonArray checksums = RequireArrayProperty(file, "checksums");
            string[] sha256Values = checksums
                .Select(checksum => RequireObject(checksum, "file checksum"))
                .Where(checksum => string.Equals(
                    RequireStringValue(checksum["algorithm"], "checksum algorithm"),
                    "SHA256",
                    StringComparison.Ordinal))
                .Select(checksum =>
                    RequireStringValue(checksum["checksumValue"], "checksum value"))
                .ToArray();
            if (sha256Values.Length != 1 ||
                !string.Equals(
                    sha256Values[0],
                    artifactFile.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SPDX SHA-256 does not match artifact file: {relativePath}");
            }
            RequireArrayProperty(file, "licenseInfoInFiles");
        }

        HashSet<string> packageIds = packageFileIds
            .Select((node, index) =>
                RequireStringValue(node, $"hasFiles[{index}]"))
            .ToHashSet(StringComparer.Ordinal);
        if (packageIds.Count != spdxIds.Count || !packageIds.SetEquals(spdxIds))
            throw new InvalidDataException("SPDX root package does not contain the exact file-id set.");

        string sidecarPath = Path.GetFullPath(spdxPath) + ".sha256";
        if (!File.Exists(sidecarPath))
            throw new InvalidDataException("SPDX SHA-256 sidecar is missing.");
        string expectedSidecar = File.ReadAllText(sidecarPath).Trim();
        string actualManifestHash = ComputeSha256(Path.GetFullPath(spdxPath));
        if (!string.Equals(
                expectedSidecar,
                actualManifestHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("SPDX SHA-256 sidecar is stale.");
        }

        return new SpdxSummary(files.Count, packages.Count);
    }

    private static CycloneSummary ValidateCycloneDx(
        string cyclonePath,
        string expectedPackageName,
        string expectedPackageVersion)
    {
        JsonObject document = RequireObject(
            ParseNode(File.ReadAllText(Path.GetFullPath(cyclonePath))),
            "CycloneDX document");
        RequireString(document, "bomFormat", "CycloneDX");
        RequireString(document, "specVersion", "1.6");
        JsonObject metadata = RequireObjectProperty(document, "metadata");
        JsonObject rootComponent = RequireObjectProperty(metadata, "component");
        RequireString(rootComponent, "name", expectedPackageName);
        RequireString(rootComponent, "version", expectedPackageVersion);
        string rootRef = RequireStringValue(rootComponent["bom-ref"], "root bom-ref");

        JsonArray components = RequireArrayProperty(document, "components");
        JsonArray dependencies = RequireArrayProperty(document, "dependencies");
        if (components.Count == 0 || dependencies.Count == 0)
            throw new InvalidDataException("CycloneDX dependency graph is empty.");

        var knownRefs = new HashSet<string>(StringComparer.Ordinal) { rootRef };
        foreach (JsonNode? componentNode in components)
        {
            JsonObject component = RequireObject(componentNode, "CycloneDX component");
            string componentRef = RequireStringValue(component["bom-ref"], "component bom-ref");
            if (!knownRefs.Add(componentRef))
                throw new InvalidDataException($"CycloneDX duplicate bom-ref: {componentRef}");
        }

        var dependencyRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? dependencyNode in dependencies)
        {
            JsonObject dependency = RequireObject(
                dependencyNode,
                "CycloneDX dependency");
            string dependencyRef = RequireStringValue(
                dependency["ref"],
                "dependency ref");
            if (!knownRefs.Contains(dependencyRef) ||
                !dependencyRefs.Add(dependencyRef))
            {
                throw new InvalidDataException(
                    $"CycloneDX dependency has an unknown or duplicate ref: {dependencyRef}");
            }
            JsonNode? dependsOnNode = dependency["dependsOn"];
            JsonArray dependsOn = dependsOnNode switch
            {
                null => new JsonArray(),
                JsonArray array => array,
                _ => throw new InvalidDataException(
                    "CycloneDX dependsOn must be a JSON array when present.")
            };
            foreach (JsonNode? targetNode in dependsOn)
            {
                string targetRef = RequireStringValue(targetNode, "dependsOn ref");
                if (!knownRefs.Contains(targetRef))
                    throw new InvalidDataException(
                        $"CycloneDX dependency names an unknown target: {targetRef}");
            }
        }
        if (!dependencyRefs.Contains(rootRef))
            throw new InvalidDataException("CycloneDX graph omits the root dependency node.");

        return new CycloneSummary(components.Count, dependencies.Count);
    }

    private static Dictionary<string, FileRecord> ReadArtifactFiles(string artifactRoot)
    {
        string prefix = artifactRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var files = new Dictionary<string, FileRecord>(StringComparer.Ordinal);
        var caseFolded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Stack<string>();
        directories.Push(artifactRoot);
        while (directories.Count > 0)
        {
            string directory = directories.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Artifact contains a reparse point: {entry}");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Push(entry);
                    continue;
                }

                string fullPath = Path.GetFullPath(entry);
                if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Artifact file escapes its root: {fullPath}");
                string relativePath = fullPath[prefix.Length..]
                    .Replace('\\', '/');
                if (!caseFolded.Add(relativePath) || files.ContainsKey(relativePath))
                    throw new InvalidDataException($"Artifact contains a duplicate path: {relativePath}");
                files.Add(relativePath, new FileRecord(ComputeSha256(fullPath)));
            }
        }
        if (files.Count == 0)
            throw new InvalidDataException("Artifact contains no files.");
        return files;
    }

    private static string NormalizeSpdxFileName(string fileName)
    {
        if (!fileName.StartsWith("./", StringComparison.Ordinal))
            throw new InvalidDataException($"SPDX fileName is not artifact-relative: {fileName}");
        string relative = fileName[2..].Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relative) ||
            relative.StartsWith("/", StringComparison.Ordinal) ||
            relative.Split('/').Any(segment =>
                segment.Length == 0 ||
                string.Equals(segment, ".", StringComparison.Ordinal) ||
                string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"SPDX fileName is unsafe: {fileName}");
        }
        return relative;
    }

    private static JsonNode ParseNode(string json)
    {
        return JsonNode.Parse(
            json,
            nodeOptions: null,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            }) ?? throw new InvalidDataException("JSON document is null.");
    }

    private static JsonObject RequireObject(JsonNode? node, string purpose)
    {
        return node as JsonObject ??
            throw new InvalidDataException($"{purpose} must be a JSON object.");
    }

    private static JsonObject RequireObjectProperty(JsonObject owner, string name)
    {
        return RequireObject(owner[name], name);
    }

    private static JsonArray RequireArrayProperty(JsonObject owner, string name)
    {
        return owner[name] as JsonArray ??
            throw new InvalidDataException($"{name} must be a JSON array.");
    }

    private static string RequireStringValue(JsonNode? node, string purpose)
    {
        if (node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out string? value) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{purpose} must be a non-empty JSON string.");
        }
        return value!;
    }

    private static void RequireString(
        JsonObject owner,
        string name,
        string expected)
    {
        string actual = RequireStringValue(owner[name], name);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{name} must be '{expected}', found '{actual}'.");
        }
    }

    private static bool RequireBoolean(JsonObject owner, string name)
    {
        JsonNode? node = owner[name];
        if (node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<bool>(out bool value))
            throw new InvalidDataException($"{name} must be a JSON boolean.");
        return value;
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void RejectReparsePoint(string path, string purpose)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{purpose} must not be a reparse point: {path}");
    }

    private static void RunSelfTest()
    {
        JsonNode canonical = Canonicalize(ParseNode(
            """{"z":[{"b":2,"a":1}],"a":[],"m":["one"]}"""));
        JsonObject canonicalObject = RequireObject(canonical, "canonical fixture");
        if (canonicalObject["z"] is not JsonArray { Count: 1 } ||
            canonicalObject["a"] is not JsonArray { Count: 0 } ||
            canonicalObject["m"] is not JsonArray { Count: 1 })
        {
            throw new InvalidDataException("Canonicalization did not preserve array types.");
        }

        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "cuetools-sbom-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string artifactRoot = Path.Combine(tempRoot, "artifact");
            Directory.CreateDirectory(artifactRoot);
            string artifactPath = Path.Combine(artifactRoot, "fixture.txt");
            File.WriteAllText(artifactPath, "fixture\n", new UTF8Encoding(false));
            string fileHash = ComputeSha256(artifactPath);
            string fileId = "SPDXRef-File-fixture";
            var spdx = new JsonObject
            {
                ["spdxVersion"] = "SPDX-2.2",
                ["dataLicense"] = "CC0-1.0",
                ["SPDXID"] = "SPDXRef-DOCUMENT",
                ["creationInfo"] = new JsonObject
                {
                    ["creators"] = new JsonArray("Tool: fixture")
                },
                ["documentDescribes"] = new JsonArray("SPDXRef-RootPackage"),
                ["packages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["SPDXID"] = "SPDXRef-RootPackage",
                        ["name"] = "Fixture",
                        ["versionInfo"] = "1.0",
                        ["filesAnalyzed"] = true,
                        ["licenseInfoFromFiles"] = new JsonArray("NOASSERTION"),
                        ["externalRefs"] = new JsonArray(),
                        ["hasFiles"] = new JsonArray(fileId)
                    }
                },
                ["relationships"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["spdxElementId"] = "SPDXRef-DOCUMENT",
                        ["relationshipType"] = "DESCRIBES",
                        ["relatedSpdxElement"] = "SPDXRef-RootPackage"
                    }
                },
                ["files"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["fileName"] = "./fixture.txt",
                        ["SPDXID"] = fileId,
                        ["checksums"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["algorithm"] = "SHA256",
                                ["checksumValue"] = fileHash
                            }
                        },
                        ["licenseInfoInFiles"] = new JsonArray("NOASSERTION")
                    }
                }
            };
            string spdxPath = Path.Combine(tempRoot, "manifest.spdx.json");
            File.WriteAllText(
                spdxPath,
                spdx.ToJsonString(IndentedJson) + "\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                spdxPath + ".sha256",
                ComputeSha256(spdxPath),
                new UTF8Encoding(false));
            SpdxSummary spdxSummary = ValidateSpdx(
                artifactRoot,
                spdxPath,
                "Fixture",
                "1.0");
            if (spdxSummary != new SpdxSummary(1, 1))
                throw new InvalidDataException("SPDX fixture summary is incorrect.");

            JsonNode malformed = spdx.DeepClone();
            RequireObject(malformed, "malformed fixture")["documentDescribes"] =
                new JsonObject
                {
                    ["value"] = new JsonArray("SPDXRef-RootPackage"),
                    ["Count"] = 1
                };
            string malformedPath = Path.Combine(tempRoot, "malformed.spdx.json");
            File.WriteAllText(
                malformedPath,
                malformed.ToJsonString(IndentedJson),
                new UTF8Encoding(false));
            File.WriteAllText(
                malformedPath + ".sha256",
                ComputeSha256(malformedPath),
                new UTF8Encoding(false));
            bool malformedRejected = false;
            try
            {
                ValidateSpdx(artifactRoot, malformedPath, "Fixture", "1.0");
            }
            catch (InvalidDataException ex)
            {
                malformedRejected = ex.Message.Contains(
                    "documentDescribes must be a JSON array",
                    StringComparison.Ordinal);
            }
            if (!malformedRejected)
                throw new InvalidDataException("Malformed one-element array wrapper was accepted.");

            var cdx = new JsonObject
            {
                ["bomFormat"] = "CycloneDX",
                ["specVersion"] = "1.6",
                ["metadata"] = new JsonObject
                {
                    ["component"] = new JsonObject
                    {
                        ["bom-ref"] = "root",
                        ["name"] = "Fixture",
                        ["version"] = "1.0"
                    }
                },
                ["components"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["bom-ref"] = "dependency"
                    }
                },
                ["dependencies"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["ref"] = "root",
                        ["dependsOn"] = new JsonArray("dependency")
                    },
                    new JsonObject
                    {
                        ["ref"] = "dependency",
                        ["dependsOn"] = new JsonArray()
                    }
                }
            };
            string cdxPath = Path.Combine(tempRoot, "fixture.cdx.json");
            File.WriteAllText(
                cdxPath,
                cdx.ToJsonString(IndentedJson),
                new UTF8Encoding(false));
            CycloneSummary cycloneSummary = ValidateCycloneDx(
                cdxPath,
                "Fixture",
                "1.0");
            if (cycloneSummary != new CycloneSummary(1, 2))
                throw new InvalidDataException("CycloneDX fixture summary is incorrect.");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed record FileRecord(string Sha256);
    private readonly record struct SpdxSummary(int FileCount, int PackageCount);
    private readonly record struct CycloneSummary(int ComponentCount, int DependencyCount);
}
