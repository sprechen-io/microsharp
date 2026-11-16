namespace MicroSharp.Native;

/// <summary>
/// Supplies complete PCM chunks for the virtual capturer.
/// An incomplete final chunk is not supplied.
/// </summary>
public interface IPcmSource
{
    /// <summary>Format in which <see cref="TryReadChunk"/> writes.</summary>
    PcmFormat Format { get; }

    /// <summary>
    /// Fills <paramref name="destination"/> with the next chunk.
    /// <see langword="false"/> means the source has ended.
    /// </summary>
    bool TryReadChunk(Span<short> destination);
}
