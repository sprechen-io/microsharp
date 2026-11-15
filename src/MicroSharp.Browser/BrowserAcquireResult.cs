using System.Text.Json.Serialization;

namespace MicroSharp.Browser;

/// <summary>
/// Ergebnis von <c>getUserMedia</c> und dem Anlegen der Browsersitzung.
/// </summary>
public sealed class BrowserAcquireResult
{
    /// <summary>Kennung der Sitzung oder leer, wenn der Browser ablehnt.</summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>Name der Browser-Ausnahme, sofern der Zugriff scheitert.</summary>
    [JsonPropertyName("errorName")]
    public string? ErrorName { get; set; }
}
