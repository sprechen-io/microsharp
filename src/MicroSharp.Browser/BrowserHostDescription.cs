using System.Text.Json.Serialization;

namespace MicroSharp.Browser;

/// <summary>
/// Meldet, ob das aktuelle Dokument die fuer die Aufnahme noetigen Browser-APIs besitzt.
/// </summary>
public sealed class BrowserHostDescription
{
    /// <summary><c>navigator.mediaDevices.getUserMedia</c> ist vorhanden.</summary>
    [JsonPropertyName("media")]
    public bool Media { get; set; }

    /// <summary><c>AudioContext</c> ist vorhanden.</summary>
    [JsonPropertyName("audio")]
    public bool Audio { get; set; }

    /// <summary><c>AudioWorkletNode</c> ist vorhanden.</summary>
    [JsonPropertyName("worklet")]
    public bool Worklet { get; set; }
}
