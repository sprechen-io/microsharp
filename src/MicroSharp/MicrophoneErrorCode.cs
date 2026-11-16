namespace MicroSharp;

/// <summary>
/// Distinguishes errors while accessing a microphone capture.
/// </summary>
public enum MicrophoneErrorCode
{
    /// <summary>Access failed for another reason.</summary>
    Unavailable = 0,

    /// <summary>Permission was denied.</summary>
    PermissionDenied = 1,

    /// <summary>No capture device was found.</summary>
    DeviceNotFound = 2,

    /// <summary>The requested format is not delivered.</summary>
    UnsupportedFormat = 3,

    /// <summary>This adapter does not carry out the requested backpressure strategy.</summary>
    UnsupportedBackpressure = 4,

    /// <summary>The call was cancelled.</summary>
    Cancelled = 5,

    /// <summary>An audio chunk does not match the agreed format.</summary>
    InvalidChunk = 6,
}
