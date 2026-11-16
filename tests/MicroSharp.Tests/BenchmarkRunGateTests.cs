using BenchmarkDotNet.Reports;
using MicroSharp.Benchmarks;

namespace MicroSharp.Tests;

public sealed class BenchmarkRunGateTests
{
    [Fact]
    public void AnEmptyBenchmarkSelectionFails()
    {
        Assert.Equal(1, BenchmarkRunGate.ExitCode(null));
        Assert.Equal(1, BenchmarkRunGate.ExitCode([]));
    }

    [Fact]
    public void ASummaryWithoutReportsFails()
    {
        var summary = new Summary(
            "empty",
            [],
            BenchmarkDotNet.Environments.HostEnvironmentInfo.GetCurrent(),
            "/tmp/microsharp-benchmarks",
            "/tmp/microsharp-benchmarks/log.txt",
            TimeSpan.Zero,
            System.Globalization.CultureInfo.InvariantCulture,
            [],
            [],
            BenchmarkDotNet.Reports.SummaryStyle.Default);

        Assert.Equal(1, BenchmarkRunGate.ExitCode([summary]));
    }
}
