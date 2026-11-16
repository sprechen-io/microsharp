namespace MicroSharp.Processing;

/// <summary>
/// The cursor points past the last byte written so far.
/// A cursor exactly at the write position is caught up and is not invalid.
/// </summary>
public sealed class PcmHistoryCursorInvalidException : ArgumentOutOfRangeException
{
    /// <summary>Creates the exception for a cursor beyond the written stream.</summary>
    public PcmHistoryCursorInvalidException(long requestedBytePosition, long writeBytePosition)
        : base(
            nameof(requestedBytePosition),
            requestedBytePosition,
            $"PCM history cursor at byte {requestedBytePosition} is past the written byte {writeBytePosition}.")
    {
        RequestedBytePosition = requestedBytePosition;
        WriteBytePosition = writeBytePosition;
    }

    /// <summary>Byte offset the caller tried to read.</summary>
    public long RequestedBytePosition { get; }

    /// <summary>Byte offset of the next byte that has not been written.</summary>
    public long WriteBytePosition { get; }
}
