namespace MicroSharp.Processing;

/// <summary>
/// Duration and mean absolute amplitude of one PCM16LE block.
/// The amplitude is the integer mean of absolute sample values, not an RMS and not a peak.
/// </summary>
/// <param name="DurationMilliseconds">Duration of the whole samples in the block.</param>
/// <param name="MeanAbsoluteAmplitude">Integer mean of the absolute sample values.</param>
public readonly record struct PcmBlockAnalysis(double DurationMilliseconds, int MeanAbsoluteAmplitude);
