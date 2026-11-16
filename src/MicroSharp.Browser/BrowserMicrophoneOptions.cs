namespace MicroSharp.Browser;

/// <summary>
/// Settings of the browser adapter. They do not require a dependency-injection container.
/// </summary>
public sealed class BrowserMicrophoneOptions
{
    /// <summary>Path of the ES module in the static web assets.</summary>
    public const string DefaultModulePath = "./_content/MicroSharp.Browser/microsharp-capture.js";

    /// <summary>Module path for <c>import</c>.</summary>
    public string ModulePath { get; init; } = DefaultModulePath;

    /// <summary>Request to the browser. <see langword="null"/> leaves the browser default.</summary>
    public bool? EchoCancellation { get; init; } = true;

    /// <summary>Request to the browser. <see langword="null"/> leaves the browser default.</summary>
    public bool? NoiseSuppression { get; init; } = true;

    /// <summary>Request to the browser. <see langword="null"/> leaves the browser default.</summary>
    public bool? AutoGainControl { get; init; } = true;

    /// <summary>
    /// Keeps the audio context open with an inaudible signal.
    /// The default is off, so a capture without this request does not produce an extra sound.
    /// </summary>
    public bool KeepAudioContextAlive { get; init; }
}
