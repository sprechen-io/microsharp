namespace MicroSharp.Processing;

/// <summary>
/// Aligns successive signed 16-bit little-endian PCM blocks to two-byte sample boundaries.
/// Use one instance per stream and feed blocks in order.
/// </summary>
public sealed class Pcm16LeAlignmentBuffer
{
    private byte? _carry;

    /// <summary>
    /// Emits every complete sample available, in byte order, and retains at most one byte.
    /// An empty block emits an empty array and preserves any retained byte.
    /// </summary>
    /// <remarks>
    /// A nonempty even-length array passes through by reference when there is no retained byte.
    /// The caller retains ownership and must keep that array valid while the output is in use.
    /// All other nonempty results are independent arrays sized exactly to the emitted samples.
    /// </remarks>
    public byte[] Align(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length == 0)
        {
            return [];
        }

        var prefixLength = _carry.HasValue ? 1 : 0;
        var availableLength = (long)bytes.Length + prefixLength;
        var outputLength = availableLength & ~1L;
        if (prefixLength == 0 && outputLength == bytes.Length)
        {
            return bytes;
        }

        if (outputLength > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "The aligned output exceeds the byte-array limit.");
        }

        var output = outputLength == 0 ? [] : new byte[(int)outputLength];
        if (_carry is byte carry)
        {
            output[0] = carry;
        }

        bytes.AsSpan(0, output.Length - prefixLength).CopyTo(output.AsSpan(prefixLength));
        _carry = availableLength == outputLength ? null : bytes[^1];
        return output;
    }

    /// <summary>
    /// Finishes and resets the stream. Returns true if an incomplete sample was discarded.
    /// No padding is emitted. The next block starts a new stream.
    /// </summary>
    public bool Complete()
    {
        var discardedPartialSample = _carry.HasValue;
        _carry = null;
        return discardedPartialSample;
    }
}
