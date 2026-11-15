namespace MicroSharp.Native;

internal sealed class VirtualMicrophoneSession : IMicrophoneSession
{
    private readonly MicrophoneSession _session;
    private readonly IPcmSource _source;
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeProvider _time;
    private readonly bool _pace;
    private readonly Action _released;
    private readonly Task _producer;
    private int _disposeState;

    public VirtualMicrophoneSession(
        MicrophoneCaptureRequest request,
        IPcmSource source,
        bool paceInRealTime,
        TimeProvider time,
        Action released)
    {
        _session = new MicrophoneSession(
            request.Format,
            request.Backpressure,
            request.QueueCapacityChunks,
            time);
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _pace = paceInRealTime;
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

        await _session.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
        _released();
    }

    private async Task ProduceAsync()
    {
        var samples = new short[Format.SamplesPerChunk];
        var first = true;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (!first && _pace)
                {
                    await Task.Delay(Format.ChunkDuration, _time, _stop.Token).ConfigureAwait(false);
                }

                first = false;
                if (!_source.TryReadChunk(samples))
                {
                    _session.Complete();
                    return;
                }

                var payload = PcmEncoding.FromSamples(samples);
                await _session.WriteAsync(payload, _stop.Token).ConfigureAwait(false);
            }

            _session.Complete();
        }
        catch (OperationCanceledException)
        {
            _session.Complete();
        }
        catch (Exception exception)
        {
            _session.Complete(exception);
        }
    }
}
