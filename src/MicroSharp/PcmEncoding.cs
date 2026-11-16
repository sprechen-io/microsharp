using System.Buffers.Binary;

namespace MicroSharp;

/// <summary>
/// Converts signed 16-bit samples to little-endian bytes and back.
/// </summary>
public static class PcmEncoding
{
    /// <summary>Writes the samples as little-endian bytes.</summary>
    public static byte[] FromSamples(ReadOnlySpan<short> samples)
    {
        var bytes = new byte[checked(samples.Length * sizeof(short))];
        CopySamples(samples, bytes);
        return bytes;
    }

    /// <summary>Writes the samples into the destination buffer.</summary>
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

    /// <summary>Reads little-endian bytes as samples.</summary>
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
