namespace MicroSharp.Browser;

/// <summary>
/// A classified failure while acquiring or releasing a browser source.
/// </summary>
public sealed class BrowserSourceOwnershipException : InvalidOperationException
{
    /// <summary>Creates the failure from a kind.</summary>
    public BrowserSourceOwnershipException(BrowserSourceFailureKind kind)
        : base(CreateMessage(kind))
    {
        Kind = kind;
    }

    /// <summary>Creates the failure from a kind and the original cause.</summary>
    public BrowserSourceOwnershipException(BrowserSourceFailureKind kind, Exception innerException)
        : base(CreateMessage(kind), innerException)
    {
        Kind = kind;
    }

    /// <summary>The classified failure.</summary>
    public BrowserSourceFailureKind Kind { get; }

    private static string CreateMessage(BrowserSourceFailureKind kind)
        => kind switch
        {
            BrowserSourceFailureKind.DeviceNotFound => "The microphone device is unavailable.",
            BrowserSourceFailureKind.PermissionDenied => "Microphone access is not permitted.",
            _ => "Microphone acquisition failed.",
        };
}
