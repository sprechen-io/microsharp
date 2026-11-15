namespace MicroSharp.Native;

internal sealed class OpenAlMicrophoneSession : IMicrophoneSession
{
    private readonly MicrophoneSession _session;
    private readonly IOpenAlCaptureHandle _handle;
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeProvider _time;
    private readonly Action _released;
    private readonly object _gate = new();
    private readonly Task _producer;
    private bool _closed;
    private int _disposeState;

    public OpenAlMicrophoneSession(
        MicrophoneCaptureRequest request,
        IOpenAlCaptureHandle handle,
        TimeProvider time,
        Action released)
    {
        _session = new MicrophoneSession(
            request.Format,
            request.Backpressure,
            request.QueueCapacityChunks,
            time);
        _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _released = released ?? throw new ArgumentNullException(nameof(released));
        _producer = Task.Run(ProduceAsync);
    }

    public PcmFormat Format => _session.Format;

    public MicrophoneSessionState State => _session.State;

    public long DroppedChunkCount => _session.DroppedChunkCount;

    public IAsyncEnumerable<PcmChunk> ReadChunksAsync(CancellationToken cancellationToken = default)
        => _session.ReadChunksAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stop.Cancel();
        await _producer.WaitAsync(cancellationToken).ConfigureAwait(false);
        ReleaseDevice();
        await _session.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        try
        {
            await _producer.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _session.Complete(exception);
        }

        ReleaseDevice();
        await _session.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
        _released();
    }

    private async Task ProduceAsync()
    {
        var samples = new short[Format.SamplesPerChunk];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var read = false;
                lock (_gate)
                {
                    if (!_closed && _handle.AvailableSamples >= Format.SamplesPerChunk)
                    {
                        _handle.Read(samples);
                        read = true;
                    }
                }

                if (!read)
                {
                    await Task.Delay(Format.ChunkDuration / 2, _time, _stop.Token).ConfigureAwait(false);
                    continue;
                }

                await _session.WriteAsync(PcmEncoding.FromSamples(samples), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _session.Complete(exception);
        }
    }

    private void ReleaseDevice()
    {
        List<byte[]> drained = [];
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                _handle.Stop();
                var samples = new short[Format.SamplesPerChunk];
                while (_handle.AvailableSamples >= Format.SamplesPerChunk)
                {
                    _handle.Read(samples);
                    drained.Add(PcmEncoding.FromSamples(samples));
                }
            }
            finally
            {
                try
                {
                    _handle.Dispose();
                }
                finally
                {
                    _closed = true;
                }
            }
        }

        foreach (var payload in drained)
        {
            var write = _session.WriteAsync(payload, CancellationToken.None);
            if (!write.IsCompletedSuccessfully)
            {
                write.AsTask().GetAwaiter().GetResult();
            }
        }
    }
}
