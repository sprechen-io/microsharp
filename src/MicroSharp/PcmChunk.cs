namespace MicroSharp;

/// <summary>
/// One complete PCM chunk. <paramref name="Payload"/> belongs to the receiver.
/// </summary>
/// <param name="Sequence">Running number starting at 0 in enqueue order.</param>
/// <param name="CapturedAt">Time since the session started.</param>
/// <param name="Payload">Little-endian signed 16-bit samples.</param>
public sealed record PcmChunk(long Sequence, TimeSpan CapturedAt, byte[] Payload);
