namespace MicroSharp;

/// <summary>
/// Unterscheidet Fehler beim Zugriff auf eine Mikrofonaufnahme.
/// </summary>
public enum MicrophoneErrorCode
{
    /// <summary>Der Zugriff ist aus einem anderen Grund fehlgeschlagen.</summary>
    Unavailable = 0,

    /// <summary>Die Berechtigung wurde verweigert.</summary>
    PermissionDenied = 1,

    /// <summary>Es wurde kein Aufnahmegeraet gefunden.</summary>
    DeviceNotFound = 2,

    /// <summary>Das angeforderte Format wird nicht geliefert.</summary>
    UnsupportedFormat = 3,

    /// <summary>Die angeforderte Staustrategie wird von diesem Adapter nicht ausgefuehrt.</summary>
    UnsupportedBackpressure = 4,

    /// <summary>Der Aufruf wurde abgebrochen.</summary>
    Cancelled = 5,

    /// <summary>Ein Audioblock passt nicht zum vereinbarten Format.</summary>
    InvalidChunk = 6,
}
