namespace MicroSharp.Processing;

/// <summary>
/// Caller-owned position in a <see cref="PcmHistoryRing"/>.
/// <see cref="BytePosition"/> is an absolute byte offset, not a sample count.
/// </summary>
/// <param name="BytePosition">Absolute byte offset from the start of the stream.</param>
public readonly record struct PcmHistoryCursor(long BytePosition);
