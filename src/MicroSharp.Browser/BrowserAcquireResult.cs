using System.Text.Json.Serialization;

namespace MicroSharp.Browser;

/// <summary>
/// Result of <c>getUserMedia</c> and of creating the browser session.
/// </summary>
public sealed class BrowserAcquireResult
{
    /// <summary>Session id, or empty when the browser refuses.</summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>Name of the browser exception when access fails.</summary>
    [JsonPropertyName("errorName")]
    public string? ErrorName { get; set; }
}
