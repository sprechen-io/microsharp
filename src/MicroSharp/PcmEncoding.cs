using System.Buffers.Binary;

namespace MicroSharp;

/// <summary>
/// Wandelt vorzeichenbehaftete 16-Bit-Abtastwerte in little-endian-Bytes und zurueck.
/// </summary>
public static class PcmEncoding
{
    /// <summary>Schreibt die Abtastwerte als little-endian-Bytes.</summary>
    public static byte[] FromSamples(ReadOnlySpan<short> samples)
    {
        var bytes = new byte[checked(samples.Length * sizeof(short))];
        CopySamples(samples, bytes);
        return bytes;
    }

    /// <summary>Schreibt die Abtastwerte in den Zielpuffer.</summary>
    public static void CopySamples(ReadOnlySpan<short> samples, Span<byte> destination)
    {
        if (destination.Length < samples.Length * sizeof(short))
        {
            throw new ArgumentException("The destination is smaller than the encoded samples.", nameof(destination));
        }

        for (var index = 0; index < samples.Length; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(destination[(index * sizeof(short))..], samples[index]);
        }
    }

    /// <summary>Liest little-endian-Bytes als Abtastwerte.</summary>
    public static short[] ToSamples(ReadOnlySpan<byte> payload)
    {
        if (payload.Length % sizeof(short) != 0)
        {
            throw new ArgumentException("PCM payload length must be a multiple of two bytes.", nameof(payload));
        }

        var samples = new short[payload.Length / sizeof(short)];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(payload[(index * sizeof(short))..]);
        }

        return samples;
    }
}
