using Microsoft.Extensions.DependencyInjection;

namespace MicroSharp.Browser.Tests;

public sealed class BrowserMicrophoneCaptureTests
{
    [Fact]
    public async Task PermissionDeniedIsClassifiedAndDoesNotStart()
    {
        var module = new ScriptedJsModule { AcquireError = "NotAllowedError" };
        var capture = new BrowserMicrophoneCapture(module);

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None));

        Assert.Equal(MicrophoneErrorCode.PermissionDenied, error.Code);
        Assert.DoesNotContain("start", module.Calls);
        Assert.False(capture.CapturesOperatingSystemDevice);
        Assert.True(capture.RequiresBrowserHost);
    }

    [Fact]
    public async Task MissingDeviceIsClassified()
    {
        var module = new ScriptedJsModule { AcquireError = "NotFoundError" };
        var capture = new BrowserMicrophoneCapture(module);

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.RequestAccessAsync());

        Assert.Equal(MicrophoneErrorCode.DeviceNotFound, error.Code);
    }

    [Fact]
    public async Task MissingBrowserApisDoNotPretendToCapture()
    {
        var module = new ScriptedJsModule { HostAvailable = false };
        var capture = new BrowserMicrophoneCapture(module);

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None));

        Assert.Equal(MicrophoneErrorCode.Unavailable, error.Code);
        Assert.DoesNotContain("acquire", module.Calls);
    }

    [Theory]
    [InlineData(22_050)]
    public async Task UnsupportedFormatFailsBeforeJavaScript(int sampleRate)
    {
        var module = new ScriptedJsModule();
        var capture = new BrowserMicrophoneCapture(module);
        var request = new MicrophoneCaptureRequest
        {
            Format = new PcmFormat(sampleRate, TimeSpan.FromMilliseconds(20)),
        };

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.StartAsync(request, CancellationToken.None));

        Assert.Equal(MicrophoneErrorCode.UnsupportedFormat, error.Code);
        Assert.Empty(module.Calls);
    }

    [Fact]
    public async Task WaitBackpressureIsRejectedForTheBrowser()
    {
        var module = new ScriptedJsModule();
        var capture = new BrowserMicrophoneCapture(module);
        var request = new MicrophoneCaptureRequest { Backpressure = CaptureBackpressure.Wait };

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.StartAsync(request, CancellationToken.None));

        Assert.Equal(MicrophoneErrorCode.UnsupportedBackpressure, error.Code);
        Assert.Empty(module.Calls);
    }

    [Fact]
    public async Task ChunksFlowThroughTheCallbackAndDisposeReleasesTheBrowser()
    {
        var module = new ScriptedJsModule();
        var capture = new BrowserMicrophoneCapture(module);
        await using var session = await capture.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None);
        var payload = new byte[PcmFormat.Default.BytesPerChunk];
        payload[0] = 9;
        module.SessionReference!.Value.OnPcmChunk(payload);

        var chunk = await FirstChunkAsync(session);
        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(0, chunk.Sequence);
        Assert.Equal(9, chunk.Payload[0]);
        Assert.Contains("stop", module.Calls);
        Assert.Equal(MicrophoneSessionState.Disposed, session.State);
    }

    [Fact]
    public async Task CancellingStartReleasesTheBrowserSession()
    {
        var module = new ScriptedJsModule { DelayStartUntilCancelled = true };
        var capture = new BrowserMicrophoneCapture(module);
        using var cancellation = new CancellationTokenSource();
        var starting = capture.StartAsync(new MicrophoneCaptureRequest(), cancellation.Token);
        await module.StartEntered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await starting);

        Assert.Contains(module.Calls, call => call is "stop" or "release");
    }

    [Fact]
    public async Task ADifferentConfirmedFormatReleasesTheBrowserSession()
    {
        var module = new ScriptedJsModule { StartSampleRateHz = 8_000 };
        var capture = new BrowserMicrophoneCapture(module);

        var error = await Assert.ThrowsAsync<MicrophoneException>(() => capture.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None));

        Assert.Equal(MicrophoneErrorCode.Unavailable, error.Code);
        Assert.Contains("stop", module.Calls);
    }

    [Fact]
    public async Task PermissionQueryMapsTheBrowserState()
    {
        var module = new ScriptedJsModule { PermissionState = "prompt" };
        var capture = new BrowserMicrophoneCapture(module);

        var permission = await capture.GetPermissionAsync();

        Assert.Equal(MicrophonePermissionState.Prompt, permission.State);
    }

    [Fact]
    public void DependencyInjectionUsesTheRegisteredJavaScriptRuntime()
    {
        var module = new ScriptedJsModule();
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(module);
        services.AddMicroSharpBrowser();
        using var provider = services.BuildServiceProvider();

        var capture = provider.GetRequiredService<IMicrophoneCapture>();

        Assert.IsType<BrowserMicrophoneCapture>(capture);
        Assert.True(capture.RequiresBrowserHost);
    }

    private static async Task<PcmChunk> FirstChunkAsync(IMicrophoneSession session)
    {
        await foreach (var chunk in session.ReadChunksAsync())
        {
            return chunk;
        }

        throw new InvalidOperationException("The session ended before a chunk.");
    }
}
