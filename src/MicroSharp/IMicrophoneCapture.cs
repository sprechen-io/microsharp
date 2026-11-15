namespace MicroSharp;

/// <summary>
/// Startet eine Mikrofonaufnahme. Anwendungen erzeugen den konkreten Adapter selbst oder lassen ihn
/// optional durch die Erweiterungsmethoden der Adapterpakete registrieren.
/// </summary>
public interface IMicrophoneCapture
{
    /// <summary>Stabiler Name des Adapters.</summary>
    string AdapterName { get; }

    /// <summary>Wahr nur, wenn dieser Adapter ein Betriebssystem-Aufnahmegeraet oeffnet.</summary>
    bool CapturesOperatingSystemDevice { get; }

    /// <summary>Wahr, wenn die Aufnahme ein Browserdokument mit Medienzugriff braucht.</summary>
    bool RequiresBrowserHost { get; }

    /// <summary>Liest den Berechtigungsstand, ohne eine Aufnahme zu starten.</summary>
    Task<MicrophonePermission> GetPermissionAsync(CancellationToken cancellationToken = default);

    /// <summary>Meldet die Faehigkeiten, ohne eine Aufnahme zu starten.</summary>
    Task<MicrophoneCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fordert die Berechtigung an und gibt ein dabei geoeffnetes Geraet wieder frei.
    /// Eine verweigerte Berechtigung oder ein fehlendes Geraet wird als <see cref="MicrophoneException"/> gemeldet.
    /// </summary>
    Task RequestAccessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Startet die Aufnahme. Wird der Token vor der Rueckgabe abgebrochen, wird jede schon angefasste Quelle freigegeben
    /// und <see cref="OperationCanceledException"/> geworfen.
    /// </summary>
    Task<IMicrophoneSession> StartAsync(MicrophoneCaptureRequest request, CancellationToken cancellationToken = default);
}
