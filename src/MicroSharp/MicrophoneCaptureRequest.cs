namespace MicroSharp;

/// <summary>
/// Requested format and buffer behavior for a capture.
/// </summary>
public sealed class MicrophoneCaptureRequest
{
    /// <summary>Format to deliver. The default is <see cref="PcmFormat.Default"/>.</summary>
    public PcmFormat Format { get; init; } = PcmFormat.Default;

    /// <summary>Behavior when the buffer is full. The default discards the oldest chunk.</summary>
    public CaptureBackpressure Backpressure { get; init; } = CaptureBackpressure.DropOldest;

    /// <summary>Number of chunks that may wait for a reader.</summary>
    public int QueueCapacityChunks { get; init; } = 50;
}
