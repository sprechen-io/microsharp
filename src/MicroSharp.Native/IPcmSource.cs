namespace MicroSharp.Native;

/// <summary>
/// Liefert vollstaendige PCM-Bloecke fuer den virtuellen Aufnehmer.
/// Ein unvollstaendiger letzter Block wird nicht geliefert.
/// </summary>
public interface IPcmSource
{
    /// <summary>Format, in dem <see cref="TryReadChunk"/> schreibt.</summary>
    PcmFormat Format { get; }

    /// <summary>
    /// Fuellt <paramref name="destination"/> mit dem naechsten Block.
    /// <see langword="false"/> bedeutet, dass die Quelle beendet ist.
    /// </summary>
    bool TryReadChunk(Span<short> destination);
}
