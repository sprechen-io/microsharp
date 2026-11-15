using Microsoft.Extensions.DependencyInjection;

namespace MicroSharp.Native.Tests;

public sealed class VirtualMicrophoneCaptureTests
{
    [Fact]
    public async Task PermissionDeniedDoesNotReadTheSource()
    {
        var source = new CountingPcmSource(new PcmToneSource(440, PcmFormat.Default)) { FailOnRead = true };
        var capture = Capture(source, permission: MicrophonePermissionState.Denied);

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.StartAsync(Request(), CancellationToken.None));

        Assert.Equal(MicrophoneErrorCode.PermissionDenied, error.Code);
        Assert.Equal(0, source.Reads);
        Assert.False(capture.CapturesOperatingSystemDevice);
        Assert.False(capture.RequiresBrowserHost);
    }

    [Fact]
    public async Task MissingDeviceDoesNotReadTheSource()
    {
        var source = new CountingPcmSource(new PcmToneSource(440, PcmFormat.Default)) { FailOnRead = true };
        var capture = new VirtualMicrophoneCapture(new VirtualMicrophoneOptions
        {
            Source = source,
            DeviceMissing = true,
        });

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.RequestAccessAsync());

        Assert.Equal(MicrophoneErrorCode.DeviceNotFound, error.Code);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task CancelledStartDoesNotReadTheSource()
    {
        var source = new CountingPcmSource(new PcmToneSource(440, PcmFormat.Default)) { FailOnRead = true };
        var capture = Capture(source);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture.StartAsync(Request(), cancelled.Token));

        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task ToneChunksMatchTheGeneratedWave()
    {
        var format = PcmFormat.Default;
        var source = new PcmToneSource(800, format, amplitude: 1);
        var capture = Capture(source);
        await using var session = await capture.StartAsync(Request(format, CaptureBackpressure.Wait, 4), CancellationToken.None);
        var chunk = await FirstChunkAsync(session);
        await session.StopAsync();

        var samples = PcmEncoding.ToSamples(chunk.Payload);
        var maxDifference = 0;
        for (var index = 0; index < samples.Length; index++)
        {
            maxDifference = Math.Max(maxDifference, Math.Abs(samples[index] - ExpectedSample(index)));
        }

        Assert.Equal(320, samples.Length);
        Assert.InRange(maxDifference, 0, 1);
        var peak = samples.Max(sample => sample == short.MinValue ? 32768 : Math.Abs((int)sample));
        Assert.True(peak > 20_000);
    }

    [Fact]
    public async Task FiniteStreamEndsAfterItsFullChunks()
    {
        var format = PcmFormat.Default;
        var first = Enumerable.Repeat((short)1000, format.SamplesPerChunk).ToArray();
        var second = Enumerable.Repeat((short)-1000, format.SamplesPerChunk).ToArray();
        var bytes = PcmEncoding.FromSamples(first).Concat(PcmEncoding.FromSamples(second)).Concat(new byte[] { 1, 2 }).ToArray();
        var source = new StreamPcmSource(new MemoryStream(bytes), format);
        var capture = Capture(source);

        await using var session = await capture.StartAsync(Request(format, CaptureBackpressure.Wait, 4), CancellationToken.None);
        var chunks = await ReadAvailableAsync(session);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(first, PcmEncoding.ToSamples(chunks[0].Payload));
        Assert.Equal(second, PcmEncoding.ToSamples(chunks[1].Payload));
        Assert.Equal(MicrophoneSessionState.Stopped, session.State);
    }

    [Fact]
    public async Task DisposeStopsFurtherReads()
    {
        var format = PcmFormat.Default;
        var source = new CountingPcmSource(new PcmToneSource(440, format));
        var capture = Capture(source);
        var session = await capture.StartAsync(Request(format, CaptureBackpressure.Wait, 2), CancellationToken.None);
        _ = await FirstChunkAsync(session);
        await session.DisposeAsync();
        await session.DisposeAsync();
        var reads = source.Reads;

        await Task.Delay(50);

        Assert.Equal(reads, source.Reads);
        Assert.Equal(MicrophoneSessionState.Disposed, session.State);
        var again = await capture.StartAsync(Request(format, CaptureBackpressure.Wait, 2), CancellationToken.None);
        await again.DisposeAsync();
    }

    [Fact]
    public async Task ARunningSessionRejectsASecondStart()
    {
        var format = PcmFormat.Default;
        var capture = Capture(new PcmToneSource(440, format));
        var session = await capture.StartAsync(Request(format, CaptureBackpressure.Wait, 1), CancellationToken.None);

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.StartAsync(Request(format), CancellationToken.None));

        Assert.Equal(MicrophoneErrorCode.Unavailable, error.Code);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task RealtimePacingWaitsBetweenChunks()
    {
        var format = PcmFormat.Default;
        var capture = new VirtualMicrophoneCapture(new VirtualMicrophoneOptions
        {
            Source = new PcmToneSource(440, format),
            PaceInRealTime = true,
        });
        await using var session = await capture.StartAsync(Request(format, CaptureBackpressure.Wait, 4), CancellationToken.None);
        var started = TimeProvider.System.GetTimestamp();
        var count = 0;
        await foreach (var _ in session.ReadChunksAsync())
        {
            count++;
            if (count == 2)
            {
                break;
            }
        }

        var elapsed = TimeProvider.System.GetElapsedTime(started);
        await session.StopAsync();

        Assert.True(elapsed >= TimeSpan.FromMilliseconds(15), $"Elapsed {elapsed.TotalMilliseconds} ms.");
    }

    [Fact]
    public void DependencyInjectionRegistersTheCaptureWithoutHidingTheConstructor()
    {
        var format = PcmFormat.Default;
        var services = new ServiceCollection();
        services.AddMicroSharpVirtualMicrophone(new VirtualMicrophoneOptions
        {
            Source = new PcmToneSource(440, format),
        });
        using var provider = services.BuildServiceProvider();

        var capture = provider.GetRequiredService<IMicrophoneCapture>();

        Assert.Equal(VirtualMicrophoneCapture.AdapterId, capture.AdapterName);
        Assert.False(capture.CapturesOperatingSystemDevice);
    }

    private static VirtualMicrophoneCapture Capture(
        IPcmSource source,
        MicrophonePermissionState permission = MicrophonePermissionState.Granted)
        => new(new VirtualMicrophoneOptions
        {
            Source = source,
            Permission = permission,
        });

    private static MicrophoneCaptureRequest Request(
        PcmFormat? format = null,
        CaptureBackpressure backpressure = CaptureBackpressure.DropOldest,
        int capacity = 8)
        => new()
        {
            Format = format ?? PcmFormat.Default,
            Backpressure = backpressure,
            QueueCapacityChunks = capacity,
        };

    private static async Task<PcmChunk> FirstChunkAsync(IMicrophoneSession session)
    {
        await foreach (var chunk in session.ReadChunksAsync())
        {
            return chunk;
        }

        throw new InvalidOperationException("The session ended before a chunk.");
    }

    private static async Task<List<PcmChunk>> ReadAvailableAsync(IMicrophoneSession session)
    {
        var chunks = new List<PcmChunk>();
        await foreach (var chunk in session.ReadChunksAsync())
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static short ExpectedSample(int index)
    {
        var value = Math.Sin(2d * Math.PI * 800d * index / 16_000d);
        var scaled = value < 0 ? value * 32768d : value * 32767d;
        return (short)scaled;
    }
}
