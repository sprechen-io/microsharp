using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using MicroSharp.ReleaseGate;

namespace MicroSharp.ReleaseGate.Tests;

public sealed class ReleaseGateTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Other = "fedcba9876543210fedcba9876543210fedcba98";
    private const long RunId = 43756866555;

    private readonly string _root;
    private readonly string _propsPath;
    private readonly string _packages;

    public ReleaseGateTests()
    {
        _root = Directory.CreateTempSubdirectory("microsharp-gate-").FullName;
        _propsPath = Path.Combine(_root, "Directory.Build.props");
        File.WriteAllText(_propsPath, Props());
        _packages = Path.Combine(_root, "packages");
        Directory.CreateDirectory(_packages);
        WritePackage("MicroSharp", "core-dll"u8.ToArray(), "core-symbols"u8.ToArray(), browserAssets: false);
        WritePackage("MicroSharp.Browser", "browser-dll"u8.ToArray(), "browser-symbols"u8.ToArray(), browserAssets: true);
        WritePackage("MicroSharp.Native", "native-dll"u8.ToArray(), "native-symbols"u8.ToArray(), browserAssets: false);
        WritePackage("MicroSharp.Processing", "processing-dll"u8.ToArray(), "processing-symbols"u8.ToArray(), browserAssets: false);
        WritePackage("MicroSharp.Browser.Playback", "playback-dll"u8.ToArray(), "playback-symbols"u8.ToArray(), browserAssets: false);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void AcceptsExactMainCiAndMatchingHashes()
    {
        var manifest = Manifest();
        var paths = CheckCase(Runs(Run(RunId)), Jobs(), manifest);
        string[] expected =
        [
            "MicroSharp.0.1.0.nupkg",
            "MicroSharp.0.1.0.snupkg",
            "MicroSharp.Browser.0.1.0.nupkg",
            "MicroSharp.Browser.0.1.0.snupkg",
            "MicroSharp.Native.0.1.0.nupkg",
            "MicroSharp.Native.0.1.0.snupkg",
            "MicroSharp.Processing.0.1.0.nupkg",
            "MicroSharp.Processing.0.1.0.snupkg",
            "MicroSharp.Browser.Playback.0.1.0.nupkg",
            "MicroSharp.Browser.Playback.0.1.0.snupkg",
        ];
        Assert.Equal(expected, paths.Select(path => Path.GetFileName(path) ?? string.Empty).ToArray());
    }

    [Fact]
    public void RefusesADifferentVersionTag()
    {
        var error = Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId)), Jobs(), Manifest(), tag: "v0.2.0"));
        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAShortTag()
    {
        Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId)), Jobs(), Manifest(), tag: "v0.1"));
    }

    [Fact]
    public void RefusesAnEmptyRunList()
    {
        var runs = new JsonObject { ["workflow_runs"] = new JsonArray(), ["total_count"] = 0 };
        var error = Assert.Throws<ReleaseGateException>(() => CheckCase(runs, Jobs(), Manifest(), addTotal: false));
        Assert.Contains("no successful", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnIncompleteRunList()
    {
        var runs = new JsonObject { ["workflow_runs"] = new JsonArray(Run(RunId)), ["total_count"] = 2 };
        var error = Assert.Throws<ReleaseGateException>(() => CheckCase(runs, Jobs(), Manifest(), addTotal: false));
        Assert.Contains("incomplete", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesSuccessForAnotherCommit()
    {
        var error = Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId, commit: Other)), Jobs(), Manifest()));
        Assert.Contains("no successful", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAPullRequestRun()
    {
        Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId, eventName: "pull_request")), Jobs(), Manifest()));
    }

    [Fact]
    public void RefusesANonMainBranch()
    {
        Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId, headBranch: "mic-001")), Jobs(), Manifest()));
    }

    [Fact]
    public void RefusesADifferentWorkflow()
    {
        Assert.Throws<ReleaseGateException>(() =>
            CheckCase(Runs(Run(RunId, path: ".github/workflows/nuget-release.yml")), Jobs(), Manifest()));
    }

    [Fact]
    public void RefusesAFailedRun()
    {
        Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId, conclusion: "failure")), Jobs(), Manifest()));
    }

    [Fact]
    public void UsesTheLatestSuccessAndIgnoresALaterFailure()
    {
        var older = Run(100, createdAt: "2026-11-15T20:00:00Z");
        var newerFailure = Run(200, createdAt: "2026-11-15T21:00:00Z", conclusion: "failure");
        var latest = Run(300, createdAt: "2026-11-15T20:30:00Z");
        var manifest = Manifest(runId: 300);
        var paths = CheckCase(Runs(older, newerFailure, latest), Jobs(300), manifest);
        Assert.Equal(10, paths.Count);
    }

    [Fact]
    public void RefusesAFailedNet11Job()
    {
        var error = Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId)), Jobs(net11: "failure"), Manifest()));
        Assert.Contains("net11 did not succeed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnEmptyJobList()
    {
        var error = Assert.Throws<ReleaseGateException>(() =>
            CheckCase(Runs(Run(RunId)), new JsonObject { ["jobs"] = new JsonArray() }, Manifest()));
        Assert.Contains("empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnIncompleteJobList()
    {
        var document = Jobs();
        document["total_count"] = 2;
        var error = Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId)), document, Manifest()));
        Assert.Contains("incomplete", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAShortCommit()
    {
        var error = Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId)), Jobs(), Manifest(), commit: "abc"));
        Assert.Contains("full SHA", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesJobsFromAnotherRun()
    {
        var error = Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId)), Jobs(runId: 1), Manifest()));
        Assert.Contains("different run", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAManifestForAnotherCommit()
    {
        var error = Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId)), Jobs(), Manifest(commit: Other)));
        Assert.Contains("commit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAHashMismatch()
    {
        var manifest = Manifest();
        var packages = Assert.IsType<JsonArray>(manifest["packages"]);
        var first = Assert.IsType<JsonObject>(packages[0]);
        first["sha256"] = new string('0', 64);
        var error = Assert.Throws<ReleaseGateException>(() => CheckCase(Runs(Run(RunId)), Jobs(), manifest));
        Assert.Contains("hash mismatch", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAMissingPackage()
    {
        File.Delete(Path.Combine(_packages, "MicroSharp.Native.0.1.0.nupkg"));
        var error = Assert.Throws<ReleaseGateException>(() => Manifest());
        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAMissingProcessingPackage()
    {
        File.Delete(Path.Combine(_packages, "MicroSharp.Processing.0.1.0.nupkg"));
        var error = Assert.Throws<ReleaseGateException>(() => Manifest());
        Assert.Contains("MicroSharp.Processing.0.1.0.nupkg", error.Message, StringComparison.Ordinal);
        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnExtraPackage()
    {
        var extra = Path.Combine(_packages, "MicroSharp.Extra.0.1.0.nupkg");
        using (var archive = ZipFile.Open(extra, ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry("lib/net11.0/Extra.dll").Open();
            stream.Write("x"u8);
        }

        var error = Assert.Throws<ReleaseGateException>(() => Manifest());
        Assert.Contains("unexpected", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesABundledNativeLibrary()
    {
        var path = Path.Combine(_packages, "MicroSharp.Native.0.1.0.nupkg");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            using var stream = archive.CreateEntry("runtimes/linux-x64/native/libopenal.so.1").Open();
            stream.Write("native"u8);
        }

        var error = Assert.Throws<ReleaseGateException>(() => Manifest());
        Assert.Contains("OpenAL native library", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesABundledWindowsOpenAlDll()
    {
        var path = Path.Combine(_packages, "MicroSharp.Native.0.1.0.nupkg");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            using var stream = archive.CreateEntry("runtimes/win-x64/native/openal32.dll").Open();
            stream.Write("native"u8);
        }

        var error = Assert.Throws<ReleaseGateException>(() => Manifest());
        Assert.Contains("OpenAL native library", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAMissingWindowsJob()
    {
        var error = Assert.Throws<ReleaseGateException>(() =>
            CheckCase(Runs(Run(RunId)), Jobs(nativeWindows: null), Manifest()));
        Assert.Contains("native-windows is missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFailedLinuxJobBesideSuccessfulJobs()
    {
        var error = Assert.Throws<ReleaseGateException>(() =>
            CheckCase(Runs(Run(RunId)), Jobs(nativeLinux: "failure"), Manifest()));
        Assert.Contains("native-linux did not succeed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesOneSuccessfulLegWhenAnotherRequiredJobIsMissing()
    {
        var error = Assert.Throws<ReleaseGateException>(() =>
            CheckCase(Runs(Run(RunId)), Jobs(nativeWindows: null, net11: null), Manifest()));
        Assert.Contains("is missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesADuplicatedQualificationJob()
    {
        var error = Assert.Throws<ReleaseGateException>(() =>
            CheckCase(Runs(Run(RunId)), Jobs(duplicateNet11: true), Manifest()));
        Assert.Contains("net11 is duplicated", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserFileNameKeepsItsOwnId()
    {
        Assert.Equal("MicroSharp.Browser", ReleaseGate.PackageIdFromName("MicroSharp.Browser.0.1.0.nupkg"));
        Assert.Equal("MicroSharp.Native", ReleaseGate.PackageIdFromName("MicroSharp.Native.0.1.0.snupkg"));
        Assert.Equal("MicroSharp", ReleaseGate.PackageIdFromName("MicroSharp.0.1.0.nupkg"));
        Assert.Equal("MicroSharp.Processing", ReleaseGate.PackageIdFromName("MicroSharp.Processing.0.1.0.nupkg"));
        Assert.Equal("MicroSharp.Processing", ReleaseGate.PackageIdFromName("MicroSharp.Processing.0.1.0.snupkg"));
    }

    [Fact]
    public void PayloadMatchIgnoresZipWrapperBytes()
    {
        var first = Path.Combine(_packages, "MicroSharp.0.1.0.nupkg");
        var second = Path.Combine(_root, "MicroSharp.0.1.0.nupkg");
        using (var source = ZipFile.OpenRead(first))
        using (var target = ZipFile.Open(second, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                var created = target.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                created.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var input = entry.Open();
                using var output = created.Open();
                input.CopyTo(output);
            }
        }

        Assert.NotEqual(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Equal(ReleaseGate.PayloadDigests(first), ReleaseGate.PayloadDigests(second));
    }

    [Fact]
    public void RestoredPayloadMismatchFails()
    {
        var cache = Path.Combine(_root, "cache");
        WriteCache(cache, "microsharp", "lib/net11.0/MicroSharp.dll", "core-dll"u8.ToArray());
        WriteCache(cache, "microsharp.browser", "lib/net11.0/MicroSharp.Browser.dll", "browser-dll"u8.ToArray());
        WriteCache(cache, "microsharp.browser", "staticwebassets/microsharp-capture.js", "capture-js"u8.ToArray());
        WriteCache(cache, "microsharp.browser", "staticwebassets/pcm-recorder-processor.js", "changed"u8.ToArray());
        WriteCache(cache, "microsharp.native", "lib/net11.0/MicroSharp.Native.dll", "native-dll"u8.ToArray());
        var error = Assert.Throws<ReleaseGateException>(() => ReleaseGate.CompareRestoredPayloads(_packages, cache, "0.1.0"));
        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
        Assert.Contains("pcm-recorder-processor.js", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoredProcessingPayloadMismatchFails()
    {
        var cache = Path.Combine(_root, "processing-cache");
        WriteCache(cache, "microsharp", "lib/net11.0/MicroSharp.dll", "core-dll"u8.ToArray());
        WriteCache(cache, "microsharp.browser", "lib/net11.0/MicroSharp.Browser.dll", "browser-dll"u8.ToArray());
        WriteCache(cache, "microsharp.browser", "staticwebassets/microsharp-capture.js", "capture-js"u8.ToArray());
        WriteCache(cache, "microsharp.browser", "staticwebassets/pcm-recorder-processor.js", "processor-js"u8.ToArray());
        WriteCache(cache, "microsharp.native", "lib/net11.0/MicroSharp.Native.dll", "native-dll"u8.ToArray());
        WriteCache(cache, "microsharp.processing", "lib/net11.0/MicroSharp.Processing.dll", "changed"u8.ToArray());
        var error = Assert.Throws<ReleaseGateException>(() => ReleaseGate.CompareRestoredPayloads(_packages, cache, "0.1.0"));
        Assert.Contains("MicroSharp.Processing", error.Message, StringComparison.Ordinal);
        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MicroSharp.Browser", "browser-dll", "microsharp-capture.js", "capture-js", "pcm-recorder-processor.js", "processor-js", "playback-js")]
    [InlineData("MicroSharp.Browser.Playback", "playback-dll", "microsharp-playback.js", "playback-js", "pcm-playback-processor.js", "playback-processor-js", "capture-js")]
    public void PublishedCopyIsValidatedAgainstItsOwningPackage(
        string packageId, string dll, string moduleName, string moduleContents,
        string processorName, string processorContents, string otherPackageContents)
    {
        var cache = Path.Combine(_root, "published-copy-cache");
        var id = packageId.ToLowerInvariant();
        var module = $"staticwebassets/{moduleName}";
        WriteCache(cache, id, $"lib/net11.0/{packageId}.dll", Encoding.UTF8.GetBytes(dll));
        WriteCache(cache, id, module, Encoding.UTF8.GetBytes(moduleContents));
        WriteCache(cache, id, $"staticwebassets/{processorName}", Encoding.UTF8.GetBytes(processorContents));
        var published = Path.Combine(_root, "published.js");
        File.WriteAllText(published, moduleContents);

        PackedRestoreChecker.Verify(_packages, cache, _propsPath, [], [$"{published}={module}"]);
        File.WriteAllText(published, otherPackageContents);

        var error = Assert.Throws<ReleaseGateException>(
            () => PackedRestoreChecker.Verify(_packages, cache, _propsPath, [], [$"{published}={module}"]));
        Assert.Contains("does not match packed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LocateFindsANestedManifestAndRefusesTwo()
    {
        var nested = Path.Combine(_root, "download", "microsharp-qualified-packages");
        Directory.CreateDirectory(nested);
        Manifest();
        var manifestPath = Path.Combine(nested, "release-manifest.json");
        File.Copy(Path.Combine(_root, "release-manifest.json"), manifestPath);
        foreach (var path in Directory.EnumerateFiles(_packages))
        {
            File.Copy(path, Path.Combine(nested, Path.GetFileName(path)));
        }

        var found = ReleaseGate.LocateRelease(Path.Combine(_root, "download"));
        Assert.Equal(Path.GetFullPath(nested), Path.GetFullPath(found.PackageDir));
        Assert.Equal(Path.GetFullPath(manifestPath), Path.GetFullPath(found.ManifestPath));
        var other = Path.Combine(_root, "download", "second");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "release-manifest.json"), "{}");
        var error = Assert.Throws<ReleaseGateException>(() => ReleaseGate.LocateRelease(Path.Combine(_root, "download")));
        Assert.Contains("one release manifest", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsNugetOrgBesideTheSdkLibraryPacks()
    {
        var config = Path.Combine(_root, "nuget.org-only.config");
        File.WriteAllText(config, "<configuration />");
        var assets = Path.Combine(_root, "project.assets.json");
        var document = new JsonObject
        {
            ["project"] = new JsonObject
            {
                ["restore"] = new JsonObject
                {
                    ["sources"] = new JsonObject
                    {
                        [ReleaseGate.NugetOrg] = new JsonObject(),
                        ["/usr/share/dotnet/library-packs"] = new JsonObject(),
                    },
                    ["configFilePaths"] = new JsonArray(Path.GetFullPath(config)),
                },
            },
        };
        File.WriteAllText(assets, document.ToJsonString());
        ReleaseGate.RequireNugetOrgOnly(assets, config);
    }

    [Fact]
    public void RefusesALocalFeedSource()
    {
        var assets = Path.Combine(_root, "project.assets.json");
        var document = new JsonObject
        {
            ["project"] = new JsonObject
            {
                ["restore"] = new JsonObject
                {
                    ["sources"] = new JsonObject
                    {
                        [ReleaseGate.NugetOrg] = new JsonObject(),
                        ["../artifacts/packages"] = new JsonObject(),
                    },
                },
            },
        };
        File.WriteAllText(assets, document.ToJsonString());
        var error = Assert.Throws<ReleaseGateException>(() => ReleaseGate.RequireNugetOrgOnly(assets, null));
        Assert.Contains("not nuget.org", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CliCheckWritesOnlyAMatchingPushList()
    {
        Manifest();
        var runs = Path.Combine(_root, "runs.json");
        File.WriteAllText(runs, Runs(Run(RunId)).ToJsonString());
        var jobs = Path.Combine(_root, "jobs.json");
        File.WriteAllText(jobs, Jobs().ToJsonString());
        var push = Path.Combine(_root, "push.txt");
        var common = new List<string>
        {
            "check",
            "--commit", Commit,
            "--version-file", _propsPath,
            "--runs", runs,
            "--jobs", jobs,
            "--manifest", Path.Combine(_root, "release-manifest.json"),
            "--packages", _packages,
            "--push-list", push,
        };
        var error = new StringWriter();
        var previous = Console.Error;
        Console.SetError(error);
        try
        {
            var bad = Program.Main([.. common, "--tag", "v0.2.0"]);
            Assert.Equal(1, bad);
            Assert.False(File.Exists(push));
            error.GetStringBuilder().Clear();
            var good = Program.Main([.. common, "--tag", "v0.1.0"]);
            Assert.Equal(0, good);
        }
        finally
        {
            Console.SetError(previous);
        }

        Assert.Equal(10, File.ReadAllLines(push).Length);
    }

    [Fact]
    public void WorkflowChecksHashesBeforeRequestingAToken()
    {
        var root = RepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, ".github", "workflows", "nuget-release.yml"));
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        Assert.DoesNotContain("--skip-duplicate", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet pack", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fb7a1a913e98bec8097babf5755eaba61f609110", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0b31e45affc7cd5d2b07ac830cf99ebb39523245", text, StringComparison.Ordinal);
        Assert.DoesNotContain("54ab7b5df39481153afb1ed60767b196cb33e04a", text, StringComparison.Ordinal);
        Assert.Contains("NuGet/login@v1", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Setup .NET", StringComparison.Ordinal) < text.IndexOf("Qualify the tagged commit", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Qualify the tagged commit", StringComparison.Ordinal) < text.IndexOf("NuGet login", StringComparison.Ordinal));
        Assert.Contains("id-token: write", text, StringComparison.Ordinal);
        Assert.Contains("actions: read", text, StringComparison.Ordinal);
        Assert.Contains("verify-cache", text, StringComparison.Ordinal);
        Assert.Contains("https://api.nuget.org/v3/index.json", text, StringComparison.Ordinal);
        Assert.Contains("--no-symbols", text, StringComparison.Ordinal);
        Assert.Contains("microsharp-qualified-packages", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NUGET_API_KEY:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--skip-duplicate", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("runner.temp", ci, StringComparison.Ordinal);
        Assert.Contains("actions: write", ci, StringComparison.Ordinal);
        Assert.Contains(
            "dotnet test tests/MicroSharp.ReleaseGate.Tests/MicroSharp.ReleaseGate.Tests.csproj --configuration Release",
            ci,
            StringComparison.Ordinal);
        Assert.Contains("microsharp-qualified-packages", ci, StringComparison.Ordinal);
        Assert.Contains("refs/heads/main", ci, StringComparison.Ordinal);
        Assert.Contains("native-linux", ci, StringComparison.Ordinal);
        Assert.Contains("native-windows", ci, StringComparison.Ordinal);
        Assert.Contains("needs: [native-linux, native-windows]", ci, StringComparison.Ordinal);
        Assert.Contains("Category!=VirtualDevice", ci, StringComparison.Ordinal);
        Assert.Contains("MICROSHARP_REQUIRE_VIRTUAL_CAPTURE", ci, StringComparison.Ordinal);
        Assert.Contains("install-openal-soft-windows.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("--job Dry", ci, StringComparison.Ordinal);
        Assert.Contains("NoSuchMeasurement", ci, StringComparison.Ordinal);
        Assert.Contains("microsharp_virtual_capture_stop", ci, StringComparison.Ordinal);
        Assert.Contains(
            "HasCriticalValidationErrors",
            File.ReadAllText(Path.Combine(root, "benchmarks", "MicroSharp.Benchmarks", "BenchmarkRunGate.cs")),
            StringComparison.Ordinal);
        Assert.DoesNotContain("openal32.dll", File.ReadAllText(Path.Combine(root, "src", "MicroSharp.Native", "MicroSharp.Native.csproj")), StringComparison.Ordinal);
        var benchmarks = File.ReadAllText(Path.Combine(root, ".github", "workflows", "benchmarks.yml"));
        Assert.Contains("workflow_dispatch", benchmarks, StringComparison.Ordinal);
        Assert.DoesNotContain("push:", benchmarks, StringComparison.Ordinal);
        Assert.Contains("ubuntu-latest", benchmarks, StringComparison.Ordinal);
        Assert.Contains("windows-latest", benchmarks, StringComparison.Ordinal);
        Assert.Contains("IsPackable>false", File.ReadAllText(Path.Combine(root, "benchmarks", "MicroSharp.Benchmarks", "MicroSharp.Benchmarks.csproj")), StringComparison.Ordinal);
    }

    internal static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MicroSharp.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("repository root was not found");
    }

    private JsonObject Manifest(long runId = RunId, string commit = Commit)
    {
        return ReleaseGate.WriteManifest(_packages, _propsPath, commit, runId, Path.Combine(_root, "release-manifest.json"));
    }

    private IReadOnlyList<string> CheckCase(
        JsonObject runs,
        JsonObject jobs,
        JsonObject manifest,
        string tag = "v0.1.0",
        string commit = Commit,
        bool addTotal = true)
    {
        if (addTotal && runs["total_count"] is null && runs["workflow_runs"] is JsonArray listed)
        {
            runs["total_count"] = listed.Count;
        }

        return ReleaseGate.Check(tag, commit, _propsPath, runs, jobs, manifest, _packages);
    }

    private static JsonObject Runs(params JsonObject[] items)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(item);
        }

        return new JsonObject
        {
            ["workflow_runs"] = array,
            ["total_count"] = array.Count,
        };
    }

    private static JsonObject Run(
        long runId,
        string commit = Commit,
        string? createdAt = null,
        string? eventName = null,
        string? headBranch = null,
        string? path = null,
        string? conclusion = null)
    {
        return new JsonObject
        {
            ["id"] = runId,
            ["head_sha"] = commit,
            ["event"] = eventName ?? "push",
            ["head_branch"] = headBranch ?? "main",
            ["path"] = path ?? ReleaseGate.CiWorkflow,
            ["status"] = "completed",
            ["conclusion"] = conclusion ?? "success",
            ["created_at"] = createdAt ?? $"2026-11-15T20:18:{runId % 60:00}Z",
        };
    }

    private static JsonObject Jobs(
        long runId = RunId,
        string? net11 = "success",
        string? nativeLinux = "success",
        string? nativeWindows = "success",
        bool duplicateNet11 = false)
    {
        var rows = new JsonArray();
        Add(rows, "native-linux", nativeLinux, runId);
        Add(rows, "native-windows", nativeWindows, runId);
        Add(rows, "net11", net11, runId);
        if (duplicateNet11)
        {
            Add(rows, "net11", "success", runId);
        }

        return new JsonObject
        {
            ["jobs"] = rows,
            ["total_count"] = rows.Count,
        };

        static void Add(JsonArray rows, string name, string? conclusion, long runId)
        {
            if (conclusion is null)
            {
                return;
            }

            rows.Add(new JsonObject
            {
                ["name"] = name,
                ["run_id"] = runId,
                ["status"] = "completed",
                ["conclusion"] = conclusion,
            });
        }
    }

    private void WritePackage(string packageId, byte[] dll, byte[] symbols, bool browserAssets)
    {
        var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [$"lib/net11.0/{packageId}.dll"] = dll,
            ["lib/net11.0/_._"] = [],
        };
        if (browserAssets)
        {
            payload["staticwebassets/microsharp-capture.js"] = "capture-js"u8.ToArray();
            payload["staticwebassets/pcm-recorder-processor.js"] = "processor-js"u8.ToArray();
        }

        if (packageId == "MicroSharp.Browser.Playback")
        {
            payload["staticwebassets/microsharp-playback.js"] = "playback-js"u8.ToArray();
            payload["staticwebassets/pcm-playback-processor.js"] = "playback-processor-js"u8.ToArray();
        }

        foreach (var symbolsPackage in new[] { false, true })
        {
            var suffix = symbolsPackage ? ".snupkg" : ".nupkg";
            var path = Path.Combine(_packages, $"{packageId}.0.1.0{suffix}");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (name, data) in payload)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(symbolsPackage ? symbols : data);
            }

            if (symbolsPackage)
            {
                using (var stream = archive.CreateEntry("symbol.txt").Open())
                {
                    stream.Write(symbols);
                }

                if (packageId == "MicroSharp.Browser.Playback")
                {
                    using var pdb = archive.CreateEntry("lib/net11.0/MicroSharp.Browser.Playback.pdb").Open();
                    pdb.Write("BSJB"u8);
                    pdb.Write(symbols);
                }
            }
        }
    }

    private static void WriteCache(string cache, string packageId, string inner, byte[] data)
    {
        var path = Path.Combine(cache, packageId, "0.1.0", inner.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    private static string Props(string version = "0.1.0")
    {
        return $"<Project><PropertyGroup><Version>{version}</Version></PropertyGroup></Project>";
    }
}
