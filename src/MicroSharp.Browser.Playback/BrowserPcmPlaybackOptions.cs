namespace MicroSharp.Browser.Playback;

/// <summary>Configures a browser PCM player.</summary>
/// <param name="InputSampleRate">The fixed mono PCM input rate in Hz, from 8,000 through 192,000.</param>
/// <param name="CapacityFrames">The maximum outstanding PCM input frames, from 2 through 192,000.</param>
public sealed record BrowserPcmPlaybackOptions(int InputSampleRate, int CapacityFrames);
