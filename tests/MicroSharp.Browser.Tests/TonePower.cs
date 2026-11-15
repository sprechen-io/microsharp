namespace MicroSharp.Browser.Tests;

internal static class TonePower
{
    public static double At(ReadOnlySpan<short> samples, int sampleRate, double frequencyHz)
    {
        var coefficient = 2d * Math.Cos(2d * Math.PI * frequencyHz / sampleRate);
        double previous = 0;
        double prior = 0;
        foreach (var sample in samples)
        {
            var current = sample + (coefficient * previous) - prior;
            prior = previous;
            previous = current;
        }

        return (previous * previous) + (prior * prior) - (coefficient * previous * prior);
    }
}
