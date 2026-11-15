namespace MicroSharp.Browser;

internal static class BrowserFailureMapper
{
    public static MicrophoneException ToException(string? errorName)
    {
        var code = errorName switch
        {
            "NotFoundError" or "DevicesNotFoundError" or "OverconstrainedError"
                => MicrophoneErrorCode.DeviceNotFound,
            "NotAllowedError" or "PermissionDeniedError" or "SecurityError"
                => MicrophoneErrorCode.PermissionDenied,
            _ => MicrophoneErrorCode.Unavailable,
        };

        return new MicrophoneException(code, errorName);
    }
}
