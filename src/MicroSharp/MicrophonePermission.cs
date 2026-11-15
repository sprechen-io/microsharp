namespace MicroSharp;

/// <summary>
/// Ergebnis einer Berechtigungsabfrage ohne laufende Aufnahme.
/// </summary>
/// <param name="State">Der festgestellte Stand.</param>
/// <param name="Detail">Optionale Angabe der Quelle.</param>
public sealed record MicrophonePermission(MicrophonePermissionState State, string? Detail);
