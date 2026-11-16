namespace MicroSharp.Native.Tests;

public sealed class OpenAlVirtualDeviceTests
{
    [Fact]
    [Trait("Category", "VirtualDevice")]
    public async Task PreparedVirtualDeviceProducesPcmAndCanBeOpenedAgain()
    {
        var capture = await OpenPreparedDeviceAsync();
        await using var session = await capture.StartAsync(new MicrophoneCaptureRequest());
        var chunks = await ReadChunksAsync(session, 4);
        Assert.Equal(4, chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Sequence);
            Assert.Equal(PcmFormat.Default.BytesPerChunk, chunks[i].Payload.Length);
        }

        await session.StopAsync();
        await session.DisposeAsync();

        await using var again = await capture.StartAsync(new MicrophoneCaptureRequest());
        var repeated = await ReadChunksAsync(again, 1);
        Assert.Equal(PcmFormat.Default.BytesPerChunk, repeated[0].Payload.Length);
        await again.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "VirtualDevice")]
    public async Task CancellingTheReaderLeavesThePreparedDeviceReleasable()
    {
        var capture = await OpenPreparedDeviceAsync();
        await using var session = await capture.StartAsync(new MicrophoneCaptureRequest());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var sawCancel = false;
        try
        {
            await foreach (var chunk in session.ReadChunksAsync(cancelled.Token))
            {
                Assert.Equal(PcmFormat.Default.BytesPerChunk, chunk.Payload.Length);
            }
        }
        catch (OperationCanceledException)
        {
            sawCancel = true;
        }

        Assert.True(sawCancel);
        Assert.Equal(MicrophoneSessionState.Running, session.State);
        await session.DisposeAsync();

        await using var again = await capture.StartAsync(new MicrophoneCaptureRequest());
        Assert.Equal(MicrophoneSessionState.Running, again.State);
        await again.DisposeAsync();
    }

    private static async Task<OpenAlMicrophoneCapture> OpenPreparedDeviceAsync()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MICROSHARP_REQUIRE_VIRTUAL_CAPTURE"), "1", StringComparison.Ordinal))
        {
            Assert.Fail("This check runs only when MICROSHARP_REQUIRE_VIRTUAL_CAPTURE=1 and a virtual source was prepared.");
        }

        var token = Environment.GetEnvironmentVariable("MICROSHARP_OPENAL_VIRTUAL_DEVICE");
        if (string.IsNullOrWhiteSpace(token))
        {
            Assert.Fail("MICROSHARP_OPENAL_VIRTUAL_DEVICE is required and must be the prepared source name.");
        }

        var probe = new OpenAlMicrophoneCapture(new OpenAlMicrophoneOptions(token));
        IReadOnlyList<string> names;
        try
        {
            names = probe.ListCaptureDevices();
        }
        catch (MicrophoneException exception) when (exception.Code == MicrophoneErrorCode.Unavailable)
        {
            throw new InvalidOperationException(
                "OpenAL did not load. Unavailable is not a passing result for virtual capture.",
                exception);
        }

        var matches = names.Where(name => name.Contains(token, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            Assert.Fail(
                "Expected one OpenAL capture name containing '" + token + "'. Listed: "
                + (names.Count == 0 ? "(none)" : string.Join(" | ", names)));
        }

        var chosen = matches[0];
        Assert.False(string.IsNullOrWhiteSpace(chosen));
        var capture = new OpenAlMicrophoneCapture(new OpenAlMicrophoneOptions(chosen));
        var permission = await capture.GetPermissionAsync();
        Assert.Equal(MicrophonePermissionState.Granted, permission.State);
        return capture;
    }

    private static async Task<List<PcmChunk>> ReadChunksAsync(IMicrophoneSession session, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var chunks = new List<PcmChunk>(count);
        try
        {
            await foreach (var chunk in session.ReadChunksAsync(timeout.Token))
            {
                chunks.Add(chunk);
                if (chunks.Count == count)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException exception)
        {
            throw new InvalidOperationException(
                $"The prepared device produced {chunks.Count} of {count} chunks before the read timed out.",
                exception);
        }

        return chunks;
    }
}
