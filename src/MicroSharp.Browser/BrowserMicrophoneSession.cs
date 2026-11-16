using Microsoft.JSInterop;

namespace MicroSharp.Browser;

/// <summary>
/// Accepts PCM chunks from the browser audio worklet.
/// </summary>
public sealed class BrowserMicrophoneSession : IMicrophoneSession
{
    private readonly MicrophoneSession _session;
    private readonly Func<CancellationToken, Task> _stopBrowser;
    private readonly Action _disposed;
    private DotNetObjectReference<BrowserMicrophoneSession>? _reference;
    private int _disposeState;

    internal BrowserMicrophoneSession(
        PcmFormat format,
        int capacity,
        Func<CancellationToken, Task> stopBrowser,
        Action disposed)
    {
        _session = new MicrophoneSession(format, CaptureBackpressure.DropOldest, capacity, TimeProvider.System);
        _stopBrowser = stopBrowser ?? throw new ArgumentNullException(nameof(stopBrowser));
        _disposed = disposed ?? throw new ArgumentNullException(nameof(disposed));
    }

    /// <summary>The agreed format.</summary>
    public PcmFormat Format => _session.Format;

    /// <summary>The lifecycle.</summary>
    public MicrophoneSessionState State => _session.State;

    /// <summary>Discarded chunks.</summary>
    public long DroppedChunkCount => _session.DroppedChunkCount;

    internal void AttachReference(DotNetObjectReference<BrowserMicrophoneSession> reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _reference = reference;
    }

    /// <summary>Called by the browser for each PCM chunk.</summary>
    [JSInvokable]
    public void OnPcmChunk(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0 || Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        try
        {
            var write = _session.WriteAsync(bytes, CancellationToken.None);
            if (!write.IsCompletedSuccessfully)
            {
                _ = ObserveWriteAsync(write);
            }
        }
        catch (Exception exception)
        {
            _session.Complete(exception);
        }
    }

    /// <summary>Called by the browser when capture fails.</summary>
    [JSInvokable]
    public void OnCaptureFault(string? message)
    {
        _session.Complete(new MicrophoneException(
            MicrophoneErrorCode.Unavailable,
            string.IsNullOrWhiteSpace(message) ? "Browser microphone capture failed." : message));
    }

    /// <summary>Called by the browser after the session is released.</summary>
    [JSInvokable]
    public void OnStopped()
    {
        _session.Complete();
    }

    /// <inheritdoc />
    public IAsyncEnumerable<PcmChunk> ReadChunksAsync(CancellationToken cancellationToken = default)
        => _session.ReadChunksAsync(cancellationToken);

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _stopBrowser(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _session.Complete(exception);
            throw;
        }

        await _session.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Discards the session without calling the browser. The caller releases the source.</summary>
    internal async ValueTask AbandonWithoutBrowserCallAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _reference?.Dispose();
        _reference = null;
        _session.Complete();
        await _session.DisposeAsync().ConfigureAwait(false);
        _disposed();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _session.Complete(exception);
        }

        _reference?.Dispose();
        _reference = null;
        await _session.DisposeAsync().ConfigureAwait(false);
        _disposed();
    }

    private async Task ObserveWriteAsync(ValueTask write)
    {
        try
        {
            await write.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _session.Complete(exception);
        }
    }
}
