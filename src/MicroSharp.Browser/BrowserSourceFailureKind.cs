namespace MicroSharp.Browser;

/// <summary>
/// Classifies a failure while acquiring or owning a browser source.
/// </summary>
public enum BrowserSourceFailureKind
{
    /// <summary>The source could not be acquired or kept.</summary>
    Unavailable = 0,

    /// <summary>The browser reported that no matching device exists.</summary>
    DeviceNotFound = 1,

    /// <summary>The browser refused microphone permission.</summary>
    PermissionDenied = 2,
}
