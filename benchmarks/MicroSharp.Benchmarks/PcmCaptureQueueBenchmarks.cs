using BenchmarkDotNet.Attributes;

namespace MicroSharp.Benchmarks;

/// <summary>
/// Measures the shared PCM queue on CoreCLR.
/// 16000 Hz, 20 ms, 640 bytes. DropOldest capacity 50. Wait capacity 1.
/// </summary>
[MemoryDiagnoser]
public class PcmCaptureQueueBenchmarks
{
    public const int SampleRateHz = 16_000;
    public const int ChunkDurationMs = 20;
    public const int DropOldestCapacity = 50;
    public const int WaitCapacity = 1;

    private static readonly PcmFormat Format = PcmFormat.Default;

    private readonly byte[] _source = new byte[Format.BytesPerChunk];
    private MicrophoneSession _flow = null!;
    private MicrophoneSession _drop = null!;
    private MicrophoneSession _wait = null!;
    private MicrophoneSession _cancel = null!;
    private long _flowSequence;
    private byte _waitStamp;
    private byte _waitExpected;

    [GlobalSetup]
    public void Setup()
    {
        if (Format.SampleRateHz != SampleRateHz || Format.BytesPerChunk != 640)
        {
            throw new InvalidOperationException("The measured format is 16000 Hz and 20 ms.");
        }

        _source[0] = 0x5A;
        _flow = new MicrophoneSession(Format, CaptureBackpressure.DropOldest, DropOldestCapacity, TimeProvider.System);
        _flowSequence = 0;
        _drop = new MicrophoneSession(Format, CaptureBackpressure.DropOldest, DropOldestCapacity, TimeProvider.System);
        for (var i = 0; i < DropOldestCapacity; i++)
        {
            Write(_drop, _source);
        }

        if (_drop.DroppedChunkCount != 0)
        {
            throw new InvalidOperationException("Filling the queue to its capacity discarded a chunk.");
        }

        _waitStamp = 1;
        _waitExpected = 1;
        _source[0] = _waitStamp;
        _wait = new MicrophoneSession(Format, CaptureBackpressure.Wait, WaitCapacity, TimeProvider.System);
        Write(_wait, _source);
        _source[0] = 0x5A;
        _cancel = new MicrophoneSession(Format, CaptureBackpressure.Wait, WaitCapacity, TimeProvider.System);
        Write(_cancel, _source);
    }

    [Benchmark]
    public int CopyEnqueueAndRead()
    {
        const byte stamp = 0x5A;
        _source[0] = stamp;
        Write(_flow, _source);
        _source[0] = stamp + 1;
        var chunk = ReadOne(_flow);
        if (ReferenceEquals(chunk.Payload, _source)
            || chunk.Payload.Length != _source.Length
            || chunk.Payload[0] != stamp
            || chunk.Sequence != _flowSequence)
        {
            throw new InvalidOperationException("Enqueue did not keep an owned copy in order.");
        }

        _flowSequence++;
        return chunk.Payload[0];
    }

    [Benchmark]
    public long DropOldestWhenFull()
    {
        var before = _drop.DroppedChunkCount;
        Write(_drop, _source);
        var after = _drop.DroppedChunkCount;
        if (after != before + 1)
        {
            throw new InvalidOperationException("A full DropOldest queue did not count one discarded chunk.");
        }

        return after;
    }

    [Benchmark]
    public async Task<int> WaitUntilRead()
    {
        _waitStamp++;
        _source[0] = _waitStamp;
        var write = _wait.WriteAsync(_source, CancellationToken.None).AsTask();
        if (write.IsCompleted)
        {
            throw new InvalidOperationException("The waiting write completed before a read.");
        }

        var chunk = await ReadOneAsync(_wait).ConfigureAwait(false);
        await write.ConfigureAwait(false);
        if (chunk.Payload.Length != Format.BytesPerChunk
            || chunk.Payload[0] != _waitExpected
            || _wait.DroppedChunkCount != 0)
        {
            throw new InvalidOperationException("Wait backpressure did not release the oldest owned chunk.");
        }

        _waitExpected = _waitStamp;
        return chunk.Payload[0];
    }

    [Benchmark]
    public async Task<int> CancelWaitingWrite()
    {
        using var cts = new CancellationTokenSource();
        var write = _cancel.WriteAsync(_source, cts.Token).AsTask();
        if (write.IsCompleted)
        {
            throw new InvalidOperationException("The cancellable write did not wait.");
        }

        cts.Cancel();
        try
        {
            await write.ConfigureAwait(false);
            throw new InvalidOperationException("The cancelled write completed.");
        }
        catch (OperationCanceledException)
        {
        }

        if (_cancel.DroppedChunkCount != 0)
        {
            throw new InvalidOperationException("Cancelling a waiting write discarded a chunk.");
        }

        return 1;
    }

    private static void Write(MicrophoneSession session, byte[] payload)
    {
        session.WriteAsync(payload, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    private static PcmChunk ReadOne(MicrophoneSession session)
    {
        var reader = session.ReadChunksAsync().GetAsyncEnumerator();
        try
        {
            if (!reader.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                throw new InvalidOperationException("The queue did not return a chunk.");
            }

            return reader.Current;
        }
        finally
        {
            reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static async Task<PcmChunk> ReadOneAsync(MicrophoneSession session)
    {
        var reader = session.ReadChunksAsync().GetAsyncEnumerator();
        try
        {
            if (!await reader.MoveNextAsync().ConfigureAwait(false))
            {
                throw new InvalidOperationException("The queue did not return a chunk.");
            }

            return reader.Current;
        }
        finally
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }
    }
}
