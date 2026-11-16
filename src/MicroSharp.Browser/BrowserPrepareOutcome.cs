namespace MicroSharp.Browser;

/// <summary>
/// Result of preparation. <see cref="Hold"/> is set only when the browser kept a source.
/// </summary>
public sealed class BrowserPrepareOutcome
{
    /// <summary>Creates the result.</summary>
    public BrowserPrepareOutcome(BrowserAcquireResult result, BrowserMicrophoneHold? hold)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Hold = hold;
    }

    /// <summary>Raw browser response, including a named error.</summary>
    public BrowserAcquireResult Result { get; }

    /// <summary>Held source, or <see langword="null"/> when the browser did not keep a source.</summary>
    public BrowserMicrophoneHold? Hold { get; }
}
