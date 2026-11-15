namespace MicroSharp;

/// <summary>
/// Prueft Formate, die alle MicroSharp-Adapter liefern.
/// </summary>
public static class MicrophoneFormats
{
    private static readonly List<int> SupportedRates = [with(capacity: 4), 8_000, 16_000, 24_000, 48_000];

    /// <summary>Unterstuetzte Abtastraten in Hertz.</summary>
    public static IReadOnlyList<int> SupportedSampleRatesHz => SupportedRates;

    /// <summary>Lehnt Formate ab, die kein Adapter liefert.</summary>
    public static void EnsureSupported(PcmFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (!SupportedSampleRatesHz.Contains(format.SampleRateHz))
        {
            throw new MicrophoneException(
                MicrophoneErrorCode.UnsupportedFormat,
                $"Sample rate {format.SampleRateHz} Hz is not supported.");
        }
    }

    /// <summary>Prueft Format und Puffergroesse einer Aufnahmeanforderung.</summary>
    public static void EnsureSupported(MicrophoneCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Format);
        if (request.QueueCapacityChunks < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.QueueCapacityChunks,
                "Queue capacity must be at least one chunk.");
        }

        EnsureSupported(request.Format);
    }
}
