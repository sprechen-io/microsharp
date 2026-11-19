using System.Buffers.Binary;

namespace MicroSharp.Processing.Tests;

public sealed class Pcm16LeWavEncoderTests
{
    [Fact]
    public void Mono_output_matches_the_canonical_header_and_owns_its_payload()
    {
        byte[] pcm = [0x00, 0x80, 0xFF, 0x7F];
        var wav = Pcm16LeWavEncoder.Encode(pcm, 16_000);
        pcm.AsSpan().Clear();

        Assert.Equal(Convert.FromHexString(
            "524946462800000057415645666D74201000000001000100803E0000007D00000200100064617461040000000080FF7F"), wav);
    }

    [Fact]
    public void Empty_input_produces_only_the_header()
    {
        var wav = Pcm16LeWavEncoder.Encode([], 16_000);

        Assert.Equal(44, wav.Length);
        Assert.Equal(36u, BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(40)));
    }

    [Fact]
    public void Stereo_preserves_interleaved_frames()
    {
        byte[] pcm = [1, 2, 3, 4, 5, 6, 7, 8];
        var wav = Pcm16LeWavEncoder.Encode(pcm, 48_000, channels: 2);

        Assert.Equal(52, wav.Length);
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(22)));
        Assert.Equal(48_000u, BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(192_000u, BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(28)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(32)));
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(40)));
        Assert.Equal(pcm, wav[44..]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void Incomplete_frames_are_rejected(int payloadLength, int channels)
    {
        Assert.Throws<ArgumentException>("pcm16Le",
            () => Pcm16LeWavEncoder.Encode(new byte[payloadLength], 16_000, channels));
    }

    [Theory]
    [InlineData(0, 1, "sampleRateHz")]
    [InlineData(-1, 1, "sampleRateHz")]
    [InlineData(16_000, 0, "channels")]
    [InlineData(16_000, -1, "channels")]
    [InlineData(16_000, 32_768, "channels")]
    [InlineData(int.MaxValue, 2, "sampleRateHz")]
    public void Invalid_or_overflowing_formats_are_rejected(int sampleRateHz, int channels, string parameter)
    {
        Assert.Throws<ArgumentOutOfRangeException>(parameter,
            () => Pcm16LeWavEncoder.Encode([], sampleRateHz, channels));
    }

    [Fact]
    public void Upper_format_boundaries_use_unsigned_riff_fields()
    {
        var highRate = Pcm16LeWavEncoder.Encode([], int.MaxValue);
        var manyChannels = Pcm16LeWavEncoder.Encode([], 1, channels: 32_767);

        Assert.Equal(uint.MaxValue - 1, BinaryPrimitives.ReadUInt32LittleEndian(highRate.AsSpan(28)));
        Assert.Equal(65_534, BinaryPrimitives.ReadUInt16LittleEndian(manyChannels.AsSpan(32)));
    }

    [Fact]
    public void Output_size_limits_are_checked_without_integer_wraparound()
    {
        var largestEvenPayload = (Array.MaxLength - 44) & ~1;

        Assert.True(Pcm16LeWavEncoder.FitsOutputLimits(largestEvenPayload));
        Assert.False(Pcm16LeWavEncoder.FitsOutputLimits(largestEvenPayload + 2));
        Assert.False(Pcm16LeWavEncoder.FitsOutputLimits(int.MaxValue - 1));
    }
}
