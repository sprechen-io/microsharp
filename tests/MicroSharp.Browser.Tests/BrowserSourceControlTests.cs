namespace MicroSharp.Browser.Tests;

public sealed class BrowserSourceControlTests
{
    [Fact]
    public async Task DirectStartLeavesTheSourceEnabledAndDoesNotKeepTheContextAlive()
    {
        var module = new ScriptedJsModule();
        var capture = new BrowserMicrophoneCapture(module);

        await using var session = await capture.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None);

        Assert.DoesNotContain("setSourceEnabled", module.Calls);
        Assert.NotNull(module.LastAcquirePreferences);
        Assert.Equal(false, module.LastAcquirePreferences["keepAudioContextAlive"]);
        Assert.Contains("stop", module.Calls.Concat(await StopAsync(session, module)));
    }

    [Fact]
    public async Task SourceCanBeDisabledBeforeTheWorkletConnects()
    {
        var module = new ScriptedJsModule();
        var capture = new BrowserMicrophoneCapture(module);
        var prepared = await capture.PrepareAsync(keepAudioContextAlive: true, CancellationToken.None);
        var hold = prepared.Hold ?? throw new InvalidOperationException("Expected a held source.");

        await hold.SetSourceEnabledAsync(false, CancellationToken.None);
        await using var session = await hold.StartAsync(new MicrophoneCaptureRequest(), CancellationToken.None);

        var disabledAt = module.Calls.IndexOf("setSourceEnabled");
        var startedAt = module.Calls.IndexOf("start");
        Assert.True(disabledAt >= 0 && startedAt > disabledAt);
        Assert.Equal(new[] { false }, module.SourceEnabled);
        Assert.Equal(true, module.LastAcquirePreferences?["keepAudioContextAlive"]);
        await session.DisposeAsync();
        await hold.DisposeAsync();
    }

    [Fact]
    public async Task HeldSourceCanRestartUntilItIsReleased()
    {
        var module = new ScriptedJsModule();
        var capture = new BrowserMicrophoneCapture(module);
        var hold = (await capture.PrepareAsync(false, CancellationToken.None)).Hold
            ?? throw new InvalidOperationException("Expected a held source.");
        var request = new MicrophoneCaptureRequest();

        var first = await hold.StartAsync(request, CancellationToken.None);
        await first.StopAsync();
        Assert.Contains("disconnect", module.Calls);
        Assert.DoesNotContain("release", module.Calls);

        var second = await hold.StartAsync(request, CancellationToken.None);
        Assert.Equal(2, module.Calls.Count(call => call == "start"));
        await second.DisposeAsync();
        await hold.DisposeAsync();

        Assert.Contains("release", module.Calls);
        Assert.False(await hold.IsLiveAsync());
    }

    [Fact]
    public async Task LivenessAndTrackProcessingComeFromTheHeldSource()
    {
        var module = new ScriptedJsModule();
        var capture = new BrowserMicrophoneCapture(module);
        var hold = (await capture.PrepareAsync(false, CancellationToken.None)).Hold
            ?? throw new InvalidOperationException("Expected a held source.");

        Assert.True(await hold.IsLiveAsync());
        module.Live = false;
        Assert.False(await hold.IsLiveAsync());
        var track = await hold.ReadTrackProcessingAsync();

        Assert.True(track.EchoCancellation);
        Assert.False(track.NoiseSuppression);
        Assert.Null(track.AutoGainControl);
        await hold.DisposeAsync();
        Assert.False(await hold.IsLiveAsync());
        Assert.Null((await hold.ReadTrackProcessingAsync()).EchoCancellation);
    }

    private static async Task<IEnumerable<string>> StopAsync(IMicrophoneSession session, ScriptedJsModule module)
    {
        await session.DisposeAsync();
        return module.Calls;
    }
}
