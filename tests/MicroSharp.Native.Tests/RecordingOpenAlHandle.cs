namespace MicroSharp.Native.Tests;

internal sealed class RecordingOpenAlHandle : IOpenAlCaptureHandle
{
    private readonly short[] _pattern;
    private int _remainingChunks;
    private bool _started;
    private bool _closed;

    public RecordingOpenAlHandle(short sample, int chunks)
    {
        _pattern = new short[PcmFormat.Default.SamplesPerChunk];
        Array.Fill(_pattern, sample);
        _remainingChunks = chunks;
    }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int DisposeCount { get; private set; }

    public int ReadCount { get; private set; }

    public Action? OnStart { get; set; }

    public void Start()
    {
        StartCount++;
        _started = true;
        OnStart?.Invoke();
    }

    public int AvailableSamples => _closed || _remainingChunks <= 0 ? 0 : _pattern.Length;

    public void Read(short[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        _pattern.CopyTo(samples, 0);
        _remainingChunks--;
        ReadCount++;
    }

    public void Stop()
    {
        if (_closed || !_started)
        {
            return;
        }

        StopCount++;
        _started = false;
    }

    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        Stop();
        _closed = true;
        DisposeCount++;
    }
}
