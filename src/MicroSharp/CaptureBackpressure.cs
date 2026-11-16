namespace MicroSharp;

/// <summary>
/// Sets what happens when the capturer delivers faster than the caller reads.
/// </summary>
public enum CaptureBackpressure
{
    /// <summary>
    /// The capturer waits until the buffer has room. A live source cannot be paused for that wait.
    /// </summary>
    Wait = 0,

    /// <summary>
    /// The oldest unread chunk is discarded.
    /// </summary>
    DropOldest = 1,
}
