using System.Buffers.Binary;

namespace MicroSharp.Native;

/// <summary>
/// Liest rohe little-endian-PCM-Bytes ohne WAV-Kopf.
/// Die aufrufende Seite besitzt den Stream und gibt ihn frei.
/// </summary>
public sealed class StreamPcmSource : IPcmSource
{
    private readonly Stream _stream;

    /// <summary>Erstellt die Quelle fuer einen lesbaren Stream.</summary>
    public StreamPcmSource(Stream stream, PcmFormat format)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead)
        {
            throw new ArgumentException("The PCM stream must be readable.", nameof(stream));
        }

        _stream = stream;
        Format = format ?? throw new ArgumentNullException(nameof(format));
    }

    /// <inheritdoc />
    public PcmFormat Format { get; }

    /// <inheritdoc />
    public bool TryReadChunk(Span<short> destination)
    {
        if (destination.Length != Format.SamplesPerChunk)
        {
            throw new ArgumentException("The destination must hold exactly one chunk.", nameof(destination));
        }

        var needed = checked(destination.Length * sizeof(short));
        var buffer = new byte[needed];
        var read = 0;
        while (read < needed)
        {
            var count = _stream.Read(buffer, read, needed - read);
            if (count == 0)
            {
                return false;
            }

            read += count;
        }

        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = BinaryPrimitives.ReadInt16LittleEndian(
                buffer.AsSpan(index * sizeof(short), sizeof(short)));
        }

        return true;
    }
}
