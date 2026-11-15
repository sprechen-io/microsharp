namespace MicroSharp;

/// <summary>
/// Gewuenschtes Format und Verhalten des Puffers fuer eine Aufnahme.
/// </summary>
public sealed class MicrophoneCaptureRequest
{
    /// <summary>Zu lieferndes Format. Der Vorgabewert ist <see cref="PcmFormat.Default"/>.</summary>
    public PcmFormat Format { get; init; } = PcmFormat.Default;

    /// <summary>Verhalten bei vollem Puffer. Der Vorgabewert verwirft den aeltesten Block.</summary>
    public CaptureBackpressure Backpressure { get; init; } = CaptureBackpressure.DropOldest;

    /// <summary>Anzahl der Bloecke, die auf einen Leser warten duerfen.</summary>
    public int QueueCapacityChunks { get; init; } = 50;
}
