using Microsoft.JSInterop;

namespace MicroSharp.Browser.Tests;

public sealed class BrowserHoldOwnershipTests
{
    [Fact]
    public async Task ConnectedReleaseFailureStaysRetriableAndKeepsTheSourceLive()
    {
        var module = new ScriptedJsModule { ReleaseFailuresRemaining = 1 };
        var hold = await HoldAsync(module);

        var rejected = await Assert.ThrowsAsync<JSException>(async () => await hold.DisposeAsync());

        Assert.Equal("The browser rejected release.", rejected.Message);
        Assert.True(await hold.IsLiveAsync());
        Assert.Equal(1, module.Calls.Count(call => call == "release"));
        await Assert.ThrowsAsync<MicrophoneException>(() => hold.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None));
        Assert.DoesNotContain("start", module.Calls);

        await hold.DisposeAsync();

        Assert.Equal(2, module.Calls.Count(call => call == "release"));
        Assert.False(await hold.IsLiveAsync());
        await hold.DisposeAsync();
        Assert.Equal(2, module.Calls.Count(call => call == "release"));
    }

    [Fact]
    public async Task ConnectedReleaseFailureDoesNotDisconnectARunningSessionBeforeRetry()
    {
        var module = new ScriptedJsModule { ReleaseFailuresRemaining = 1 };
        var hold = await HoldAsync(module);
        var session = await hold.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None);

        await Assert.ThrowsAsync<JSException>(async () => await hold.DisposeAsync());

        Assert.Equal(MicrophoneSessionState.Disposed, session.State);
        Assert.DoesNotContain("disconnect", module.Calls);
        Assert.True(await hold.IsLiveAsync());

        await hold.DisposeAsync();

        Assert.Equal(2, module.Calls.Count(call => call == "release"));
        Assert.False(await hold.IsLiveAsync());
    }

    [Fact]
    public async Task DisconnectedReleaseIsAlreadyCleaned()
    {
        var module = new ScriptedJsModule { ReleaseDisconnects = true };
        var hold = await HoldAsync(module);

        await hold.DisposeAsync();
        await hold.DisposeAsync();

        Assert.Equal(1, module.Calls.Count(call => call == "release"));
        Assert.False(await hold.IsLiveAsync());
    }

    [Fact]
    public async Task DisposeDuringPendingStartDoesNotPublishTheSession()
    {
        var module = new ScriptedJsModule { PauseStart = true };
        var hold = await HoldAsync(module);
        var starting = hold.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None);
        await module.StartEntered.Task;
        await hold.DisposeAsync();

        Assert.Contains("release", module.Calls);
        Assert.DoesNotContain("disconnect", module.Calls);
        module.StartGate.TrySetResult(true);

        var error = await Assert.ThrowsAsync<MicrophoneException>(async () => await starting);

        Assert.Equal(MicrophoneErrorCode.Unavailable, error.Code);
        Assert.Equal(1, module.Calls.Count(call => call == "start"));
        Assert.Equal(1, module.Calls.Count(call => call == "release"));
        Assert.DoesNotContain("disconnect", module.Calls);
        Assert.False(await hold.IsLiveAsync());
    }

    [Fact]
    public async Task ReleaseFailureDuringPendingStartSurfacesAndStillPublishesThatStart()
    {
        var module = new ScriptedJsModule { PauseStart = true, ReleaseFailuresRemaining = 1 };
        var hold = await HoldAsync(module);
        var starting = hold.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None);
        await module.StartEntered.Task;

        var rejected = await Assert.ThrowsAsync<JSException>(async () => await hold.DisposeAsync());

        Assert.Equal("The browser rejected release.", rejected.Message);
        Assert.Equal(1, module.Calls.Count(call => call == "release"));
        Assert.DoesNotContain("disconnect", module.Calls);
        module.StartGate.TrySetResult(true);

        var session = await starting;
        Assert.Equal(MicrophoneSessionState.Running, session.State);
        Assert.True(await hold.IsLiveAsync());
        await Assert.ThrowsAsync<MicrophoneException>(() => hold.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None));
        Assert.Equal(1, module.Calls.Count(call => call == "start"));

        await hold.DisposeAsync();

        Assert.Equal(2, module.Calls.Count(call => call == "release"));
        Assert.Equal(MicrophoneSessionState.Disposed, session.State);
        Assert.False(await hold.IsLiveAsync());
    }

    [Fact]
    public async Task SetSourceEnabledReachesTheBrowserWhileReleaseIsPending()
    {
        var module = new ScriptedJsModule { PauseRelease = true };
        var hold = await HoldAsync(module);
        var releasing = hold.DisposeAsync();
        await module.ReleaseEntered.Task;

        await hold.SetSourceEnabledAsync(false);

        Assert.Equal([false], module.SourceEnabled);
        module.ReleaseGate.TrySetResult(true);
        await releasing;
        var unavailable = await Assert.ThrowsAsync<MicrophoneException>(
            () => hold.SetSourceEnabledAsync(true).AsTask());

        Assert.Equal(MicrophoneErrorCode.Unavailable, unavailable.Code);
        Assert.Equal(1, module.Calls.Count(call => call == "release"));
    }

    [Fact]
    public async Task OverlappingStartsOnOneHoldPublishOnlyTheFirstSession()
    {
        var module = new ScriptedJsModule { PauseStart = true };
        var hold = await HoldAsync(module);
        var request = new MicrophoneCaptureRequest();
        var first = hold.StartAsync(request, CancellationToken.None);
        await module.StartEntered.Task;
        var second = hold.StartAsync(request, CancellationToken.None);

        Assert.Equal(1, module.Calls.Count(call => call == "start"));
        module.StartGate.TrySetResult(true);

        var session = await first;
        var error = await Assert.ThrowsAsync<MicrophoneException>(async () => await second);

        Assert.Equal(MicrophoneErrorCode.Unavailable, error.Code);
        Assert.Equal(MicrophoneSessionState.Running, session.State);
        Assert.Equal(1, module.Calls.Count(call => call == "start"));
        await session.DisposeAsync();
        await hold.DisposeAsync();
        Assert.Contains("disconnect", module.Calls);
        Assert.Contains("release", module.Calls);
    }

    [Fact]
    public async Task OverlappingDirectStartsKeepOneSessionAndReleaseTheOtherSource()
    {
        var module = new ScriptedJsModule { PauseAcquire = true };
        var capture = new BrowserMicrophoneCapture(module);
        var request = new MicrophoneCaptureRequest();
        var first = capture.StartAsync(request, CancellationToken.None);
        var second = capture.StartAsync(request, CancellationToken.None);

        Assert.Equal(2, module.AcquireCount);
        Assert.DoesNotContain("start", module.Calls);
        module.AcquireGate.TrySetResult(true);

        var left = await SettleAsync(first);
        var right = await SettleAsync(second);
        var session = left.Session ?? right.Session;
        var error = left.Error ?? right.Error;

        Assert.NotNull(session);
        Assert.IsType<MicrophoneException>(error);
        Assert.Equal(MicrophoneErrorCode.Unavailable, ((MicrophoneException)error!).Code);
        Assert.Equal(MicrophoneSessionState.Running, session!.State);
        Assert.Equal(1, module.Calls.Count(call => call == "start"));
        Assert.Contains("release", module.Calls);
        await session.DisposeAsync();
        Assert.Contains("stop", module.Calls);
    }

    private static async Task<BrowserMicrophoneHold> HoldAsync(ScriptedJsModule module)
    {
        var capture = new BrowserMicrophoneCapture(module);
        var prepared = await capture.PrepareAsync(false, CancellationToken.None);
        return prepared.Hold ?? throw new InvalidOperationException("Expected a held source.");
    }

    private static async Task<(IMicrophoneSession? Session, Exception? Error)> SettleAsync(Task<IMicrophoneSession> task)
    {
        try
        {
            return (await task.ConfigureAwait(false), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }
}
