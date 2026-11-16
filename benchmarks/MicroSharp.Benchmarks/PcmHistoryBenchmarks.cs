using BenchmarkDotNet.Attributes;
using MicroSharp.Processing;

namespace MicroSharp.Benchmarks;

/// <summary>
/// Measures the PCM history ring and PCM16LE block analysis on CoreCLR.
/// 16000 Hz, 20 ms, 640 bytes. Setup is outside the measured calls.
/// </summary>
[MemoryDiagnoser]
public class PcmHistoryBenchmarks
{
    public const int SampleRateHz = 16_000;
    public const int ChunkDurationMs = 20;

    private static readonly PcmFormat Format = PcmFormat.Default;

    // C# 15 collection-expression capacity is supported on List<T>, not on an array.
    private static readonly List<byte> Pattern = [with(capacity: 8), 1, 2, 3, 4, 5, 6, 7, 8];

    private readonly byte[] _chunk = new byte[Format.BytesPerChunk];
    private readonly byte[] _destination = new byte[Format.BytesPerChunk];
    private PcmHistoryRing _ring = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (Format.SampleRateHz != SampleRateHz || Format.BytesPerChunk != 640)
        {
            throw new InvalidOperationException("The measured format is 16000 Hz and 20 ms.");
        }

        Pattern.CopyTo(_chunk);
        _ring = new PcmHistoryRing(Format, TimeSpan.FromSeconds(1));
        _ring.Write(_chunk);
    }

    [Benchmark]
    public int WriteHistoryAndRead()
    {
        var cursor = new PcmHistoryCursor(_ring.WriteBytePosition);
        _ring.Write(_chunk);
        var read = _ring.Read(cursor, _destination);
        if (read != _chunk.Length || _destination[0] != Pattern[0])
        {
            throw new InvalidOperationException("History read did not return the written bytes.");
        }

        return read;
    }

    [Benchmark]
    public int AnalyzePcm16Block()
    {
        var analysis = Pcm16LeBlockAnalyzer.Analyze(_chunk, Format);
        if (analysis.DurationMilliseconds != ChunkDurationMs || analysis.MeanAbsoluteAmplitude <= 0)
        {
            throw new InvalidOperationException("Block analysis did not return duration and mean absolute amplitude.");
        }

        return analysis.MeanAbsoluteAmplitude;
    }
}
