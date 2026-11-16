using System.Runtime.CompilerServices;

using MicroSharp;

namespace MicroSharp.Browser;

/// <summary>
/// One capture started by <see cref="BrowserCaptureController"/>.
/// Payloads come from the browser session. This type does not keep a second queue.
/// </summary>
public sealed class BrowserCaptureLease : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly object _sync = new();
    private readonly Func<BrowserCaptureLease, CancellationToken, Task> _stopAsync;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TaskCompletionSource? _stopAttempt;
    private IMicrophoneSession? _captureSession;
    private bool _disposed;

    internal BrowserCaptureLease(Func<BrowserCaptureLease, CancellationToken, Task> stopAsync)
    {
        _stopAsync = stopAsync ?? throw new ArgumentNullException(nameof(stopAsync));
    }

    /// <summary>Confirmed content type, or empty until the browser confirms the format.</summary>
    public string MediaType { get; private set; } = string.Empty;

    /// <summary>Chunks discarded by the library capture queue.</summary>
    public long DroppedChunkCount => _captureSession?.DroppedChunkCount ?? 0;

    /// <summary>Completes after a successful stop or release. A failed start also completes it.</summary>
    public Task Completion => _stopped.Task;

    /// <summary>True after the lease has been retired by failure, release or disposal. A stop alone does not retire it.</summary>
    public bool HasEnded { get; private set; }

    /// <summary>
    /// Reads payload bytes in enqueue order. Empty payloads are skipped.
    /// Cancelling or disposing the enumeration ends only the read.
    /// </summary>
    public IAsyncEnumerable<byte[]> ReadPayloadsAsync(CancellationToken cancellationToken = default)
    {
        var session = _captureSession;
        if (session is null)
        {
            throw new InvalidOperationException("This recording has no capture session.");
        }

        return ReadPayloads(session, cancellationToken);
    }

    /// <summary>Stops capture. A repeated call shares the same attempt.</summary>
    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource stopAttempt;
        var startsAttempt = false;
        lock (_sync)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            if (_stopAttempt is null)
            {
                _stopAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                startsAttempt = true;
            }

            stopAttempt = _stopAttempt;
        }

        if (!startsAttempt)
        {
            return new ValueTask(stopAttempt.Task.WaitAsync(cancellationToken));
        }

        return new ValueTask(CompleteStopAttemptAsync(stopAttempt, cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        await StopAsync().ConfigureAwait(false);

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            HasEnded = true;
        }

        CompleteStopped();
        await ReleaseAttachedCaptureAsync().ConfigureAwait(false);
    }

    internal void AttachCapture(IMicrophoneSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _captureSession = session;
    }

    internal void SetMediaType(string? mediaType)
    {
        MediaType = mediaType?.Trim() ?? string.Empty;
    }

    internal void StartFailed(Exception exception)
    {
        _ = exception;
        if (_disposed)
        {
            return;
        }

        _stopped.TrySetResult();
        _disposed = true;
        HasEnded = true;
    }

    internal void MarkStopped()
    {
        if (_disposed)
        {
            return;
        }

        CompleteStopped();
    }

    internal void ReleaseCaptureEnded()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            HasEnded = true;
        }

        CompleteStopped();
    }

    private async Task CompleteStopAttemptAsync(TaskCompletionSource stopAttempt, CancellationToken cancellationToken)
    {
        try
        {
            await _stopAsync(this, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_stopAttempt, stopAttempt))
                {
                    _stopAttempt = null;
                }
            }

            stopAttempt.TrySetException(exception);
            await stopAttempt.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var completed = await Task.WhenAny(
                _stopped.Task,
                Task.Delay(StopTimeout))
            .ConfigureAwait(false);

        if (completed != _stopped.Task)
        {
            _stopped.TrySetResult();
        }

        stopAttempt.TrySetResult();
        await stopAttempt.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ReleaseAttachedCaptureAsync()
    {
        IMicrophoneSession? session;
        lock (_sync)
        {
            session = _captureSession;
            _captureSession = null;
        }

        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void CompleteStopped()
    {
        _stopped.TrySetResult();
    }

    private async IAsyncEnumerable<byte[]> ReadPayloads(
        IMicrophoneSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in session.ReadChunksAsync(cancellationToken).ConfigureAwait(false))
        {
            var payload = chunk.Payload;
            if (payload.Length == 0)
            {
                continue;
            }

            yield return payload;
        }
    }
}
