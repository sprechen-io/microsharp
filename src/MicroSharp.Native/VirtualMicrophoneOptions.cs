namespace MicroSharp.Native;

/// <summary>
/// Configuration of the virtual capture. It does not open an operating-system device.
/// </summary>
public sealed class VirtualMicrophoneOptions
{
    /// <summary>PCM source. The session does not release it.</summary>
    public required IPcmSource Source { get; init; }

    /// <summary>Configured permission. Only <see cref="MicrophonePermissionState.Granted"/> starts a capture.</summary>
    public MicrophonePermissionState Permission { get; init; } = MicrophonePermissionState.Granted;

    /// <summary>Reports a missing device without reading the source.</summary>
    public bool DeviceMissing { get; init; }

    /// <summary>Waits the chunk duration between chunks.</summary>
    public bool PaceInRealTime { get; init; }

    /// <summary>Clock for the real-time pause. When omitted, <see cref="TimeProvider.System"/> is used.</summary>
    public TimeProvider? TimeProvider { get; init; }
}
