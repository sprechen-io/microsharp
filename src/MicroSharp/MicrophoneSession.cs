using System.Runtime.CompilerServices;

namespace MicroSharp;

/// <summary>
/// Puffert PCM-Bloecke fuer genau einen Leser. Adapter schreiben, Anwendungen lesen.
/// </summary>
internal sealed class MicrophoneSession : IMicrophoneSession
{
    private readonly object _gate = new();
    private readonly Queue<PcmChunk> _pending;
    private readonly CaptureBackpressure _backpressure;
    private readonly int _capacity;
    private readonly TimeProvider _time;
    private readonly long _startedAt;
    private TaskCompletionSource<bool>? _readerReady;
    private TaskCompletionSource<bool>? _spaceReady;
    private bool _readerActive;
    private long _sequence;
    private long _dropped;
    private Exception? _error;
    private bool _stopRequested;
    private bool _disposed;
    private MicrophoneSessionState _state;

    public MicrophoneSession(
        PcmFormat format,
        CaptureBackpressure backpressure,
        int capacity,
        TimeProvider? timeProvider)
    {
        Format = format ?? throw new ArgumentNullException(nameof(format));
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (!Enum.IsDefined(backpressure))
        {
            throw new ArgumentOutOfRangeException(nameof(backpressure));
        }

        _backpressure = backpressure;
        _capacity = capacity;
        _pending = [with(capacity: capacity)];
        _time = timeProvider ?? TimeProvider.System;
        _startedAt = _time.GetTimestamp();
        _state = MicrophoneSessionState.Running;
    }

    public PcmFormat Format { get; }

    public MicrophoneSessionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public long DroppedChunkCount
    {
        get
        {
            lock (_gate)
            {
                return _dropped;
            }
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length != Format.BytesPerChunk)
        {
            var error = new MicrophoneException(
                MicrophoneErrorCode.InvalidChunk,
                $"Expected {Format.BytesPerChunk} bytes and received {payload.Length}.");
            Fault(error);
            throw error;
        }

        var copy = payload.ToArray();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaskCompletionSource<bool> wait;
            lock (_gate)
            {
                if (_disposed || _stopRequested || _error is not null)
                {
                    return;
                }

                if (_pending.Count >= _capacity && _backpressure == CaptureBackpressure.Wait)
                {
                    _spaceReady ??= CreateSignal();
                    wait = _spaceReady;
                }
                else
                {
                    if (_pending.Count >= _capacity)
                    {
                        _pending.Dequeue();
                        _dropped++;
                    }

                    _pending.Enqueue(new PcmChunk(_sequence, _time.GetElapsedTime(_startedAt), copy));
                    _sequence++;
                    Signal(_readerReady);
                    _readerReady = null;
                    return;
                }
            }

            await wait.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Complete(Exception? error = null)
    {
        lock (_gate)
        {
            if (_stopRequested || _disposed)
            {
                return;
            }

            _stopRequested = true;
            if (error is not null && _error is null)
            {
                _error = error as MicrophoneException
                    ?? new MicrophoneException(MicrophoneErrorCode.Unavailable, error.Message, error);
                _state = MicrophoneSessionState.Faulted;
            }
            else if (_error is null)
            {
                _state = MicrophoneSessionState.Stopped;
            }

            Signal(_readerReady);
            _readerReady = null;
            Signal(_spaceReady);
            _spaceReady = null;
        }
    }

    public async IAsyncEnumerable<PcmChunk> ReadChunksAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_readerActive)
            {
                throw new InvalidOperationException("The microphone session already has a reader.");
            }

            _readerActive = true;
        }

        try
        {
            while (true)
            {
                TaskCompletionSource<bool>? wait = null;
                PcmChunk? chunk = null;
                Exception? error = null;
                var ended = false;
                lock (_gate)
                {
                    if (_pending.Count > 0)
                    {
                        chunk = _pending.Dequeue();
                        Signal(_spaceReady);
                        _spaceReady = null;
                    }
                    else if (_error is not null)
                    {
                        error = _error;
                    }
                    else if (_stopRequested || _disposed)
                    {
                        ended = true;
                    }
                    else
                    {
                        _readerReady ??= CreateSignal();
                        wait = _readerReady;
                    }
                }

                if (chunk is not null)
                {
                    yield return chunk;
                    continue;
                }

                if (error is not null)
                {
                    throw error;
                }

                if (ended)
                {
                    yield break;
                }

                await wait!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                _readerActive = false;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Complete();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Complete();
        lock (_gate)
        {
            _disposed = true;
            _state = MicrophoneSessionState.Disposed;
            Signal(_readerReady);
            _readerReady = null;
            Signal(_spaceReady);
            _spaceReady = null;
        }

        return ValueTask.CompletedTask;
    }

    private void Fault(MicrophoneException error)
    {
        lock (_gate)
        {
            if (_error is null && !_disposed && !_stopRequested)
            {
                _error = error;
                _stopRequested = true;
                _state = MicrophoneSessionState.Faulted;
                Signal(_readerReady);
                _readerReady = null;
                Signal(_spaceReady);
                _spaceReady = null;
            }
        }
    }

    private static TaskCompletionSource<bool> CreateSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Signal(TaskCompletionSource<bool>? signal)
        => signal?.TrySetResult(true);
}
