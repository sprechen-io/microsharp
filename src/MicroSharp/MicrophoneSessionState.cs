namespace MicroSharp;

/// <summary>
/// Lebenszyklus einer laufenden Aufnahme.
/// </summary>
public enum MicrophoneSessionState
{
    /// <summary>Die Sitzung nimmt auf oder haelt bereits gelesene Bloecke bereit.</summary>
    Running = 0,

    /// <summary>Die Sitzung wurde ohne Fehler abgeschlossen.</summary>
    Stopped = 1,

    /// <summary>Die Sitzung wurde mit einem Fehler abgeschlossen.</summary>
    Faulted = 2,

    /// <summary>Die Sitzung wurde freigegeben.</summary>
    Disposed = 3,
}
