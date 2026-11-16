using MicroSharp;

namespace MicroSharp.Browser;

/// <summary>
/// Caller-selected options for <see cref="BrowserCaptureController"/>.
/// Sample rate and chunk duration come from <see cref="Format"/> and are not assumed.
/// </summary>
public sealed class BrowserCaptureControllerOptions
{
    /// <summary>PCM format used when a start does not pass another format.</summary>
    public PcmFormat Format { get; init; } = PcmFormat.Default;

    /// <summary>Bounded capture queue length in chunks.</summary>
    public int QueueCapacityChunks { get; init; } = 50;

    /// <summary>Browser echo-cancellation request.</summary>
    public bool? EchoCancellation { get; init; } = true;

    /// <summary>Browser noise-suppression request.</summary>
    public bool? NoiseSuppression { get; init; } = true;

    /// <summary>Browser automatic-gain request.</summary>
    public bool? AutoGainControl { get; init; } = true;

    /// <summary>Keeps the audio context alive while the source is held.</summary>
    public bool KeepAudioContextAlive { get; init; } = true;

    /// <summary>How long a release waits before the in-flight browser call is detached.</summary>
    public TimeSpan ReleaseProgressTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Source-enabled value published when a caller cancels an in-flight write.
    /// The caller chooses the value. The controller only serializes it.
    /// </summary>
    public bool SourceEnabledAfterCancellation { get; init; } = true;

    /// <summary>Clock used for release timeouts and the reclaimed interval.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Optional diagnostic callback. The library does not reference an application logger.</summary>
    public Action<Exception, string>? Diagnostic { get; init; }

    /// <summary>Optional callback with the milliseconds between release and the next acquire.</summary>
    public Action<double>? SourceReclaimed { get; init; }
}
