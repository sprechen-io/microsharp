using System.Buffers.Binary;

namespace MicroSharp.Processing;

/// <summary>
/// Measures duration and mean absolute amplitude of signed 16-bit little-endian mono PCM.
/// </summary>
/// <remarks>
/// Derived from the block measurement in sprechen-io/sprechen-io commit
/// 1d603f3a75f408d1d11010cd49190e110979803b. Speech thresholds, segmentation,
/// wake words and dialog policy were not copied.
/// </remarks>
public static class Pcm16LeBlockAnalyzer
{
    /// <summary>
    /// Measures <paramref name="pcm16Le"/> at the sample rate of <paramref name="format"/>.
    /// A trailing partial sample is ignored. An empty block returns zeros.
    /// The chunk duration is not an input.
    /// </summary>
    public static PcmBlockAnalysis Analyze(ReadOnlySpan<byte> pcm16Le, PcmFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return Analyze(pcm16Le, format.SampleRateHz);
    }

    /// <summary>
    /// Measures <paramref name="pcm16Le"/> at <paramref name="sampleRateHz"/>.
    /// A trailing partial sample is ignored. An empty block returns zeros.
    /// </summary>
    public static PcmBlockAnalysis Analyze(ReadOnlySpan<byte> pcm16Le, int sampleRateHz)
    {
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRateHz),
                sampleRateHz,
                "The sample rate must be positive.");
        }

        var sampleCount = pcm16Le.Length / PcmFormat.BytesPerSample;
        if (sampleCount <= 0)
        {
            return new PcmBlockAnalysis(0d, 0);
        }

        long sumAbsolute = 0;
        for (var index = 0; index < sampleCount; index++)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(
                pcm16Le.Slice(index * PcmFormat.BytesPerSample, PcmFormat.BytesPerSample));
            sumAbsolute += Math.Abs((int)sample);
        }

        var durationMilliseconds = sampleCount * 1000d / sampleRateHz;
        var meanAbsoluteAmplitude = (int)(sumAbsolute / sampleCount);
        return new PcmBlockAnalysis(durationMilliseconds, meanAbsoluteAmplitude);
    }
}
