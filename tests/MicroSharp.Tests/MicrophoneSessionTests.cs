namespace MicroSharp.Tests;

public sealed class MicrophoneSessionTests
{
    [Fact]
    public async Task ReadReturnsChunksInOrder()
    {
        await using var session = CreateSession(capacity: 4);
        await session.WriteAsync(Bytes(1), CancellationToken.None);
        await session.WriteAsync(Bytes(2), CancellationToken.None);
        session.Complete();

        var chunks = await ReadAllAsync(session);

        Assert.Equal([0L, 1L], chunks.Select(chunk => chunk.Sequence).ToArray());
        Assert.Equal(1, chunks[0].Payload[0]);
        Assert.Equal(2, chunks[1].Payload[0]);
        Assert.Equal(MicrophoneSessionState.Stopped, session.State);
    }

    [Fact]
    public async Task DropOldestCountsDiscardedChunks()
    {
        await using var session = CreateSession(capacity: 2, CaptureBackpressure.DropOldest);
        await session.WriteAsync(Bytes(1), CancellationToken.None);
        await session.WriteAsync(Bytes(2), CancellationToken.None);
        await session.WriteAsync(Bytes(3), CancellationToken.None);
        await session.WriteAsync(Bytes(4), CancellationToken.None);
        session.Complete();

        var chunks = await ReadAllAsync(session);

        Assert.Equal(2, session.DroppedChunkCount);
        Assert.Equal([2L, 3L], chunks.Select(chunk => chunk.Sequence).ToArray());
        Assert.Equal(3, chunks[0].Payload[0]);
        Assert.Equal(4, chunks[1].Payload[0]);
    }

    [Fact]
    public async Task WaitReleasesTheWriterWhenAChunkIsRead()
    {
        await using var session = CreateSession(capacity: 1, CaptureBackpressure.Wait);
        await session.WriteAsync(Bytes(1), CancellationToken.None);
        var secondWrite = session.WriteAsync(Bytes(2), CancellationToken.None).AsTask();
        Assert.False(secondWrite.IsCompleted);

        var reader = session.ReadChunksAsync().GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(1, reader.Current.Payload[0]);
        await secondWrite;
        session.Complete();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(2, reader.Current.Payload[0]);
        Assert.False(await reader.MoveNextAsync());
        await reader.DisposeAsync();

        Assert.Equal(0, session.DroppedChunkCount);
    }

    [Fact]
    public async Task CancellingTheReaderDoesNotStopTheSession()
    {
        await using var session = CreateSession(capacity: 2);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in session.ReadChunksAsync(cancelled.Token))
            {
            }
        });

        Assert.Equal(MicrophoneSessionState.Running, session.State);
        await session.WriteAsync(Bytes(7), CancellationToken.None);
        var chunks = await ReadAllAfterStopAsync(session);
        Assert.Equal(7, chunks[0].Payload[0]);
    }

    [Fact]
    public async Task InvalidChunkFaultsTheSession()
    {
        await using var session = CreateSession(capacity: 2);

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => session.WriteAsync(new byte[3], CancellationToken.None).AsTask());

        Assert.Equal(MicrophoneErrorCode.InvalidChunk, error.Code);
        var thrown = await Assert.ThrowsAsync<MicrophoneException>(async () =>
        {
            await foreach (var _ in session.ReadChunksAsync())
            {
            }
        });
        Assert.Equal(MicrophoneErrorCode.InvalidChunk, thrown.Code);
        Assert.Equal(MicrophoneSessionState.Faulted, session.State);
    }

    [Fact]
    public async Task DisposeIsIdempotentAndEndsTheRead()
    {
        await using var session = CreateSession(capacity: 2);
        await session.WriteAsync(Bytes(4), CancellationToken.None);
        await session.DisposeAsync();
        await session.DisposeAsync();

        var chunks = await ReadAllAsync(session);

        Assert.Single(chunks);
        Assert.Equal(MicrophoneSessionState.Disposed, session.State);
    }

    [Fact]
    public async Task QueuedChunkStaysStableWhenTheSourceBufferIsReused()
    {
        await using var session = CreateSession(capacity: 2);
        var source = Bytes(9);
        await session.WriteAsync(source, CancellationToken.None);
        source[0] = 1;
        source[1] = 2;
        session.Complete();

        var chunks = await ReadAllAsync(session);

        Assert.Equal(9, chunks[0].Payload[0]);
        Assert.Equal(0, chunks[0].Payload[1]);
        Assert.NotSame(source, chunks[0].Payload);
    }

    [Fact]
    public async Task ASecondReaderIsRejected()
    {
        await using var session = CreateSession(capacity: 1);
        var first = session.ReadChunksAsync().GetAsyncEnumerator();
        var next = first.MoveNextAsync();
        var second = session.ReadChunksAsync().GetAsyncEnumerator();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await second.MoveNextAsync());

        session.Complete();
        await next;
        await first.DisposeAsync();
        await second.DisposeAsync();
    }

    private static MicrophoneSession CreateSession(int capacity, CaptureBackpressure backpressure = CaptureBackpressure.DropOldest)
        => new(PcmFormat.Default, backpressure, capacity, TimeProvider.System);

    private static byte[] Bytes(byte value)
    {
        var bytes = new byte[PcmFormat.Default.BytesPerChunk];
        bytes[0] = value;
        return bytes;
    }

    private static async Task<List<PcmChunk>> ReadAllAsync(MicrophoneSession session)
    {
        var chunks = new List<PcmChunk>();
        await foreach (var chunk in session.ReadChunksAsync())
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static async Task<List<PcmChunk>> ReadAllAfterStopAsync(MicrophoneSession session)
    {
        session.Complete();
        return await ReadAllAsync(session);
    }
}
