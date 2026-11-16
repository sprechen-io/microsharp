using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MicroSharp.ReleaseGate;

/// <summary>
/// Checks the tag, the CI run and package hashes before a publishing token is requested.
/// </summary>
public static class ReleaseGate
{
    public const string CiWorkflow = ".github/workflows/ci.yml";
    public const string CiJob = "net11";
    public static readonly string[] RequiredJobs = ["native-linux", "native-windows", "net11"];
    public const string NugetOrg = "https://api.nuget.org/v3/index.json";

    private static readonly string[] PackageIds = ["MicroSharp", "MicroSharp.Browser", "MicroSharp.Native"];
    private static readonly Regex VersionPattern = new(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex ShaPattern = new(@"^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions ManifestOptions = new() { WriteIndented = true };

    private static readonly Dictionary<string, string[]> Payloads = new(StringComparer.Ordinal)
    {
        ["MicroSharp"] = ["lib/net11.0/MicroSharp.dll"],
        ["MicroSharp.Browser"] =
        [
            "lib/net11.0/MicroSharp.Browser.dll",
            "staticwebassets/microsharp-capture.js",
            "staticwebassets/pcm-recorder-processor.js",
        ],
        ["MicroSharp.Native"] = ["lib/net11.0/MicroSharp.Native.dll"],
    };

    public static JsonObject WriteManifest(string packageDir, string propsPath, string commit, long runId, string output)
    {
        RequireCommit(commit);
        var version = ReadVersion(propsPath);
        var packages = new JsonArray();
        foreach (var record in FileRecords(packageDir, version))
        {
            packages.Add(new JsonObject
            {
                ["id"] = record.Id,
                ["file"] = record.File,
                ["sha256"] = record.Sha256,
            });
        }

        var document = new JsonObject
        {
            ["schema"] = 1,
            ["commit"] = commit,
            ["version"] = version,
            ["workflow"] = CiWorkflow,
            ["runId"] = runId,
            ["packages"] = packages,
        };
        File.WriteAllText(output, document.ToJsonString(ManifestOptions) + "\n");
        return document;
    }

    public static IReadOnlyList<string> Check(
        string tag,
        string commit,
        string propsPath,
        JsonObject runsDocument,
        JsonObject jobsDocument,
        JsonObject manifest,
        string packageDir)
    {
        RequireCommit(commit);
        var version = ReadVersion(propsPath);
        RequireTag(tag, version);
        var run = SelectRunRecord(runsDocument, commit);
        RequireQualificationJobs(jobsDocument, run.Id);
        if (!TryLong(manifest["schema"], out var schema) || schema != 1)
        {
            throw new ReleaseGateException("release manifest schema is not 1");
        }

        if (GetString(manifest, "commit") != commit)
        {
            throw new ReleaseGateException("release manifest commit does not match the tagged commit");
        }

        if (GetString(manifest, "version") != version)
        {
            throw new ReleaseGateException("release manifest version does not match Directory.Build.props");
        }

        if (GetString(manifest, "workflow") != CiWorkflow)
        {
            throw new ReleaseGateException("release manifest did not come from ci.yml");
        }

        if (!TryLong(manifest["runId"], out var manifestRun) || manifestRun != run.Id)
        {
            throw new ReleaseGateException("release manifest run does not match the qualifying CI run");
        }

        var expected = FileRecords(packageDir, version).ToDictionary(record => record.File, StringComparer.Ordinal);
        if (manifest["packages"] is not JsonArray listed)
        {
            throw new ReleaseGateException("release manifest has no package list");
        }

        var seen = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in listed)
        {
            if (node is not JsonObject record)
            {
                throw new ReleaseGateException("release manifest package list does not match the qualified files");
            }

            var file = GetString(record, "file");
            if (file is null)
            {
                throw new ReleaseGateException("release manifest package list does not match the qualified files");
            }

            seen[file] = record;
        }

        if (seen.Count != expected.Count || seen.Keys.Any(name => !expected.ContainsKey(name)))
        {
            throw new ReleaseGateException("release manifest package list does not match the qualified files");
        }

        foreach (var (name, record) in expected)
        {
            var listedRecord = seen[name];
            if (GetString(listedRecord, "id") != record.Id || GetString(listedRecord, "sha256") != record.Sha256)
            {
                throw new ReleaseGateException($"hash mismatch for {name}");
            }
        }

        var order = new List<string>(PackageIds.Length * 2);
        foreach (var packageId in PackageIds)
        {
            order.Add(Path.Combine(packageDir, $"{packageId}.{version}.nupkg"));
            order.Add(Path.Combine(packageDir, $"{packageId}.{version}.snupkg"));
        }

        return order;
    }

    public static long SelectRun(JsonObject runsDocument, string commit)
    {
        return SelectRunRecord(runsDocument, commit).Id;
    }

    public static (string PackageDir, string ManifestPath) LocateRelease(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new ReleaseGateException($"{root} is not a directory");
        }

        var manifests = Directory.EnumerateFiles(root, "release-manifest.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (manifests.Count != 1)
        {
            throw new ReleaseGateException("expected one release manifest");
        }

        var manifestPath = manifests[0];
        var packageDir = Path.GetDirectoryName(manifestPath) ?? root;
        var packages = Directory.EnumerateFiles(packageDir)
            .Where(path => string.Equals(Path.GetExtension(path), ".nupkg", StringComparison.Ordinal))
            .ToList();
        if (packages.Count == 0)
        {
            throw new ReleaseGateException("release manifest is not beside the packages");
        }

        return (packageDir, manifestPath);
    }

    public static void CompareRestoredPayloads(string packageDir, string cache, string version)
    {
        var shared = SharedPackagesFolder();
        var resolved = Path.GetFullPath(cache);
        if (string.Equals(resolved, shared, StringComparison.Ordinal) || IsUnder(shared, resolved))
        {
            throw new ReleaseGateException("refusing to compare against the shared NuGet global-packages folder");
        }

        foreach (var packageId in PackageIds)
        {
            var packagePath = Path.Combine(packageDir, $"{packageId}.{version}.nupkg");
            foreach (var (inner, digest) in PayloadDigests(packagePath))
            {
                var restored = Path.Combine(cache, packageId.ToLowerInvariant(), version, inner.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(restored))
                {
                    throw new ReleaseGateException($"restored payload is missing: {restored}");
                }

                var restoredDigest = Sha256(File.ReadAllBytes(restored));
                if (!string.Equals(restoredDigest, digest, StringComparison.Ordinal))
                {
                    throw new ReleaseGateException($"{packageId} {inner} restored {restoredDigest} does not match packed {digest}");
                }
            }
        }
    }

    public static void RequireNugetOrgOnly(string assetsPath, string? configPath)
    {
        var document = LoadJson(assetsPath);
        if (document["project"] is not JsonObject project || project["restore"] is not JsonObject restore)
        {
            throw new ReleaseGateException($"{assetsPath} does not restore from nuget.org");
        }

        if (restore["sources"] is not JsonObject sources || !sources.ContainsKey(NugetOrg))
        {
            throw new ReleaseGateException($"{assetsPath} does not restore from nuget.org");
        }

        foreach (var source in sources)
        {
            if (string.Equals(source.Key, NugetOrg, StringComparison.Ordinal))
            {
                continue;
            }

            if (Path.IsPathRooted(source.Key) && string.Equals(Path.GetFileName(source.Key), "library-packs", StringComparison.Ordinal))
            {
                continue;
            }

            throw new ReleaseGateException($"restore source is not nuget.org: {source.Key}");
        }

        if (configPath is null)
        {
            return;
        }

        var expected = Path.GetFullPath(configPath);
        var recorded = new List<string>();
        if (restore["configFilePaths"] is JsonArray configs)
        {
            foreach (var item in configs)
            {
                if (item is not JsonValue value || !value.TryGetValue<string>(out var text) || text is null)
                {
                    recorded.Clear();
                    break;
                }

                recorded.Add(Path.GetFullPath(text));
            }
        }

        if (recorded.Count != 1 || !string.Equals(recorded[0], expected, StringComparison.Ordinal))
        {
            throw new ReleaseGateException($"{assetsPath} did not use the nuget.org-only NuGet config");
        }
    }

    public static string PackageIdFromName(string name)
    {
        string? selected = null;
        foreach (var packageId in PackageIds)
        {
            if (!name.StartsWith(packageId + ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (selected is null || packageId.Length > selected.Length)
            {
                selected = packageId;
            }
        }

        if (selected is null)
        {
            throw new ReleaseGateException($"package file {name} is not one of the release ids");
        }

        return selected;
    }

    public static IReadOnlyDictionary<string, string> PayloadDigests(string packagePath)
    {
        var packageId = PackageIdFromName(Path.GetFileName(packagePath));
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var inner in Payloads[packageId])
        {
            var entry = archive.GetEntry(inner);
            if (entry is null)
            {
                throw new ReleaseGateException($"{Path.GetFileName(packagePath)} is missing {inner}");
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            digests[inner] = Sha256(memory.ToArray());
        }

        return digests;
    }

    public static void StageUpload(string packageDir, string manifestPath, string destination)
    {
        var manifest = LoadJson(manifestPath);
        if (manifest["packages"] is not JsonArray packages)
        {
            throw new ReleaseGateException("release manifest has no package list");
        }

        Directory.CreateDirectory(destination);
        foreach (var node in packages)
        {
            if (node is not JsonObject record)
            {
                throw new ReleaseGateException("release manifest has no package list");
            }

            var file = GetString(record, "file");
            if (file is null)
            {
                throw new ReleaseGateException("release manifest package list does not match the qualified files");
            }

            var source = Path.Combine(packageDir, file);
            if (!File.Exists(source))
            {
                throw new ReleaseGateException($"required package file is missing: {file}");
            }

            File.Copy(source, Path.Combine(destination, file), overwrite: true);
        }

        File.Copy(manifestPath, Path.Combine(destination, "release-manifest.json"), overwrite: true);
    }

    public static string ReadVersion(string propsPath)
    {
        var text = File.ReadAllText(propsPath);
        var match = Regex.Match(text, @"<Version>([^<]+)</Version>", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new ReleaseGateException($"{propsPath} has no Version");
        }

        var version = match.Groups[1].Value.Trim();
        if (!VersionPattern.IsMatch(version))
        {
            throw new ReleaseGateException($"package version {version} is not a three-part numeric version");
        }

        return version;
    }

    public static JsonObject LoadJson(string path)
    {
        JsonNode? document;
        try
        {
            document = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new JsonException(exception.Message, exception);
        }

        if (document is not JsonObject obj)
        {
            throw new ReleaseGateException($"{path} is not a JSON object");
        }

        return obj;
    }

    public static string Sha256(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static SelectedRun SelectRunRecord(JsonObject runsDocument, string commit)
    {
        RequireCommit(commit);
        if (runsDocument["workflow_runs"] is not JsonArray runs)
        {
            throw new ReleaseGateException("CI run list is missing");
        }

        if (!TryLong(runsDocument["total_count"], out var total) || total != runs.Count)
        {
            throw new ReleaseGateException("CI run list is incomplete");
        }

        var matches = new List<SelectedRun>();
        foreach (var node in runs)
        {
            if (node is not JsonObject run)
            {
                throw new ReleaseGateException("CI run list is missing");
            }

            if (GetString(run, "head_sha") != commit)
            {
                continue;
            }

            if (GetString(run, "event") != "push" || GetString(run, "head_branch") != "main")
            {
                continue;
            }

            if (GetString(run, "path") != CiWorkflow)
            {
                continue;
            }

            if (GetString(run, "status") != "completed" || GetString(run, "conclusion") != "success")
            {
                continue;
            }

            if (!TryLong(run["id"], out var id) || id == 0 || string.IsNullOrEmpty(GetString(run, "created_at")))
            {
                throw new ReleaseGateException("qualifying CI run has no id or timestamp");
            }

            matches.Add(new SelectedRun(id, GetString(run, "created_at")!));
        }

        if (matches.Count == 0)
        {
            throw new ReleaseGateException($"no successful main push of {CiWorkflow} for {commit}");
        }

        return matches.OrderByDescending(match => match.CreatedAt, StringComparer.Ordinal).First();
    }

    private static void RequireQualificationJobs(JsonObject jobsDocument, long runId)
    {
        if (jobsDocument["jobs"] is not JsonArray jobs || jobs.Count == 0)
        {
            throw new ReleaseGateException("CI job list is empty");
        }

        if (!TryLong(jobsDocument["total_count"], out var total) || total != jobs.Count)
        {
            throw new ReleaseGateException("CI job list is incomplete");
        }

        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in jobs)
        {
            if (node is not JsonObject job)
            {
                throw new ReleaseGateException("CI job list is empty");
            }

            if (!TryLong(job["run_id"], out var jobRun) || jobRun != runId)
            {
                throw new ReleaseGateException("CI job list belongs to a different run");
            }

            var name = GetString(job, "name");
            if (string.IsNullOrEmpty(name))
            {
                throw new ReleaseGateException("CI job has no name");
            }

            if (!seen.TryAdd(name, GetString(job, "conclusion") ?? string.Empty))
            {
                throw new ReleaseGateException($"CI job {name} is duplicated");
            }

            if (GetString(job, "status") != "completed" || GetString(job, "conclusion") != "success")
            {
                throw new ReleaseGateException($"CI job {name} did not succeed");
            }
        }

        foreach (var required in RequiredJobs)
        {
            if (!seen.ContainsKey(required))
            {
                throw new ReleaseGateException($"CI job {required} is missing");
            }
        }
    }

    private static List<PackageRecord> FileRecords(string packageDir, string version)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var packageId in PackageIds)
        {
            expected[$"{packageId}.{version}.nupkg"] = packageId;
            expected[$"{packageId}.{version}.snupkg"] = packageId;
        }

        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(packageDir))
        {
            var extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".nupkg", StringComparison.Ordinal)
                && !string.Equals(extension, ".snupkg", StringComparison.Ordinal))
            {
                continue;
            }

            found[Path.GetFileName(path)] = path;
        }

        var missing = expected.Keys.Except(found.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var extra = found.Keys.Except(expected.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new ReleaseGateException("required package file is missing: " + string.Join(", ", missing));
        }

        if (extra.Count > 0)
        {
            throw new ReleaseGateException("unexpected package file: " + string.Join(", ", extra));
        }

        var records = new List<PackageRecord>(expected.Count);
        foreach (var name in expected.Keys.Order(StringComparer.Ordinal))
        {
            var path = found[name];
            RejectNativeLibrary(path);
            records.Add(new PackageRecord(expected[name], name, Sha256(File.ReadAllBytes(path))));
        }

        return records;
    }

    private static void RejectNativeLibrary(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            if (BundlesOpenAl(entry.FullName))
            {
                throw new ReleaseGateException($"{Path.GetFileName(path)} bundles an OpenAL native library");
            }
        }
    }

    private static bool BundlesOpenAl(string fullName)
    {
        var name = fullName.Replace('\\', '/');
        if (name.Contains("libopenal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Contains("soft_oal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.EndsWith("openal32.dll", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.Contains("OpenAL.framework", StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireTag(string tag, string version)
    {
        if (!string.Equals(tag, "v" + version, StringComparison.Ordinal))
        {
            throw new ReleaseGateException($"tag {tag} does not match package version {version}");
        }
    }

    private static void RequireCommit(string commit)
    {
        if (!ShaPattern.IsMatch(commit))
        {
            throw new ReleaseGateException($"commit {commit} is not a full SHA");
        }
    }

    private static string SharedPackagesFolder()
    {
        return Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages"));
    }

    private static bool IsUnder(string parent, string candidate)
    {
        var prefix = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string? GetString(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return text;
    }

    private static bool TryLong(JsonNode? node, out long number)
    {
        number = 0;
        if (node is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue<JsonElement>(out var element))
        {
            if (element.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            var raw = element.GetRawText();
            if (raw.Contains('.') || raw.Contains('e') || raw.Contains('E'))
            {
                return false;
            }

            return element.TryGetInt64(out number);
        }

        if (value.TryGetValue<long>(out number))
        {
            return true;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            number = integer;
            return true;
        }

        return false;
    }

    private readonly record struct PackageRecord(string Id, string File, string Sha256);

    private readonly record struct SelectedRun(long Id, string CreatedAt);
}
