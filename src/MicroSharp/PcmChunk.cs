namespace MicroSharp;

/// <summary>
/// Ein vollstaendiger PCM-Block. <paramref name="Payload"/> gehoert dem Empfaenger.
/// </summary>
/// <param name="Sequence">Laufende Nummer ab 0 in der Reihenfolge des Einreihens.</param>
/// <param name="CapturedAt">Zeit seit Beginn der Sitzung.</param>
/// <param name="Payload">Little-endian, vorzeichenbehaftete 16-Bit-Abtastwerte.</param>
public sealed record PcmChunk(long Sequence, TimeSpan CapturedAt, byte[] Payload);
