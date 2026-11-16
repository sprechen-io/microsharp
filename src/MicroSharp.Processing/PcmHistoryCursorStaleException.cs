namespace MicroSharp.Processing;

/// <summary>
/// The cursor points at a byte the ring has already overwritten.
/// The ring does not move the cursor forward.
/// </summary>
public sealed class PcmHistoryCursorStaleException : InvalidOperationException
{
    /// <summary>Creates the exception for a cursor behind the retained window.</summary>
    public PcmHistoryCursorStaleException(long requestedBytePosition, long earliestRetainedBytePosition)
        : base(
            $"PCM history cursor at byte {requestedBytePosition} fell behind the earliest retained byte {earliestRetainedBytePosition}.")
    {
        RequestedBytePosition = requestedBytePosition;
        EarliestRetainedBytePosition = earliestRetainedBytePosition;
    }

    /// <summary>Byte offset the caller tried to read.</summary>
    public long RequestedBytePosition { get; }

    /// <summary>Oldest byte offset still stored.</summary>
    public long EarliestRetainedBytePosition { get; }
}
