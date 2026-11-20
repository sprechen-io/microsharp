using System.Runtime.Versioning;
using Microsoft.JSInterop;
using MicroSharp.Browser.Playback;

namespace MicroSharp.Browser.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserPcmPlayerDrainTests
{
    [Fact(Timeout = 5_000)]
    public async Task CancellationBeforeDispatchSkipsEofAndCancellationAfterDispatchStopsWaiting()
    {
        var interop = new PlaybackInterop();
        await using var player = await CreatePlayerAsync(interop);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => player.DrainAsync(new CancellationToken(true)).AsTask());
        Assert.Equal(0, interop.DrainRequests);

        var drain = player.DrainAsync(cancellation.Token).AsTask();
        Assert.Equal(1, interop.DrainRequests);
        Assert.False(drain.IsCompleted);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(interop.RenderReply.Task.IsCompleted);
        interop.RenderReply.SetResult();
    }

    [Theory(Timeout = 5_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TeardownReachesTheBridgeWhileDrainIsPending(bool dispose)
    {
        var interop = new PlaybackInterop();
        await using var player = await CreatePlayerAsync(interop);
        var drain = player.DrainAsync().AsTask();
        Assert.False(drain.IsCompleted);

        await (dispose ? player.DisposeAsync() : player.StopAsync());
        var error = await Assert.ThrowsAsync<JSException>(() => drain);
        Assert.Equal("Playback stopped.", error.Message);
    }

    [Fact(Timeout = 5_000)]
    public async Task DrainDoesNotDispatchEofDuringAnUnfinishedWrite()
    {
        var interop = new PlaybackInterop { WriteReply = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var player = await CreatePlayerAsync(interop);
        var write = player.EnqueueAsync(new byte[2]).AsTask();
        try
        {
            Assert.False(write.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => player.DrainAsync().AsTask());
            Assert.Equal(0, interop.DrainRequests);
        }
        finally
        {
            interop.WriteReply.SetResult();
        }

        await write;
        var drain = player.DrainAsync().AsTask();
        Assert.Equal(1, interop.DrainRequests);
        interop.RenderReply.SetException(new JSException("Playback drain timed out."));
        var error = await Assert.ThrowsAsync<JSException>(() => drain);
        Assert.Equal("Playback drain timed out.", error.Message);
    }

    private static async ValueTask<BrowserPcmPlayer> CreatePlayerAsync(PlaybackInterop interop)
    {
        var player = await BrowserPcmPlayer.CreateAsync(interop, new Uri("https://localhost/"), new(24_000, 2));
        await player.StartAsync();
        return player;
    }

    private sealed class PlaybackInterop : IJSRuntime, IJSObjectReference
    {
        public TaskCompletionSource RenderReply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? WriteReply { get; init; }
        public int DrainRequests { get; private set; }

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
            => InvokeAsync<T>(identifier, CancellationToken.None, args);

        public async ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args)
        {
            switch (identifier)
            {
                case "import":
                case "createPlayer":
                    return (T)(object)this;
                case "start":
                    return (T)(object)48_000;
                case "reserve":
                    return (T)args![0]!;
                case "enqueue":
                    await WriteReply!.Task;
                    break;
                case "drain":
                    DrainRequests++;
                    await RenderReply.Task.WaitAsync(token);
                    break;
                case "stop":
                    if (DrainRequests != 0)
                    {
                        RenderReply.TrySetException(new JSException("Playback stopped."));
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected JavaScript call {identifier}.");
            }

            return default!;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
