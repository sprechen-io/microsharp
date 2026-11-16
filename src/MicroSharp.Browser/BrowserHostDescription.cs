using System.Text.Json.Serialization;

namespace MicroSharp.Browser;

/// <summary>
/// Reports whether the current document has the browser APIs that capture needs.
/// </summary>
public sealed class BrowserHostDescription
{
    /// <summary><c>navigator.mediaDevices.getUserMedia</c> is present.</summary>
    [JsonPropertyName("media")]
    public bool Media { get; set; }

    /// <summary><c>AudioContext</c> is present.</summary>
    [JsonPropertyName("audio")]
    public bool Audio { get; set; }

    /// <summary><c>AudioWorkletNode</c> is present.</summary>
    [JsonPropertyName("worklet")]
    public bool Worklet { get; set; }
}
