namespace MicroSharp.Tests;

public sealed class PcmFormatTests
{
    [Fact]
    public void DefaultFormatIs16KilohertzMono20Milliseconds()
    {
        var format = PcmFormat.Default;

        Assert.Equal(16_000, format.SampleRateHz);
        Assert.Equal(1, format.Channels);
        Assert.Equal(TimeSpan.FromMilliseconds(20), format.ChunkDuration);
        Assert.Equal(320, format.SamplesPerChunk);
        Assert.Equal(640, format.BytesPerChunk);
        Assert.Contains("audio/pcm;rate=16000", format.MediaType, StringComparison.Ordinal);
        Assert.Contains("endian=little", format.MediaType, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(201)]
    public void ChunkDurationOutsideTheSupportedRangeIsRejected(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcmFormat(16_000, TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void SampleRateOutsideTheAdapterSetIsRejected()
    {
        var format = new PcmFormat(22_050, TimeSpan.FromMilliseconds(20));

        var error = Assert.Throws<MicrophoneException>(() => MicrophoneFormats.EnsureSupported(format));

        Assert.Equal(MicrophoneErrorCode.UnsupportedFormat, error.Code);
    }

    [Fact]
    public void EncodingRoundTripPreservesSamples()
    {
        short[] samples = [0, 1, -1, short.MaxValue, short.MinValue];

        var restored = PcmEncoding.ToSamples(PcmEncoding.FromSamples(samples));

        Assert.Equal(samples, restored);
    }
}
