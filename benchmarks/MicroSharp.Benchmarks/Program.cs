using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;
using MicroSharp.Benchmarks;

var artifacts = Environment.GetEnvironmentVariable("MICROSHARP_BENCHMARK_ARTIFACTS");
if (string.IsNullOrWhiteSpace(artifacts))
{
    artifacts = Path.Combine("artifacts", "benchmarks");
}

artifacts = Path.GetFullPath(artifacts);
var config = ManualConfig.Create(DefaultConfig.Instance)
    .WithArtifactsPath(artifacts)
    .AddExporter(MarkdownExporter.GitHub)
    .AddExporter(JsonExporter.Full);

var exitCode = 1;
try
{
    var summaries = BenchmarkSwitcher.FromAssembly(typeof(PcmCaptureQueueBenchmarks).Assembly).Run(args, config);
    exitCode = BenchmarkRunGate.ExitCode(summaries);
    if (exitCode != 0)
    {
        Console.Error.WriteLine("The benchmark run produced no successful measurement.");
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    exitCode = 1;
}
finally
{
    Directory.CreateDirectory(artifacts);
    var metadata = new Dictionary<string, object?>
    {
        ["measuredAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
        ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
        ["processArchitecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        ["runtimeKind"] = "CoreCLR",
        ["browserWasmMeasured"] = false,
        ["sampleRateHz"] = PcmCaptureQueueBenchmarks.SampleRateHz,
        ["chunkDurationMs"] = PcmCaptureQueueBenchmarks.ChunkDurationMs,
        ["bytesPerChunk"] = 640,
        ["dropOldestCapacityChunks"] = PcmCaptureQueueBenchmarks.DropOldestCapacity,
        ["waitCapacityChunks"] = PcmCaptureQueueBenchmarks.WaitCapacity,
        ["note"] = "CoreCLR measurements of the shared PCM queue. Browser and WebAssembly capture are not measured here.",
    };
    var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(artifacts, "runtime-metadata.json"), json + "\n");
}

return exitCode;
