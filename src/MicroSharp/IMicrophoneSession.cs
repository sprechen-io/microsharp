namespace MicroSharp;

/// <summary>
/// A capture that has started. DisposeAsync releases it deterministically and may be called more than once.
/// </summary>
public interface IMicrophoneSession : IAsyncDisposable
{
    /// <summary>The agreed format.</summary>
    PcmFormat Format { get; }

    /// <summary>The lifecycle.</summary>
    MicrophoneSessionState State { get; }

    /// <summary>Chunks discarded because the buffer was full.</summary>
    long DroppedChunkCount { get; }

    /// <summary>
    /// Reads chunks in enqueue order. Cancellation ends only the read and does not stop capture.
    /// A cancelled token does not take the next chunk out of the queue.
    /// Chunks that have not yet been delivered remain in the session.
    /// After a clean stop the sequence ends. After a fault the stored exception is thrown
    /// once chunks that were already enqueued have been read.
    /// </summary>
    IAsyncEnumerable<PcmChunk> ReadChunksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops capture without an error. Chunks that were already enqueued remain readable.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
