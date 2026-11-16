namespace MicroSharp;

/// <summary>
/// Carries a classified microphone-capture error.
/// </summary>
public sealed class MicrophoneException : InvalidOperationException
{
    /// <summary>
    /// Creates the exception from an error code and an optional detail.
    /// </summary>
    public MicrophoneException(MicrophoneErrorCode code, string? detail = null, Exception? innerException = null)
        : base(CreateMessage(code, detail), innerException)
    {
        Code = code;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
    }

    /// <summary>The classified error.</summary>
    public MicrophoneErrorCode Code { get; }

    /// <summary>Additional description of the source, when present.</summary>
    public string? Detail { get; }

    private static string CreateMessage(MicrophoneErrorCode code, string? detail)
    {
        var message = code switch
        {
            MicrophoneErrorCode.PermissionDenied => "Microphone access is not permitted.",
            MicrophoneErrorCode.DeviceNotFound => "The microphone device is unavailable.",
            MicrophoneErrorCode.UnsupportedFormat => "The requested microphone format is not supported.",
            MicrophoneErrorCode.UnsupportedBackpressure => "The requested backpressure is not supported by this adapter.",
            MicrophoneErrorCode.Cancelled => "Microphone capture was cancelled.",
            MicrophoneErrorCode.InvalidChunk => "A microphone chunk did not match the negotiated format.",
            _ => "Microphone capture failed.",
        };

        return string.IsNullOrWhiteSpace(detail) ? message : message + " " + detail.Trim();
    }
}
