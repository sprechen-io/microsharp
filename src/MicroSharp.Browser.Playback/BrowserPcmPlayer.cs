using Microsoft.JSInterop;

namespace MicroSharp.Browser.Playback;

/// <summary>Plays a bounded stream of mono PCM16LE using an owned browser audio context.</summary>
public sealed class BrowserPcmPlayer : IAsyncDisposable
{
    private const string ModulePath = "_content/MicroSharp.Browser.Playback/microsharp-playback.js";
    private readonly IJSObjectReference player;
    // Regular operations take the gate immediately. Teardown can run while drain awaits rendering.
    private readonly SemaphoreSlim operation = new(1);
    private int pendingTeardowns;
    private int disposeRequested;
    private bool disposed;

    private BrowserPcmPlayer(IJSObjectReference player) => this.player = player;

    /// <summary>Loads the bridge ahead of the user gesture that starts playback.</summary>
    /// <param name="jsRuntime">The browser's JavaScript runtime.</param>
    /// <param name="applicationBaseUri">The host's absolute application base URI, including its trailing slash.</param>
    /// <param name="options">The fixed PCM input rate and FIFO capacity.</param>
    public static async ValueTask<BrowserPcmPlayer> CreateAsync(
        IJSRuntime jsRuntime, Uri applicationBaseUri, BrowserPcmPlaybackOptions options)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        ArgumentNullException.ThrowIfNull(applicationBaseUri);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.InputSampleRate, 8_000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.InputSampleRate, 192_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.CapacityFrames, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.CapacityFrames, 192_000);
        if (!applicationBaseUri.IsAbsoluteUri || !applicationBaseUri.AbsolutePath.EndsWith('/'))
        {
            throw new ArgumentException("Supply an absolute application base URI ending in a slash.", nameof(applicationBaseUri));
        }

        var moduleUrl = new Uri(applicationBaseUri, ModulePath).AbsoluteUri;
        await using var module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", moduleUrl);
        var player = await module.InvokeAsync<IJSObjectReference>(
            "createPlayer", options.InputSampleRate, options.CapacityFrames);
        return new BrowserPcmPlayer(player);
    }

    /// <summary>Starts playback from a user gesture and returns the actual output sample rate in Hz.</summary>
    /// <exception cref="InvalidOperationException">Another playback operation is in progress.</exception>
    public async ValueTask<int> StartAsync()
    {
        await EnterOperationAsync();
        try
        {
            var sampleRate = await player.InvokeAsync<int>("start");
            CheckTeardown();
            return sampleRate;
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>Enqueues the prefix that fits and returns counts in input frames.</summary>
    /// <remarks>Keep the input stable until completion. An empty buffer is valid while playing.</remarks>
    /// <exception cref="ArgumentException">The buffer ends with an incomplete PCM16LE frame.</exception>
    /// <exception cref="InvalidOperationException">Another playback operation is in progress. The input is not copied.</exception>
    public async ValueTask<PcmPlaybackWriteResult> EnqueueAsync(ReadOnlyMemory<byte> pcm16Le)
    {
        await EnterOperationAsync();
        try
        {
            if ((pcm16Le.Length & 1) != 0)
            {
                throw new ArgumentException("PCM16LE input must contain complete two-byte frames.", nameof(pcm16Le));
            }

            var requested = pcm16Le.Length / 2;
            var accepted = await player.InvokeAsync<int>("reserve", requested);
            if (accepted != 0)
            {
                try
                {
                    CheckTeardown();
                    await player.InvokeVoidAsync("enqueue", new object?[] { pcm16Le[..(accepted * 2)].ToArray() });
                }
                catch
                {
                    await player.InvokeVoidAsync("cancelReservation");
                    throw;
                }
            }

            CheckTeardown();
            return new PcmPlaybackWriteResult(accepted, requested - accepted);
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>Reads the counters for the currently running audio context.</summary>
    /// <exception cref="InvalidOperationException">Another playback operation is in progress.</exception>
    public async ValueTask<PcmPlaybackSnapshot> GetSnapshotAsync()
    {
        await EnterOperationAsync();
        try
        {
            var snapshot = await player.InvokeAsync<PcmPlaybackSnapshot>("snapshot");
            CheckTeardown();
            return snapshot;
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>Ends input for the active playback run and waits for its final PCM output to render.</summary>
    /// <remarks>
    /// Call once per run, after awaiting the final write. Stop and start the player before writing again.
    /// Cancellation after sending end-of-input cancels the wait and leaves input closed.
    /// Completion refers to output from the playback worklet. Device latency and downstream processing are outside that boundary.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Another playback operation is in progress.</exception>
    /// <exception cref="OperationCanceledException">The caller canceled the wait.</exception>
    /// <exception cref="JSException">The drain could not start or finish, including a render timeout, processor failure or stop.</exception>
    public async ValueTask DrainAsync(CancellationToken cancellationToken = default)
    {
        await EnterOperationAsync();
        ValueTask drain;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            drain = player.InvokeVoidAsync("drain", cancellationToken);
        }
        finally
        {
            operation.Release();
        }

        await drain;
    }

    /// <summary>Stops playback, discards buffered PCM and waits for the audio context to close.</summary>
    public async ValueTask StopAsync()
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref pendingTeardowns);
        await operation.WaitAsync();
        try
        {
            ThrowIfDisposed();
            await player.InvokeVoidAsync("stop");
        }
        finally
        {
            Interlocked.Decrement(ref pendingTeardowns);
            operation.Release();
        }
    }

    /// <summary>Stops playback and releases the JavaScript reference permanently.</summary>
    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref disposeRequested, 1);
        await operation.WaitAsync();
        try
        {
            if (!disposed)
            {
                disposed = true;
                try
                {
                    await player.InvokeVoidAsync("stop");
                }
                finally
                {
                    await player.DisposeAsync();
                }
            }
        }
        finally
        {
            operation.Release();
        }
    }

    private async ValueTask EnterOperationAsync()
    {
        ThrowIfDisposed();
        if (!await operation.WaitAsync(0))
        {
            throw new InvalidOperationException("Playback is busy.");
        }

        try
        {
            CheckTeardown();
        }
        catch
        {
            operation.Release();
            throw;
        }
    }

    private void CheckTeardown()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref pendingTeardowns) != 0)
        {
            throw new InvalidOperationException("Playback is stopping.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeRequested) != 0, this);
}
