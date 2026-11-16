namespace MicroSharp.Browser;

/// <summary>
/// Ergebnis einer Vorbereitung. <see cref="Hold"/> ist nur gesetzt, wenn der Browser eine Quelle behalten hat.
/// </summary>
public sealed class BrowserPrepareOutcome
{
    /// <summary>Erstellt das Ergebnis.</summary>
    public BrowserPrepareOutcome(BrowserAcquireResult result, BrowserMicrophoneHold? hold)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Hold = hold;
    }

    /// <summary>Rohantwort des Browsers, einschliesslich eines genannten Fehlers.</summary>
    public BrowserAcquireResult Result { get; }

    /// <summary>Gehaltene Quelle oder <see langword="null"/>, wenn der Browser keine Quelle behalten hat.</summary>
    public BrowserMicrophoneHold? Hold { get; }
}
