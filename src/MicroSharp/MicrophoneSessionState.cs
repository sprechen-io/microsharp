namespace MicroSharp;

/// <summary>
/// Lifecycle of a running capture.
/// </summary>
public enum MicrophoneSessionState
{
    /// <summary>The session is capturing or still has chunks available that were already read from the source.</summary>
    Running = 0,

    /// <summary>The session finished without an error.</summary>
    Stopped = 1,

    /// <summary>The session finished with an error.</summary>
    Faulted = 2,

    /// <summary>The session was released.</summary>
    Disposed = 3,
}
