using MicroSharp.ReleaseGate;

namespace MicroSharp.ReleaseGate.Tests;

public sealed class RepositoryLanguageTests
{
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
