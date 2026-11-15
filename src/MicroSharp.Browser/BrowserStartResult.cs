using System.Text.Json.Serialization;

namespace MicroSharp.Browser;

/// <summary>
/// Vom Browser bestaetigtes PCM-Format nach dem Verbinden des AudioWorklets.
/// </summary>
public sealed class BrowserStartResult
{
    /// <summary>Bestaetigte Abtastrate.</summary>
    [JsonPropertyName("sampleRateHz")]
    public int SampleRateHz { get; set; }

    /// <summary>Bestaetigte Kanalzahl.</summary>
    [JsonPropertyName("channels")]
    public int Channels { get; set; }

    /// <summary>Bestaetigte Blockdauer in Millisekunden.</summary>
    [JsonPropertyName("chunkMilliseconds")]
    public int ChunkMilliseconds { get; set; }
}
