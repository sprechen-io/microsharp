using System.Buffers.Binary;
using MicroSharp.Processing;

namespace MicroSharp.Processing.Tests;

public sealed class Pcm16LeBlockAnalyzerTests
{
    [Fact]
    public void Known_samples_return_duration_and_mean_absolute_amplitude()
    {
        var analysis = Pcm16LeBlockAnalyzer.Analyze(Pcm(1000, -500, 250, -250), PcmFormat.Default);

        Assert.Equal(0.25d, analysis.DurationMilliseconds, precision: 5);
        Assert.Equal(500, analysis.MeanAbsoluteAmplitude);
    }

    [Fact]
    public void Silence_has_duration_and_zero_amplitude()
    {
        var analysis = Pcm16LeBlockAnalyzer.Analyze(new byte[640], PcmFormat.Default);

        Assert.Equal(20d, analysis.DurationMilliseconds, precision: 5);
        Assert.Equal(0, analysis.MeanAbsoluteAmplitude);
    }

    [Fact]
    public void Minimum_signed_sample_uses_a_widened_absolute_value()
    {
        var analysis = Pcm16LeBlockAnalyzer.Analyze(Pcm(short.MinValue), PcmFormat.Default);

        Assert.Equal(32768, analysis.MeanAbsoluteAmplitude);
        Assert.Equal(1000d / 16_000d, analysis.DurationMilliseconds, precision: 8);
    }

    [Fact]
    public void Rate_change_scales_duration_and_keeps_amplitude()
    {
        var pcm = Pcm(1000, -500, 250, -250);
        var slow = Pcm16LeBlockAnalyzer.Analyze(pcm, new PcmFormat(8_000, TimeSpan.FromMilliseconds(20)));
        var fast = Pcm16LeBlockAnalyzer.Analyze(pcm, PcmFormat.Default);

        Assert.Equal(0.5d, slow.DurationMilliseconds, precision: 5);
        Assert.Equal(0.25d, fast.DurationMilliseconds, precision: 5);
        Assert.Equal(slow.MeanAbsoluteAmplitude, fast.MeanAbsoluteAmplitude);
    }

    [Fact]
    public void Empty_and_partial_samples_contribute_nothing()
    {
        var empty = Pcm16LeBlockAnalyzer.Analyze([], PcmFormat.Default);
        var partial = Pcm16LeBlockAnalyzer.Analyze([0x01], PcmFormat.Default);
        var withTail = Pcm16LeBlockAnalyzer.Analyze([.. Pcm(300, -900), 0x7F], PcmFormat.Default);

        Assert.Equal(0d, empty.DurationMilliseconds);
        Assert.Equal(0, empty.MeanAbsoluteAmplitude);
        Assert.Equal(0d, partial.DurationMilliseconds);
        Assert.Equal(0, partial.MeanAbsoluteAmplitude);
        Assert.Equal(0.125d, withTail.DurationMilliseconds, precision: 5);
        Assert.Equal(600, withTail.MeanAbsoluteAmplitude);
    }

    [Fact]
    public void Little_endian_byte_order_is_explicit()
    {
        var low = Pcm16LeBlockAnalyzer.Analyze([0x01, 0x00], PcmFormat.Default);
        var high = Pcm16LeBlockAnalyzer.Analyze([0x00, 0x01], PcmFormat.Default);
        var minimum = Pcm16LeBlockAnalyzer.Analyze([0x00, 0x80], PcmFormat.Default);

        Assert.Equal(1, low.MeanAbsoluteAmplitude);
        Assert.Equal(256, high.MeanAbsoluteAmplitude);
        Assert.Equal(32768, minimum.MeanAbsoluteAmplitude);
    }

    [Fact]
    public void Integer_division_truncates_the_mean()
    {
        var analysis = Pcm16LeBlockAnalyzer.Analyze(Pcm(1, 2), PcmFormat.Default);

        Assert.Equal(1, analysis.MeanAbsoluteAmplitude);
    }

    [Fact]
    public void Null_format_is_rejected_and_analysis_allocates_nothing_after_warmup()
    {
        Assert.Throws<ArgumentNullException>(() => Pcm16LeBlockAnalyzer.Analyze([0, 0], null!));
        var pcm = Pcm(1000, -1000);
        _ = Pcm16LeBlockAnalyzer.Analyze(pcm, PcmFormat.Default);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var analysis = Pcm16LeBlockAnalyzer.Analyze(pcm, PcmFormat.Default);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1000, analysis.MeanAbsoluteAmplitude);
        Assert.Equal(0, allocated);
    }

    private static byte[] Pcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(short)];
        for (var index = 0; index < samples.Length; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * sizeof(short), sizeof(short)), samples[index]);
        }

        return bytes;
    }
}
