namespace MicroSharp.Native.Tests;

internal sealed class CountingPcmSource : IPcmSource
{
    private readonly IPcmSource _inner;

    public CountingPcmSource(IPcmSource inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Format = inner.Format;
    }

    public int Reads { get; private set; }

    public bool FailOnRead { get; set; }

    public PcmFormat Format { get; }

    public bool TryReadChunk(Span<short> destination)
    {
        if (FailOnRead)
        {
            throw new InvalidOperationException("The source was read.");
        }

        Reads++;
        return _inner.TryReadChunk(destination);
    }
}
