namespace MicroSharp.Processing;

/// <summary>Where a new history cursor starts.</summary>
public enum PcmHistoryCursorOrigin
{
    /// <summary>The next byte that has not been written yet.</summary>
    Latest,

    /// <summary>The oldest byte still retained in the ring.</summary>
    Earliest,
}
