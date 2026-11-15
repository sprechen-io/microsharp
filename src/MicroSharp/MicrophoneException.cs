namespace MicroSharp;

/// <summary>
/// Transportiert einen klassifizierten Fehler einer Mikrofonaufnahme.
/// </summary>
public sealed class MicrophoneException : InvalidOperationException
{
    /// <summary>
    /// Erstellt die Ausnahme aus einem Fehlercode und einem optionalen Detail.
    /// </summary>
    public MicrophoneException(MicrophoneErrorCode code, string? detail = null, Exception? innerException = null)
        : base(CreateMessage(code, detail), innerException)
    {
        Code = code;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
    }

    /// <summary>Der klassifizierte Fehler.</summary>
    public MicrophoneErrorCode Code { get; }

    /// <summary>Zusaetzliche Angabe der Quelle, sofern vorhanden.</summary>
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
