using System.Text.Json.Serialization;

namespace MicroSharp.Browser;

/// <summary>
/// Microphone-track processing reported by the browser.
/// A value that cannot be read stays <see langword="null"/>.
/// </summary>
public sealed class BrowserTrackProcessing
{
    /// <summary>Empty report when no track can be read.</summary>
    public static BrowserTrackProcessing Unknown { get; } = new();

    /// <summary>Echo cancellation that was applied.</summary>
    [JsonPropertyName("echoCancellation")]
    public bool? EchoCancellation { get; set; }

    /// <summary>Noise suppression that was applied.</summary>
    [JsonPropertyName("noiseSuppression")]
    public bool? NoiseSuppression { get; set; }

    /// <summary>Automatic gain control that was applied.</summary>
    [JsonPropertyName("autoGainControl")]
    public bool? AutoGainControl { get; set; }
}
