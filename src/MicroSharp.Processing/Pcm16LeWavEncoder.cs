using System.Buffers.Binary;

namespace MicroSharp.Processing;

/// <summary>Exports interleaved signed 16-bit little-endian PCM as a PCM RIFF/WAVE file.</summary>
public static class Pcm16LeWavEncoder
{
    private const int HeaderSize = 44;

    /// <summary>
    /// Returns a new array containing a 44-byte WAV header followed by the unchanged PCM bytes.
    /// An empty payload produces a header-only WAV.
    /// </summary>
    /// <param name="pcm16Le">Interleaved PCM bytes containing an integral number of frames.</param>
    /// <param name="sampleRateHz">Positive sample rate in hertz.</param>
    /// <param name="channels">Positive number of channels, defaulting to mono.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The rate or channel count is invalid, a format field would overflow, or the output
    /// exceeds RIFF or managed byte-array size limits.
    /// </exception>
    /// <exception cref="ArgumentException">The payload ends with an incomplete frame.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> pcm16Le, int sampleRateHz, int channels = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRateHz);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, ushort.MaxValue / sizeof(short));

        var blockAlignment = channels * sizeof(short);
        var byteRate = (long)sampleRateHz * blockAlignment;
        if (byteRate > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "The byte rate exceeds the RIFF field limit.");
        }

        if (pcm16Le.Length % blockAlignment != 0)
        {
            throw new ArgumentException("PCM payload length must be a multiple of the frame size.", nameof(pcm16Le));
        }

        if (!FitsOutputLimits(pcm16Le.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(pcm16Le), "The WAV output exceeds RIFF or byte-array size limits.");
        }

        var output = new byte[HeaderSize + pcm16Le.Length];
        var header = output.AsSpan(0, HeaderSize);
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)(output.Length - 8));
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[22..], (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)sampleRateHz);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..], (uint)byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..], (ushort)blockAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], (uint)pcm16Le.Length);
        pcm16Le.CopyTo(output.AsSpan(HeaderSize));
        return output;
    }

    internal static bool FitsOutputLimits(int payloadLength)
    {
        var outputLength = (long)HeaderSize + payloadLength;
        return outputLength - 8 <= uint.MaxValue && outputLength <= Array.MaxLength;
    }
}
