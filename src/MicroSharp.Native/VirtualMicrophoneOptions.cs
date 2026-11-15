namespace MicroSharp.Native;

/// <summary>
/// Konfiguration der virtuellen Aufnahme. Sie oeffnet kein Betriebssystem-Geraet.
/// </summary>
public sealed class VirtualMicrophoneOptions
{
    /// <summary>PCM-Quelle. Die Sitzung gibt sie nicht frei.</summary>
    public required IPcmSource Source { get; init; }

    /// <summary>Konfigurierte Berechtigung. Nur <see cref="MicrophonePermissionState.Granted"/> startet eine Aufnahme.</summary>
    public MicrophonePermissionState Permission { get; init; } = MicrophonePermissionState.Granted;

    /// <summary>Meldet ein fehlendes Geraet, ohne die Quelle zu lesen.</summary>
    public bool DeviceMissing { get; init; }

    /// <summary>Wartet zwischen den Bloecken die Blockdauer ab.</summary>
    public bool PaceInRealTime { get; init; }

    /// <summary>Uhr fuer die Echtzeit-Pause. Ohne Angabe wird <see cref="TimeProvider.System"/> verwendet.</summary>
    public TimeProvider? TimeProvider { get; init; }
}
