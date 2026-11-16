namespace MicroSharp.Tests;

public sealed class PcmCaptureQueueOwnershipTests
{
    [Fact]
    public async Task QueuedChunkKeepsACopyAfterTheCallerReusesTheBuffer()
    {
        var format = PcmFormat.Default;
        await using var session = new MicrophoneSession(format, CaptureBackpressure.DropOldest, 4, TimeProvider.System);
        var source = new byte[format.BytesPerChunk];
        source[0] = 7;
        source[^1] = 8;
        await session.WriteAsync(source, CancellationToken.None);
        source[0] = 9;
        session.Complete();

        var chunks = await ReadAllAsync(session);

        Assert.Single(chunks);
        Assert.False(ReferenceEquals(source, chunks[0].Payload));
        Assert.Equal(7, chunks[0].Payload[0]);
        Assert.Equal(8, chunks[0].Payload[^1]);
        Assert.Equal(format.BytesPerChunk, chunks[0].Payload.Length);
    }

    [Theory]
    [InlineData(8_000, 10)]
    [InlineData(48_000, 20)]
    public async Task SupportedFormatsStayInsideTheChunkCapacity(int sampleRateHz, int chunkMs)
    {
        var format = new PcmFormat(sampleRateHz, TimeSpan.FromMilliseconds(chunkMs));
        const int capacity = 3;
        await using var session = new MicrophoneSession(format, CaptureBackpressure.DropOldest, capacity, TimeProvider.System);
        var source = new byte[format.BytesPerChunk];
        for (var i = 0; i < capacity + 5; i++)
        {
            source[0] = (byte)i;
            await session.WriteAsync(source, CancellationToken.None);
        }

        session.Complete();
        var chunks = await ReadAllAsync(session);

        Assert.Equal(5, session.DroppedChunkCount);
        Assert.Equal(capacity, chunks.Count);
        Assert.Equal(format.BytesPerChunk, chunks[0].Payload.Length);
        Assert.Equal((byte)5, chunks[0].Payload[0]);
        Assert.Equal((byte)7, chunks[^1].Payload[0]);
    }

    [Fact]
    public async Task ConcurrentReadsAccountForEveryWrittenChunk()
    {
        const int capacity = 4;
        const int writes = 100;
        var format = PcmFormat.Default;
        await using var session = new MicrophoneSession(format, CaptureBackpressure.DropOldest, capacity, TimeProvider.System);
        var read = 0;
        var reader = Task.Run(async () =>
        {
            await foreach (var chunk in session.ReadChunksAsync())
            {
                if (chunk.Payload.Length != format.BytesPerChunk)
                {
                    throw new InvalidOperationException("The reader observed a short chunk.");
                }

                Interlocked.Increment(ref read);
            }
        });

        var source = new byte[format.BytesPerChunk];
        for (var i = 0; i < writes; i++)
        {
            source[0] = (byte)i;
            await session.WriteAsync(source, CancellationToken.None);
        }

        session.Complete();
        var finished = await Task.WhenAny(reader, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(reader, finished);
        await reader;
        Assert.Equal(writes, read + session.DroppedChunkCount);
        Assert.InRange(read, 1, writes);
    }

    [Fact]
    public async Task SteadyCopyPathAllocatesLessThanASecondPayload()
    {
        var format = PcmFormat.Default;
        var session = new MicrophoneSession(format, CaptureBackpressure.DropOldest, 4, TimeProvider.System);
        try
        {
            var source = new byte[format.BytesPerChunk];
            const int warmup = 64;
            const int measured = 2_000;
            for (var i = 0; i < warmup; i++)
            {
                WriteAndRead(session, source);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < measured; i++)
            {
                WriteAndRead(session, source);
            }

            var perChunk = (GC.GetAllocatedBytesForCurrentThread() - before) / measured;
            var bound = format.BytesPerChunk + 1024;
            Assert.True(
                perChunk >= format.BytesPerChunk && perChunk < bound,
                $"queue path allocated ~{perChunk} B/chunk; expected one {format.BytesPerChunk} B copy and less than {bound} B total.");
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static void WriteAndRead(MicrophoneSession session, byte[] source)
    {
        session.WriteAsync(source, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        var reader = session.ReadChunksAsync().GetAsyncEnumerator();
        try
        {
            if (!reader.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                throw new InvalidOperationException("The queue did not return the written chunk.");
            }

            if (reader.Current.Payload.Length != source.Length)
            {
                throw new InvalidOperationException("The returned chunk has the wrong length.");
            }
        }
        finally
        {
            reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
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
}
