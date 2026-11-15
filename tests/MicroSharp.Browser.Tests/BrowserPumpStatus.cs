using System.Text.Json.Serialization;

namespace MicroSharp.Browser.Tests;

internal sealed class BrowserPumpStatus
{
    [JsonPropertyName("chunks")]
    public int[][]? Chunks { get; set; }

    [JsonPropertyName("fault")]
    public string? Fault { get; set; }
}
