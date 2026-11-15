using MicroSharp.ReleaseGate;

namespace MicroSharp.ReleaseGate.Tests;

public sealed class RepositoryLanguageTests
{
    [Fact]
    public void RepositoryDoesNotShipPythonFilesOrInvokeThatRuntime()
    {
        var root = ReleaseGateTests.RepositoryRoot();
        var failures = new List<string>();
        var skipped = new HashSet<string>(StringComparer.Ordinal)
        {
            ".git", "bin", "obj", "artifacts", ".playwright", "node_modules", "TestResults", ".vs",
        };
        Walk(new DirectoryInfo(root));
        Assert.True(failures.Count == 0, string.Join('\n', failures));

        void Walk(DirectoryInfo directory)
        {
            foreach (var child in directory.EnumerateDirectories())
            {
                if (child.Name is "__pycache__" or ".pytest_cache")
                {
                    failures.Add(child.FullName);
                    continue;
                }

                if (skipped.Contains(child.Name))
                {
                    continue;
                }

                Walk(child);
            }

            foreach (var file in directory.EnumerateFiles())
            {
                if (file.Extension is ".py" or ".pyc" or ".pyo")
                {
                    failures.Add(file.FullName);
                    continue;
                }

                if (file.Extension is not ".yml" and not ".yaml" and not ".sh" and not ".ps1")
                {
                    continue;
                }

                var text = File.ReadAllText(file.FullName);
                if (ReleaseGateTests.PythonInvocation().IsMatch(text))
                {
                    failures.Add(file.FullName);
                }
            }
        }
    }

    [Fact]
    public void RefusesTheSharedPackageCache()
    {
        var root = Directory.CreateTempSubdirectory("microsharp-packed-").FullName;
        try
        {
            var props = Path.Combine(root, "Directory.Build.props");
            File.WriteAllText(props, "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>");
            var shared = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
            var error = Assert.Throws<ReleaseGateException>(() => PackedRestoreChecker.Verify(root, shared, props, [], []));
            Assert.Contains("shared NuGet", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
