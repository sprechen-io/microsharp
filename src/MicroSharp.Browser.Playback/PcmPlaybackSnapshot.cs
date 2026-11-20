using System.Text.Json.Serialization;

namespace MicroSharp.Browser.Playback;

/// <summary>Playback counters since the current audio context started.</summary>
/// <param name="ContentOutputFrames">Output frames containing decoded or interpolated PCM.</param>
/// <param name="UnderrunOutputFrames">Output frames filled with silence because PCM was unavailable.</param>
/// <param name="RejectedInputFrames">Input frames rejected because of available FIFO capacity.</param>
public sealed record PcmPlaybackSnapshot(
    [property: JsonPropertyName("contentOutputFrames")] long ContentOutputFrames,
    [property: JsonPropertyName("underrunOutputFrames")] long UnderrunOutputFrames,
    [property: JsonPropertyName("rejectedInputFrames")] long RejectedInputFrames);
