using System.Text.Json.Serialization;

namespace MicroSharp.Browser;

/// <summary>
/// PCM format confirmed by the browser after the audio worklet is connected.
/// </summary>
public sealed class BrowserStartResult
{
    /// <summary>Confirmed sample rate.</summary>
    [JsonPropertyName("sampleRateHz")]
    public int SampleRateHz { get; set; }

    /// <summary>Confirmed channel count.</summary>
    [JsonPropertyName("channels")]
    public int Channels { get; set; }

    /// <summary>Confirmed chunk duration in milliseconds.</summary>
    [JsonPropertyName("chunkMilliseconds")]
    public int ChunkMilliseconds { get; set; }
}
