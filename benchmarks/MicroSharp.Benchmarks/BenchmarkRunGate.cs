using BenchmarkDotNet.Reports;

namespace MicroSharp.Benchmarks;

internal static class BenchmarkRunGate
{
    public static int ExitCode(IEnumerable<Summary>? summaries)
    {
        if (summaries is null)
        {
            return 1;
        }

        var any = false;
        foreach (var summary in summaries)
        {
            any = true;
            if (summary.HasCriticalValidationErrors || summary.Reports.IsDefaultOrEmpty)
            {
                return 1;
            }

            foreach (var report in summary.Reports)
            {
                if (!report.Success || report.AllMeasurements.Count == 0)
                {
                    return 1;
                }
            }
        }

        return any ? 0 : 1;
    }
}
