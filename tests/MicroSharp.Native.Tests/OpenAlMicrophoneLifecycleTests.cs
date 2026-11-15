using System.Buffers.Binary;
using Microsoft.Extensions.DependencyInjection;

namespace MicroSharp.Native.Tests;

public sealed class OpenAlMicrophoneLifecycleTests
{
    [Fact]
    public void EmptyDeviceNameIsRejectedBeforeOpenAl()
    {
        Assert.Throws<ArgumentException>(() => new OpenAlMicrophoneOptions(" "));
    }

    [Fact]
    public async Task UnsupportedFormatDoesNotOpenADevice()
    {
        var factory = Factory();
        var capture = Capture(factory);

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.StartAsync(new MicrophoneCaptureRequest
        {
            Format = new PcmFormat(22_050, TimeSpan.FromMilliseconds(20)),
        }));

        Assert.Equal(MicrophoneErrorCode.UnsupportedFormat, error.Code);
        Assert.Equal(0, factory.OpenCount);
    }

    [Fact]
    public async Task WaitBackpressureDoesNotOpenADevice()
    {
        var factory = Factory();
        var capture = Capture(factory);

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.StartAsync(new MicrophoneCaptureRequest
        {
            Backpressure = CaptureBackpressure.Wait,
        }));

        Assert.Equal(MicrophoneErrorCode.UnsupportedBackpressure, error.Code);
        Assert.Equal(0, factory.OpenCount);
    }

    [Fact]
    public async Task CancellingBeforeOpenDoesNotOpenADevice()
    {
        var factory = Factory();
        var capture = Capture(factory);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture.StartAsync(
            new MicrophoneCaptureRequest(),
            cancelled.Token));

        Assert.Equal(0, factory.OpenCount);
        Assert.Equal(0, factory.Handle.DisposeCount);
    }

    [Fact]
    public async Task CancellingAfterStartClosesTheDevice()
    {
        var factory = Factory();
        var capture = Capture(factory);
        using var cts = new CancellationTokenSource();
        factory.Handle.OnStart = () => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture.StartAsync(
            new MicrophoneCaptureRequest(),
            cts.Token));

        Assert.Equal(1, factory.Handle.StartCount);
        Assert.Equal(1, factory.Handle.StopCount);
        Assert.Equal(1, factory.Handle.DisposeCount);

        await using var again = await capture.StartAsync(new MicrophoneCaptureRequest());
        Assert.Equal(2, factory.OpenCount);
        Assert.Equal(MicrophoneSessionState.Running, again.State);
    }

    [Fact]
    public async Task CapturedChunksMatchTheRequestedPcmFormat()
    {
        var factory = Factory(sample: 99, chunks: 2);
        var capture = Capture(factory, "named-device");

        await using var session = await capture.StartAsync(new MicrophoneCaptureRequest());
        var chunks = new List<PcmChunk>();
        await foreach (var chunk in session.ReadChunksAsync())
        {
            chunks.Add(chunk);
            if (chunks.Count == 2)
            {
                break;
            }
        }

        await session.StopAsync();

        Assert.Equal("named-device", factory.OpenedName);
        Assert.Equal(16_000, factory.OpenedRate);
        Assert.Equal(16_000, factory.OpenedBuffer);
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, chunk => Assert.Equal(PcmFormat.Default.BytesPerChunk, chunk.Payload.Length));
        Assert.Equal(99, BinaryPrimitives.ReadInt16LittleEndian(chunks[0].Payload));
        Assert.Equal(1, factory.Handle.StopCount);
        Assert.Equal(1, factory.Handle.DisposeCount);
    }

    [Fact]
    public async Task DisposeIsIdempotent()
    {
        var factory = Factory(chunks: 1);
        var capture = Capture(factory);
        var session = await capture.StartAsync(new MicrophoneCaptureRequest());

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, factory.Handle.DisposeCount);
        Assert.Equal(MicrophoneSessionState.Disposed, session.State);
    }

    [Fact]
    public async Task ASecondStartWhileRunningIsRejected()
    {
        var factory = Factory(chunks: 4);
        var capture = Capture(factory);
        await using var session = await capture.StartAsync(new MicrophoneCaptureRequest());

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.StartAsync(new MicrophoneCaptureRequest()));

        Assert.Equal(MicrophoneErrorCode.Unavailable, error.Code);
        Assert.Equal(1, factory.OpenCount);
    }

    [Fact]
    public async Task PermissionFollowsTheNamedDevice()
    {
        var factory = Factory();
        var capture = Capture(factory, "named-device");

        var missing = await capture.GetPermissionAsync();
        factory.Devices.Add("named-device");
        var present = await capture.GetPermissionAsync();

        Assert.Equal(MicrophonePermissionState.Denied, missing.State);
        Assert.Equal(MicrophonePermissionState.Granted, present.State);
    }

    [Fact]
    public async Task RequestAccessDoesNotOpenWhenTheNameIsAbsent()
    {
        var factory = Factory();
        var capture = Capture(factory);

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.RequestAccessAsync());

        Assert.Equal(MicrophoneErrorCode.DeviceNotFound, error.Code);
        Assert.Equal(0, factory.OpenCount);
    }

    [Fact]
    public void DependencyInjectionRegistersTheOperatingSystemAdapter()
    {
        var services = new ServiceCollection();
        services.AddMicroSharpOpenAlMicrophone(new OpenAlMicrophoneOptions("named-device"));
        using var provider = services.BuildServiceProvider();

        var capture = provider.GetRequiredService<IMicrophoneCapture>();

        Assert.IsType<OpenAlMicrophoneCapture>(capture);
        Assert.True(capture.CapturesOperatingSystemDevice);
        Assert.False(capture.RequiresBrowserHost);
        Assert.Equal(OpenAlMicrophoneCapture.AdapterId, capture.AdapterName);
    }

    private static RecordingOpenAlFactory Factory(short sample = 1, int chunks = 1)
        => new(new RecordingOpenAlHandle(sample, chunks));

    private static OpenAlMicrophoneCapture Capture(RecordingOpenAlFactory factory, string deviceName = "named-device")
        => new(new OpenAlMicrophoneOptions(deviceName), factory);
}
