namespace MicroSharp;

/// <summary>
/// Starts a microphone capture. Applications construct the concrete adapter themselves or optionally
/// register it through the extension methods of the adapter packages.
/// </summary>
public interface IMicrophoneCapture
{
    /// <summary>Stable name of the adapter.</summary>
    string AdapterName { get; }

    /// <summary>True only when this adapter opens an operating-system capture device.</summary>
    bool CapturesOperatingSystemDevice { get; }

    /// <summary>True when capture needs a browser document with media access.</summary>
    bool RequiresBrowserHost { get; }

    /// <summary>Reads the permission state without starting a capture.</summary>
    Task<MicrophonePermission> GetPermissionAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports capabilities without starting a capture.</summary>
    Task<MicrophoneCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests permission and releases a device that was opened for that request.
    /// A denied permission or a missing device is reported as <see cref="MicrophoneException"/>.
    /// </summary>
    Task RequestAccessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts capture. If the token is cancelled before the method returns, every source that was
    /// already touched is released and <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    Task<IMicrophoneSession> StartAsync(MicrophoneCaptureRequest request, CancellationToken cancellationToken = default);
}
