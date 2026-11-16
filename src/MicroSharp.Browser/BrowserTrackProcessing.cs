using System.Text.Json.Serialization;

namespace MicroSharp.Browser;

/// <summary>
/// Vom Browser gemeldete Verarbeitung der Mikrofonspur.
/// Ein nicht lesbarer Wert bleibt <see langword="null"/>.
/// </summary>
public sealed class BrowserTrackProcessing
{
    /// <summary>Leere Meldung, wenn keine Spur gelesen werden kann.</summary>
    public static BrowserTrackProcessing Unknown { get; } = new();

    /// <summary>Angewendete Echounterdrueckung.</summary>
    [JsonPropertyName("echoCancellation")]
    public bool? EchoCancellation { get; set; }

    /// <summary>Angewendete Rauschunterdrueckung.</summary>
    [JsonPropertyName("noiseSuppression")]
    public bool? NoiseSuppression { get; set; }

    /// <summary>Angewendete automatische Verstaerkung.</summary>
    [JsonPropertyName("autoGainControl")]
    public bool? AutoGainControl { get; set; }
}
