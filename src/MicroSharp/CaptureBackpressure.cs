namespace MicroSharp;

/// <summary>
/// Legt fest, was geschieht, wenn der Aufnehmer schneller liefert, als der Aufrufer liest.
/// </summary>
public enum CaptureBackpressure
{
    /// <summary>
    /// Der Aufnehmer wartet, bis im Puffer Platz ist. Das kann eine live laufende Quelle nicht anhalten.
    /// </summary>
    Wait = 0,

    /// <summary>
    /// Der aelteste noch nicht gelesene Block wird verworfen.
    /// </summary>
    DropOldest = 1,
}
