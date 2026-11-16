using MicroSharp;
using Microsoft.JSInterop;

namespace MicroSharp.Browser.Tests;

public sealed class BrowserCaptureControllerTests
{
    [Fact]
    public async Task StartDeliversPayloadsFromTheBrowserSession()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module);

        await using var lease = await controller.StartAsync(CancellationToken.None);
        var payload = Sample();
        module.SessionReference!.Value.OnPcmChunk(payload);

        byte[]? observed = null;
        await foreach (var chunk in lease.ReadPayloadsAsync())
        {
            observed = chunk;
            break;
        }

        Assert.Equal(payload, observed);
        Assert.Equal("audio/pcm;rate=16000", lease.MediaType);
        Assert.Equal(true, module.LastAcquirePreferences?["keepAudioContextAlive"]);
        Assert.Equal(16_000, module.RequestedSampleRateHz);
    }

    [Fact]
    public async Task CallerSelectsTheSampleRate()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module, new BrowserCaptureControllerOptions
        {
            Format = new PcmFormat(48_000, TimeSpan.FromMilliseconds(20)),
            KeepAudioContextAlive = false,
            EchoCancellation = false,
        });

        await using var lease = await controller.StartAsync(CancellationToken.None);

        Assert.Equal("audio/pcm;rate=48000", lease.MediaType);
        Assert.Equal(48_000, module.RequestedSampleRateHz);
        Assert.Equal(false, module.LastAcquirePreferences?["keepAudioContextAlive"]);
        Assert.Equal(false, module.LastAcquirePreferences?["echoCancellation"]);
    }

    [Fact]
    public async Task SecondStartWithoutStopIsRejected()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module);
        await using var lease = await controller.StartAsync(CancellationToken.None);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StartAsync(CancellationToken.None));

        Assert.Contains("already running", failure.Message, StringComparison.Ordinal);
        Assert.Equal(MicrophoneSessionState.Running, module.SessionReference!.Value.State);
        _ = lease;
    }

    [Fact]
    public async Task StopKeepsTheSourceAndTheNextStartReusesIt()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module);
        var first = await controller.StartAsync(CancellationToken.None);
        await controller.StopAsync(CancellationToken.None);
        await first.DisposeAsync();

        await using var second = await controller.StartAsync(CancellationToken.None);

        Assert.Equal(1, module.AcquireCount);
        Assert.Contains("disconnect", module.Calls);
        Assert.DoesNotContain("release", module.Calls);
        Assert.Equal(MicrophoneSessionState.Running, module.SessionReference!.Value.State);
    }

    [Fact]
    public async Task ReleaseThenAccessReportsTheReclaimedIntervalAndOpensANewSource()
    {
        double? reclaimed = null;
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module, new BrowserCaptureControllerOptions
        {
            SourceReclaimed = value => reclaimed = value,
        });
        await controller.RequestAccessAsync(CancellationToken.None);
        await controller.ReleaseAsync(CancellationToken.None);
        await controller.RequestAccessAsync(CancellationToken.None);

        Assert.Equal(2, module.AcquireCount);
        Assert.Contains("release", module.Calls);
        Assert.NotNull(reclaimed);
        Assert.True(reclaimed >= 0);
    }

    [Fact]
    public async Task MalformedAcquisitionIsAnUnavailableFailureWithTheOriginalCause()
    {
        var module = new ScriptedJsModule { MalformedAcquire = true };
        var controller = new BrowserCaptureController(module);

        var failure = await Assert.ThrowsAsync<BrowserSourceOwnershipException>(() => controller.RequestAccessAsync(CancellationToken.None));

        Assert.Equal(BrowserSourceFailureKind.Unavailable, failure.Kind);
        var inner = Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal("The microphone acquisition result is malformed.", inner.Message);
    }

    [Theory]
    [InlineData("NotFoundError", BrowserSourceFailureKind.DeviceNotFound, "The microphone device is unavailable.")]
    [InlineData("DevicesNotFoundError", BrowserSourceFailureKind.DeviceNotFound, "The microphone device is unavailable.")]
    [InlineData("OverconstrainedError", BrowserSourceFailureKind.DeviceNotFound, "The microphone device is unavailable.")]
    [InlineData("NotAllowedError", BrowserSourceFailureKind.PermissionDenied, "Microphone access is not permitted.")]
    [InlineData("PermissionDeniedError", BrowserSourceFailureKind.PermissionDenied, "Microphone access is not permitted.")]
    [InlineData("SecurityError", BrowserSourceFailureKind.PermissionDenied, "Microphone access is not permitted.")]
    [InlineData("AbortError", BrowserSourceFailureKind.Unavailable, "Microphone acquisition failed.")]
    public async Task NamedBrowserErrorsKeepTheirKind(string errorName, BrowserSourceFailureKind kind, string message)
    {
        var module = new ScriptedJsModule { AcquireError = errorName };
        var controller = new BrowserCaptureController(module);

        var failure = await Assert.ThrowsAsync<BrowserSourceOwnershipException>(() => controller.RequestAccessAsync(CancellationToken.None));

        Assert.Equal(kind, failure.Kind);
        Assert.Equal(message, failure.Message);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task ReportedRateMismatchStaysAnInvalidOperationInsideUnavailable()
    {
        var module = new ScriptedJsModule { StartSampleRateHz = 48_000 };
        var controller = new BrowserCaptureController(module);

        var failure = await Assert.ThrowsAsync<BrowserSourceOwnershipException>(() => controller.StartAsync(CancellationToken.None));

        Assert.Equal(BrowserSourceFailureKind.Unavailable, failure.Kind);
        var inner = Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Contains("rate=48000", inner.Message, StringComparison.Ordinal);
        Assert.Contains("rate=16000", inner.Message, StringComparison.Ordinal);
        Assert.Contains("release", module.Calls);
    }

    [Fact]
    public async Task CancelledSourceWritePublishesTheConfiguredValue()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module, new BrowserCaptureControllerOptions
        {
            SourceEnabledAfterCancellation = true,
        });
        await controller.RequestAccessAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var failure = await Record.ExceptionAsync(() => controller.SetSourceEnabledAsync(false, cancellation.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        var restored = await WaitUntilAsync(() => module.SourceEnabled.Count > 0 && module.SourceEnabled[^1]);

        Assert.True(restored);
        Assert.All(module.SourceEnabled, enabled => Assert.True(enabled));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingCancellationPublishesTheConfiguredSourceState(bool restore)
    {
        var module = new ScriptedJsModule { PauseSetSource = true };
        var controller = new BrowserCaptureController(module, new BrowserCaptureControllerOptions
        {
            SourceEnabledAfterCancellation = restore,
        });
        await controller.RequestAccessAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var pending = controller.SetSourceEnabledAsync(false, cancellation.Token);
        var entered = await WaitUntilAsync(() => module.Calls.Count(call => call == "setSourceEnabled") == 1);
        Assert.True(entered);

        await cancellation.CancelAsync();
        module.SetSourceGate.TrySetResult(true);
        var failure = await Record.ExceptionAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        var restored = await WaitUntilAsync(() => module.SourceEnabled.Count > 0 && module.SourceEnabled[^1] == restore);

        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.True(restored);
        Assert.DoesNotContain("release", module.Calls);
    }

    [Fact]
    public async Task StaleSourceFailureDoesNotReleaseTheSource()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module);
        await controller.RequestAccessAsync(CancellationToken.None);
        module.PauseSetSource = true;
        module.FailNextSetSource = true;
        var first = controller.SetSourceEnabledAsync(false, CancellationToken.None);
        var entered = await WaitUntilAsync(() => module.Calls.Contains("setSourceEnabled"));
        Assert.True(entered);

        module.PauseSetSource = false;
        var second = controller.SetSourceEnabledAsync(true, CancellationToken.None);
        module.SetSourceGate.TrySetResult(true);
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        await second.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain("release", module.Calls);
        Assert.NotEmpty(module.SourceEnabled);
        Assert.All(module.SourceEnabled, enabled => Assert.True(enabled));
    }

    [Fact]
    public async Task CurrentSourceFailureReleasesTheSourceAndSurfacesTheBrowserError()
    {
        var module = new ScriptedJsModule { FailNextSetSource = true };
        var controller = new BrowserCaptureController(module);
        await controller.RequestAccessAsync(CancellationToken.None);

        var failure = await Assert.ThrowsAsync<JSException>(() => controller.SetSourceEnabledAsync(false, CancellationToken.None));

        Assert.Contains("rejected", failure.Message, StringComparison.Ordinal);
        Assert.Contains("release", module.Calls);
    }

    [Fact]
    public async Task EarlyBreakLeavesTheSessionRunningAndTheNextChunkReadable()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module);
        await using var lease = await controller.StartAsync(CancellationToken.None);
        var session = module.SessionReference!.Value;
        var first = Sample();
        var second = Sample();
        second[0] = 0x11;
        session.OnPcmChunk(first);
        session.OnPcmChunk(second);

        byte[]? observed = null;
        await foreach (var chunk in lease.ReadPayloadsAsync())
        {
            observed = chunk;
            break;
        }

        Assert.Equal(first, observed);
        Assert.Equal(MicrophoneSessionState.Running, session.State);
        Assert.DoesNotContain("disconnect", module.Calls);
        await using var again = lease.ReadPayloadsAsync().GetAsyncEnumerator();
        Assert.True(await again.MoveNextAsync());
        Assert.Equal(second, again.Current);
    }

    [Fact]
    public async Task CancellingAPendingReadLeavesTheSessionRunning()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module);
        await using var lease = await controller.StartAsync(CancellationToken.None);
        var session = module.SessionReference!.Value;
        using var cancellation = new CancellationTokenSource();
        var enumerator = lease.ReadPayloadsAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        var pending = enumerator.MoveNextAsync();
        await Task.Delay(30);
        await cancellation.CancelAsync();

        var canceled = await Record.ExceptionAsync(() => pending.AsTask());
        await enumerator.DisposeAsync();

        Assert.IsAssignableFrom<OperationCanceledException>(canceled);
        Assert.Equal(MicrophoneSessionState.Running, session.State);
        Assert.DoesNotContain("disconnect", module.Calls);
        var payload = Sample();
        session.OnPcmChunk(payload);
        await using var again = lease.ReadPayloadsAsync().GetAsyncEnumerator();
        Assert.True(await again.MoveNextAsync());
        Assert.Equal(payload, again.Current);
    }

    [Fact]
    public async Task StoppingWhileTheReaderWaitsEndsTheReadWithoutReleasingTheSource()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module);
        await using var lease = await controller.StartAsync(CancellationToken.None);
        await using var enumerator = lease.ReadPayloadsAsync().GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync();
        await Task.Delay(30);

        await controller.StopAsync(CancellationToken.None);
        var moved = await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(moved);
        Assert.Equal(1, module.Calls.Count(call => call == "disconnect"));
        Assert.DoesNotContain("release", module.Calls);
    }

    [Fact]
    public async Task LeaseWithoutASessionHasNoInjectionMethod()
    {
        var lease = new BrowserCaptureLease(static (_, _) => Task.CompletedTask);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in lease.ReadPayloadsAsync())
            {
            }
        });

        Assert.Contains("no capture session", failure.Message, StringComparison.Ordinal);
        Assert.Null(typeof(BrowserCaptureLease).GetMethod("OnChunk"));
        Assert.DoesNotContain(
            typeof(BrowserCaptureLease).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic),
            method => method.GetCustomAttributes(false).Any(attribute => attribute.GetType().Name == "JSInvokableAttribute"));
    }

    [Fact]
    public async Task LateAcquireAfterAReleaseTimeoutIsStillReleased()
    {
        var module = new ScriptedJsModule { PauseAcquire = true };
        var controller = new BrowserCaptureController(module, new BrowserCaptureControllerOptions
        {
            ReleaseProgressTimeout = TimeSpan.FromMilliseconds(50),
        });
        var access = controller.RequestAccessAsync(CancellationToken.None);
        var sawAcquire = await WaitUntilAsync(() => module.AcquireCount == 1);
        Assert.True(sawAcquire);

        var releaseError = await Record.ExceptionAsync(() => controller.ReleaseAsync(CancellationToken.None));
        Assert.IsType<TimeoutException>(releaseError);

        module.AcquireGate.TrySetResult(true);
        var released = await WaitUntilAsync(() => module.Calls.Contains("release"));
        Assert.True(released);
        await access.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task BrowserReleaseErrorStaysAJsException()
    {
        var module = new ScriptedJsModule { ReleaseFailuresRemaining = 1 };
        var controller = new BrowserCaptureController(module);
        await controller.RequestAccessAsync(CancellationToken.None);

        var failure = await Assert.ThrowsAsync<JSException>(() => controller.ReleaseAsync(CancellationToken.None));

        Assert.Contains("rejected release", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QueueCapacityMustBePositive()
    {
        var module = new ScriptedJsModule();

        var failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BrowserCaptureController(module, new BrowserCaptureControllerOptions { QueueCapacityChunks = 0 }));

        Assert.Contains("at least one", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeaseWithoutASessionRejectsEnumeration()
    {
        await using var lease = new BrowserCaptureLease(static (_, _) => Task.CompletedTask);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in lease.ReadPayloadsAsync())
            {
            }
        });

        Assert.Contains("no capture session", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureFaultStopsTheSourceAndTheNextStartCanOpenAgain()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module);
        var first = await controller.StartAsync(CancellationToken.None);
        var generation = controller.SourceOwnershipGeneration;

        await module.SessionReference!.Value.OnCaptureFault("MICROPHONE_TRACK_ENDED");

        Assert.True(first.Completion.IsCompleted);
        Assert.False(first.HasEnded);
        Assert.Contains("disconnect", module.Calls);
        Assert.True(controller.SourceOwnershipGeneration > generation);

        module.Live = false;
        await using var second = await controller.StartAsync(CancellationToken.None);

        Assert.Equal("audio/pcm;rate=16000", second.MediaType);
        Assert.Equal(2, module.AcquireCount);
        Assert.Contains("release", module.Calls);
    }

    [Fact]
    public async Task CaptureFaultDuringConnectRejectsTheStart()
    {
        var module = new ScriptedJsModule { PauseStart = true };
        var controller = new BrowserCaptureController(module);
        var start = controller.StartAsync(CancellationToken.None);
        var entered = await WaitUntilAsync(() => module.StartEntered.Task.IsCompleted);
        Assert.True(entered);

        await module.SessionReference!.Value.OnCaptureFault("MICROPHONE_TRACK_ENDED");
        module.StartGate.TrySetResult(true);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => start);
        Assert.Contains("stopped during start", failure.Message, StringComparison.Ordinal);
        Assert.Contains("disconnect", module.Calls);
    }

    [Fact]
    public async Task EmptyPayloadsAreSkippedAndAFaultCarriesTheBrowserMessage()
    {
        var module = new ScriptedJsModule();
        var controller = new BrowserCaptureController(module);
        await using var lease = await controller.StartAsync(CancellationToken.None);
        var session = module.SessionReference!.Value;
        session.OnPcmChunk(null);
        session.OnPcmChunk([]);
        session.OnPcmChunk(Sample());

        await using var reader = lease.ReadPayloadsAsync().GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(Sample(), reader.Current);

        await session.OnCaptureFault("   ");
        var failure = await Assert.ThrowsAsync<MicrophoneException>(async () => await reader.MoveNextAsync());
        Assert.Contains("Browser microphone capture failed.", failure.Message, StringComparison.Ordinal);
    }

    private static byte[] Sample()
    {
        var bytes = new byte[PcmFormat.Default.BytesPerChunk];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index + 1);
        }

        return bytes;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }
}
