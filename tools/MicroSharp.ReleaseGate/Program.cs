using System.IO.Compression;
using System.Text.Json;

namespace MicroSharp.ReleaseGate;

/// <summary>
/// Kommandozeile der Release-Pruefung. Sie veroeffentlicht keine Pakete.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                throw new ReleaseGateException("command is required");
            }

            var command = args[0];
            var options = Parse(args);
            switch (command)
            {
                case "write-manifest":
                    ReleaseGate.WriteManifest(
                        Required(options, "packages"),
                        Required(options, "version-file"),
                        Required(options, "commit"),
                        long.Parse(Required(options, "run-id"), System.Globalization.CultureInfo.InvariantCulture),
                        Required(options, "output"));
                    break;
                case "check":
                    var paths = ReleaseGate.Check(
                        Required(options, "tag"),
                        Required(options, "commit"),
                        Required(options, "version-file"),
                        ReleaseGate.LoadJson(Required(options, "runs")),
                        ReleaseGate.LoadJson(Required(options, "jobs")),
                        ReleaseGate.LoadJson(Required(options, "manifest")),
                        Required(options, "packages"));
                    File.WriteAllText(Required(options, "push-list"), string.Join('\n', paths) + "\n");
                    break;
                case "select-run":
                    var selected = ReleaseGate.SelectRun(
                        ReleaseGate.LoadJson(Required(options, "runs")),
                        Required(options, "commit"));
                    File.WriteAllText(
                        Required(options, "output"),
                        selected.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
                    break;
                case "locate":
                    var located = ReleaseGate.LocateRelease(Required(options, "root"));
                    File.WriteAllText(Required(options, "package-dir-output"), located.PackageDir + "\n");
                    File.WriteAllText(Required(options, "manifest-output"), located.ManifestPath + "\n");
                    break;
                case "verify-cache":
                    var version = ReleaseGate.ReadVersion(Required(options, "version-file"));
                    ReleaseGate.CompareRestoredPayloads(
                        Required(options, "packages"),
                        Required(options, "cache"),
                        version);
                    var config = Optional(options, "nuget-config");
                    foreach (var assets in Repeated(options, "assets"))
                    {
                        ReleaseGate.RequireNugetOrgOnly(assets, config);
                    }

                    break;
                case "verify-packed":
                    PackedRestoreChecker.Verify(
                        Required(options, "packages"),
                        Required(options, "cache"),
                        Required(options, "version-file"),
                        Repeated(options, "assets"),
                        Repeated(options, "copy"));
                    break;
                case "stage-upload":
                    ReleaseGate.StageUpload(
                        Required(options, "packages"),
                        Required(options, "manifest"),
                        Required(options, "destination"));
                    break;
                default:
                    throw new ReleaseGateException($"unknown command {command}");
            }

            return 0;
        }
        catch (Exception exception) when (exception is ReleaseGateException
            or IOException
            or JsonException
            or InvalidDataException
            or KeyNotFoundException
            or FormatException
            or OverflowException)
        {
            Console.Error.WriteLine(string.IsNullOrEmpty(exception.Message) ? exception.GetType().Name : exception.Message);
            return 1;
        }
    }

    private static Dictionary<string, List<string>> Parse(string[] args)
    {
        var options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length < 3)
            {
                throw new ReleaseGateException($"unexpected argument {arg}");
            }

            if (index + 1 >= args.Length)
            {
                throw new ReleaseGateException($"missing value for {arg}");
            }

            var key = arg[2..];
            var value = args[++index];
            if (!options.TryGetValue(key, out var list))
            {
                list = [];
                options[key] = list;
            }

            list.Add(value);
        }

        return options;
    }

    private static string Required(Dictionary<string, List<string>> options, string name)
    {
        if (!options.TryGetValue(name, out var values) || values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            throw new ReleaseGateException($"missing --{name}");
        }

        return values[0];
    }

    private static string? Optional(Dictionary<string, List<string>> options, string name)
    {
        if (!options.TryGetValue(name, out var values))
        {
            return null;
        }

        if (values.Count != 1)
        {
            throw new ReleaseGateException($"missing --{name}");
        }

        return values[0];
    }

    private static IReadOnlyList<string> Repeated(Dictionary<string, List<string>> options, string name)
    {
        return options.TryGetValue(name, out var values) ? values : [];
    }
}
