namespace MicroSharp;

/// <summary>
/// Describes whether capture is allowed.
/// </summary>
public enum MicrophonePermissionState
{
    /// <summary>The adapter cannot determine the permission state.</summary>
    Unknown = 0,

    /// <summary>The user has not been asked yet, or the decision is still pending.</summary>
    Prompt = 1,

    /// <summary>Capture is allowed.</summary>
    Granted = 2,

    /// <summary>Capture is denied.</summary>
    Denied = 3,
}
