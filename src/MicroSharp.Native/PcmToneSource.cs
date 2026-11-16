namespace MicroSharp.Native;

/// <summary>
/// Generates a sine tone as signed 16-bit samples.
/// </summary>
public sealed class PcmToneSource : IPcmSource
{
    private readonly double _frequencyHz;
    private readonly double _amplitude;
    private double _phase;

    /// <summary>Creates a non-ending tone source in the given format.</summary>
    public PcmToneSource(double frequencyHz, PcmFormat format, double amplitude = 0.5)
    {
        if (frequencyHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        }

        if (amplitude is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude));
        }

        Format = format ?? throw new ArgumentNullException(nameof(format));
        _frequencyHz = frequencyHz;
        _amplitude = amplitude;
    }

    /// <inheritdoc />
    public PcmFormat Format { get; }

    /// <inheritdoc />
    public bool TryReadChunk(Span<short> destination)
    {
        if (destination.Length != Format.SamplesPerChunk)
        {
            throw new ArgumentException("The destination must hold exactly one chunk.", nameof(destination));
        }

        var step = 2d * Math.PI * _frequencyHz / Format.SampleRateHz;
        for (var index = 0; index < destination.Length; index++)
        {
            var value = Math.Sin(_phase) * _amplitude;
            var scaled = value < 0 ? value * 32768d : value * 32767d;
            destination[index] = (short)scaled;
            _phase += step;
            if (_phase > Math.PI * 2d)
            {
                _phase -= Math.PI * 2d;
            }
        }

        return true;
    }
}
