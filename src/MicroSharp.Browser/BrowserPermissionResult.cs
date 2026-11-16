using System.Text.Json.Serialization;

namespace MicroSharp.Browser;

/// <summary>
/// Permission state for the microphone as reported by the browser.
/// </summary>
public sealed class BrowserPermissionResult
{
    /// <summary><c>granted</c>, <c>denied</c>, <c>prompt</c> or <c>unknown</c>.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }
}
