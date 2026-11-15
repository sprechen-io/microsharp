using System.Text.Json.Serialization;

namespace MicroSharp.Browser;

/// <summary>
/// Vom Browser gemeldeter Berechtigungsstand fuer das Mikrofon.
/// </summary>
public sealed class BrowserPermissionResult
{
    /// <summary><c>granted</c>, <c>denied</c>, <c>prompt</c> oder <c>unknown</c>.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }
}
