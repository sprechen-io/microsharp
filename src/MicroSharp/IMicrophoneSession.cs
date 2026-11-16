namespace MicroSharp;

/// <summary>
/// Eine begonnene Aufnahme. DisposeAsync gibt sie deterministisch frei und darf mehrfach aufgerufen werden.
/// </summary>
public interface IMicrophoneSession : IAsyncDisposable
{
    /// <summary>Das vereinbarte Format.</summary>
    PcmFormat Format { get; }

    /// <summary>Der Lebenszyklus.</summary>
    MicrophoneSessionState State { get; }

    /// <summary>Bloecke, die wegen eines vollen Puffers verworfen wurden.</summary>
    long DroppedChunkCount { get; }

    /// <summary>
    /// Liest Bloecke in Einreihenfolge. Der Abbruch beendet nur das Lesen und haelt die Aufnahme nicht an.
    /// Ein abgebrochener Token nimmt den naechsten Block nicht aus der Warteschlange.
    /// Noch nicht ausgegebene Bloecke bleiben in der Sitzung.
    /// Nach einem fehlerfreien Stopp endet die Folge. Nach einem Fehler wird die gespeicherte Ausnahme geworfen,
    /// sobald bereits eingereihte Bloecke gelesen wurden.
    /// </summary>
    IAsyncEnumerable<PcmChunk> ReadChunksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Beendet die Aufnahme ohne Fehler. Bereits eingereihte Bloecke bleiben lesbar.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
