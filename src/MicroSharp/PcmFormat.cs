namespace MicroSharp;

/// <summary>
/// Describes delivered PCM: signed 16-bit samples, little-endian, one channel.
/// </summary>
public sealed class PcmFormat
{
    /// <summary>Number of bytes in one sample.</summary>
    public const int BytesPerSample = 2;

    /// <summary>16 kHz, mono, chunks of 20 milliseconds.</summary>
    public static PcmFormat Default { get; } = new(16_000, TimeSpan.FromMilliseconds(20));

    /// <summary>
    /// Sets the sample rate and chunk duration. The chunk duration must be a whole number of
    /// milliseconds from 10 through 200 and must contain a whole number of samples at that rate.
    /// </summary>
    public PcmFormat(int sampleRateHz, TimeSpan chunkDuration)
    {
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }

        var milliseconds = chunkDuration.TotalMilliseconds;
        if (milliseconds is < 10 or > 200 || Math.Abs(milliseconds - Math.Round(milliseconds)) > 0.001)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkDuration),
                "Chunk duration must be a whole number of milliseconds from 10 through 200.");
        }

        var wholeMilliseconds = (int)Math.Round(milliseconds);
        if ((long)sampleRateHz * wholeMilliseconds % 1000 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkDuration),
                "Chunk duration must contain a whole number of samples at the sample rate.");
        }

        SampleRateHz = sampleRateHz;
        ChunkDuration = TimeSpan.FromMilliseconds(wholeMilliseconds);
    }

    /// <summary>Samples per second.</summary>
    public int SampleRateHz { get; }

    /// <summary>Always 1. Multi-channel capture is not delivered.</summary>
    public int Channels => 1;

    /// <summary>Duration of one complete chunk.</summary>
    public TimeSpan ChunkDuration { get; }

    /// <summary>Samples in one chunk.</summary>
    public int SamplesPerChunk => checked((int)((long)SampleRateHz * ChunkDuration.Ticks / TimeSpan.TicksPerSecond));

    /// <summary>Bytes in one chunk.</summary>
    public int BytesPerChunk => checked(SamplesPerChunk * BytesPerSample);

    /// <summary>Content type with rate, channel count and encoding.</summary>
    public string MediaType =>
        $"audio/pcm;rate={SampleRateHz};channels=1;encoding=signed-integer;bits=16;endian=little";

    /// <summary>Compares rate and chunk duration.</summary>
    public bool Matches(PcmFormat other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return SampleRateHz == other.SampleRateHz && ChunkDuration == other.ChunkDuration;
    }
}
