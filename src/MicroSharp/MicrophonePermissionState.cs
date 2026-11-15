namespace MicroSharp;

/// <summary>
/// Beschreibt, ob eine Aufnahme erlaubt ist.
/// </summary>
public enum MicrophonePermissionState
{
    /// <summary>Der Adapter kann den Berechtigungsstand nicht feststellen.</summary>
    Unknown = 0,

    /// <summary>Der Benutzer wurde noch nicht gefragt oder die Entscheidung steht aus.</summary>
    Prompt = 1,

    /// <summary>Die Aufnahme ist erlaubt.</summary>
    Granted = 2,

    /// <summary>Die Aufnahme ist verweigert.</summary>
    Denied = 3,
}
