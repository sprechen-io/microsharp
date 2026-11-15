namespace MicroSharp;

/// <summary>
/// Beschreibt geliefertes PCM: vorzeichenbehaftete 16-Bit-Abtastwerte, little-endian, ein Kanal.
/// </summary>
public sealed class PcmFormat
{
    /// <summary>Anzahl der Bytes eines Abtastwerts.</summary>
    public const int BytesPerSample = 2;

    /// <summary>16 kHz, mono, Bloecke von 20 Millisekunden.</summary>
    public static PcmFormat Default { get; } = new(16_000, TimeSpan.FromMilliseconds(20));

    /// <summary>
    /// Legt Abtastrate und Blockdauer fest. Die Blockdauer muss ganzzahlig zwischen 10 und 200 Millisekunden liegen
    /// und an der Abtastrate eine ganze Zahl von Abtastwerten ergeben.
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

    /// <summary>Abtastwerte je Sekunde.</summary>
    public int SampleRateHz { get; }

    /// <summary>Immer 1. Mehrkanalige Aufnahme wird nicht geliefert.</summary>
    public int Channels => 1;

    /// <summary>Dauer eines vollstaendigen Blocks.</summary>
    public TimeSpan ChunkDuration { get; }

    /// <summary>Abtastwerte in einem Block.</summary>
    public int SamplesPerChunk => checked((int)((long)SampleRateHz * ChunkDuration.Ticks / TimeSpan.TicksPerSecond));

    /// <summary>Bytes in einem Block.</summary>
    public int BytesPerChunk => checked(SamplesPerChunk * BytesPerSample);

    /// <summary>Inhaltstyp mit Rate, Kanalzahl und Kodierung.</summary>
    public string MediaType =>
        $"audio/pcm;rate={SampleRateHz};channels=1;encoding=signed-integer;bits=16;endian=little";

    /// <summary>Vergleicht Rate und Blockdauer.</summary>
    public bool Matches(PcmFormat other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return SampleRateHz == other.SampleRateHz && ChunkDuration == other.ChunkDuration;
    }
}
