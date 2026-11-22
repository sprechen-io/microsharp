using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace MicroSharp.ReleaseGate;

/// <summary>
/// Compares the payloads just packed with a fresh private NuGet cache.
/// </summary>
public static class PackedRestoreChecker
{
    private static readonly Dictionary<string, string[]> Packages = new(StringComparer.Ordinal)
    {
        ["MicroSharp"] = ["lib/net11.0/MicroSharp.dll"],
        ["MicroSharp.Native"] = ["lib/net11.0/MicroSharp.Native.dll"],
        ["MicroSharp.Browser"] =
        [
            "lib/net11.0/MicroSharp.Browser.dll",
            "staticwebassets/microsharp-capture.js",
            "staticwebassets/pcm-recorder-processor.js",
        ],
        ["MicroSharp.Processing"] = ["lib/net11.0/MicroSharp.Processing.dll"],
        ["MicroSharp.Browser.Playback"] =
        [
            "lib/net11.0/MicroSharp.Browser.Playback.dll",
            "staticwebassets/microsharp-playback.js",
            "staticwebassets/pcm-playback-processor.js",
        ],
    };

    private static readonly Dictionary<string, string[]> RequiredByAssets = new(StringComparer.Ordinal)
    {
        ["MicroSharp.Console"] = ["MicroSharp", "MicroSharp.Native", "MicroSharp.Processing"],
        ["MicroSharp.BlazorWasm"] = ["MicroSharp", "MicroSharp.Browser", "MicroSharp.Processing"],
        ["MicroSharp.PlaybackWasm"] = ["MicroSharp.Browser.Playback"],
    };

    public static void Verify(
        string packageDir,
        string cache,
        string versionFile,
        IReadOnlyList<string> assets,
        IReadOnlyList<string> copies)
    {
        var version = ReleaseGate.ReadVersion(versionFile);
        var resolvedCache = Path.GetFullPath(cache);
        var shared = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages"));
        var sharedPrefix = shared.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (string.Equals(resolvedCache, shared, StringComparison.Ordinal)
            || resolvedCache.StartsWith(sharedPrefix, StringComparison.Ordinal))
        {
            throw new ReleaseGateException("refusing to treat the shared NuGet global-packages folder as this run's cache");
        }

        if (!Directory.Exists(resolvedCache))
        {
            throw new ReleaseGateException($"private package cache is missing: {resolvedCache}");
        }

        var sampleForAssets = new List<(string Path, string Sample)>();
        var requiredIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assetsPath in assets)
        {
            string? sampleName = null;
            foreach (var name in RequiredByAssets.Keys)
            {
                if (assetsPath.Contains(name, StringComparison.Ordinal))
                {
                    sampleName = name;
                    break;
                }
            }

            if (sampleName is null)
            {
                throw new ReleaseGateException($"no package requirement is defined for {assetsPath}");
            }

            sampleForAssets.Add((assetsPath, sampleName));
            foreach (var packageId in RequiredByAssets[sampleName])
            {
                requiredIds.Add(packageId);
            }
        }

        var publishedCopies = new List<(string PackageId, string Written, string Inner)>();
        foreach (var item in copies)
        {
            var split = item.IndexOf('=', StringComparison.Ordinal);
            if (split <= 0)
            {
                throw new ReleaseGateException($"published asset is missing: {item}");
            }

            var written = item[..split];
            var inner = item[(split + 1)..];
            var owners = Packages.Where(package => package.Value.Contains(inner, StringComparer.Ordinal)).ToArray();
            if (!inner.StartsWith("staticwebassets/", StringComparison.Ordinal) || owners.Length != 1)
            {
                throw new ReleaseGateException($"no unique packed browser asset is named {inner}");
            }

            var packageId = owners[0].Key;
            requiredIds.Add(packageId);
            publishedCopies.Add((packageId, written, inner));
        }

        var innerHash = new Dictionary<(string PackageId, string Inner), string>();
        var packageSha512 = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var packageId in requiredIds.Order(StringComparer.Ordinal))
        {
            var nupkg = Path.Combine(packageDir, $"{packageId}.{version}.nupkg");
            if (!File.Exists(nupkg))
            {
                throw new ReleaseGateException($"packed file is missing: {nupkg}");
            }

            var blob = File.ReadAllBytes(nupkg);
            packageSha512[packageId] = Convert.ToBase64String(SHA512.HashData(blob));
            Console.WriteLine($"nupkg {Path.GetFileName(nupkg)} sha256 {ReleaseGate.Sha256(blob)}");
            using var archive = ZipFile.OpenRead(nupkg);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.Contains("libopenal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ReleaseGateException($"{Path.GetFileName(nupkg)} bundles libopenal");
                }
            }

            foreach (var inner in Packages[packageId])
            {
                var entry = archive.GetEntry(inner) ?? throw new ReleaseGateException($"{Path.GetFileName(nupkg)} is missing {inner}");
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var digest = ReleaseGate.Sha256(memory.ToArray());
                innerHash[(packageId, inner)] = digest;
                var cached = Path.Combine(resolvedCache, packageId.ToLowerInvariant(), version, inner.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(cached))
                {
                    throw new ReleaseGateException($"private cache is missing {cached}");
                }

                var cachedDigest = ReleaseGate.Sha256(File.ReadAllBytes(cached));
                if (!string.Equals(cachedDigest, digest, StringComparison.Ordinal))
                {
                    throw new ReleaseGateException($"{inner} cache {cachedDigest} does not match nupkg {digest}");
                }

                Console.WriteLine($"bound {packageId} {inner} sha256 {digest}");
            }
        }

        foreach (var (assetsPath, sampleName) in sampleForAssets)
        {
            var document = ReleaseGate.LoadJson(assetsPath);
            var folders = new HashSet<string>(StringComparer.Ordinal);
            if (document["packageFolders"] is JsonObject packageFolders)
            {
                foreach (var folder in packageFolders)
                {
                    folders.Add(Path.GetFullPath(folder.Key));
                }
            }

            if (!folders.Contains(resolvedCache))
            {
                var listed = string.Join(", ", folders.Order(StringComparer.Ordinal));
                throw new ReleaseGateException($"{assetsPath} did not restore into {resolvedCache}; folders={listed}");
            }

            if (document["libraries"] is not JsonObject libraries)
            {
                throw new ReleaseGateException($"{assetsPath} does not reference a library");
            }

            var expectedIds = RequiredByAssets[sampleName];
            foreach (var (libraryName, _) in libraries)
            {
                var separator = libraryName.IndexOf('/', StringComparison.Ordinal);
                var id = separator < 0 ? libraryName : libraryName[..separator];
                if (Packages.ContainsKey(id) && !expectedIds.Contains(id, StringComparer.Ordinal))
                {
                    throw new ReleaseGateException($"{assetsPath} unexpectedly references {id}");
                }
            }

            foreach (var packageId in expectedIds)
            {
                if (libraries[$"{packageId}/{version}"] is not JsonObject library)
                {
                    throw new ReleaseGateException($"{assetsPath} does not reference {packageId}/{version}");
                }

                var recorded = library["sha512"] is JsonValue shaValue && shaValue.TryGetValue<string>(out var sha) ? sha : null;
                if (!string.Equals(recorded, packageSha512[packageId], StringComparison.Ordinal))
                {
                    throw new ReleaseGateException(
                        $"{assetsPath} sha512 for {packageId} is {recorded}, packed file is {packageSha512[packageId]}");
                }

                Console.WriteLine($"assets {sampleName} {packageId}/{version} sha512 matches the packed file");
            }
        }

        foreach (var (packageId, written, inner) in publishedCopies)
        {
            if (!File.Exists(written))
            {
                throw new ReleaseGateException($"published asset is missing: {written}");
            }

            var digest = ReleaseGate.Sha256(File.ReadAllBytes(written));
            if (!innerHash.TryGetValue((packageId, inner), out var expected))
            {
                throw new ReleaseGateException($"no packed browser asset is named {inner}");
            }

            if (!string.Equals(digest, expected, StringComparison.Ordinal))
            {
                throw new ReleaseGateException($"{written} sha256 {digest} does not match packed {inner} {expected}");
            }

            Console.WriteLine($"published {Path.GetFileName(written)} sha256 {digest} matches packed {inner}");
        }
    }
}
