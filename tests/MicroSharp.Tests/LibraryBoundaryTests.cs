namespace MicroSharp.Tests;

public sealed class LibraryBoundaryTests
{
    [Fact]
    public void LibrarySourcesDoNotReferenceTheSpeechApplication()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();
        foreach (var directory in new[] { "src", "samples", "benchmarks", "tests" })
        {
            var path = Path.Combine(root, directory);
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var extension = Path.GetExtension(file);
                if (extension is not (".cs" or ".csproj" or ".razor" or ".js" or ".props"))
                {
                    continue;
                }

                var text = WithoutComments(File.ReadAllText(file), extension);
                var applicationNamespace = "Sprech" + "en.";
                if (text.Contains(applicationNamespace, StringComparison.Ordinal))
                {
                    violations.Add(Path.GetRelativePath(root, file));
                }
            }
        }

        Assert.Empty(violations);
    }

    private static string WithoutComments(string text, string extension)
    {
        if (extension is ".csproj" or ".props")
        {
            return text;
        }

        var builder = new System.Text.StringBuilder();
        var block = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            if (block)
            {
                var end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0)
                {
                    continue;
                }

                line = line[(end + 2)..];
                block = false;
            }

            var comment = line.IndexOf("//", StringComparison.Ordinal);
            var blockStart = line.IndexOf("/*", StringComparison.Ordinal);
            if (blockStart >= 0 && (comment < 0 || blockStart < comment))
            {
                var end = line.IndexOf("*/", blockStart + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    line = line[..blockStart];
                    block = true;
                }
                else
                {
                    line = string.Concat(line.AsSpan(0, blockStart), line.AsSpan(end + 2));
                }
            }

            comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
            {
                line = line[..comment];
            }

            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MicroSharp.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("The MicroSharp repository root was not found.");
    }
}
