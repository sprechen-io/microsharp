namespace MicroSharp;

/// <summary>
/// Was ein Adapter liefern kann, ohne dafuer ein Geraet zu oeffnen.
/// </summary>
/// <param name="AdapterName">Stabiler Name des Adapters.</param>
/// <param name="CapturesOperatingSystemDevice">Wahr nur, wenn der Adapter ein Betriebssystem-Geraet oeffnet.</param>
/// <param name="RequiresBrowserHost">Wahr, wenn die Aufnahme Browser-APIs im Dokument des Benutzers braucht.</param>
/// <param name="SampleRatesHz">Unterstuetzte Abtastraten.</param>
public sealed record MicrophoneCapabilities(
    string AdapterName,
    bool CapturesOperatingSystemDevice,
    bool RequiresBrowserHost,
    IReadOnlyList<int> SampleRatesHz);
