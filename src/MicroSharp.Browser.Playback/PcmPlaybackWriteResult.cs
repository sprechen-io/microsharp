namespace MicroSharp.Browser.Playback;

/// <summary>Reports admission of a PCM write in input frames.</summary>
/// <param name="AcceptedInputFrames">The number of input frames accepted from the start of the buffer.</param>
/// <param name="RejectedInputFrames">The number of input frames in the rejected suffix.</param>
public sealed record PcmPlaybackWriteResult(int AcceptedInputFrames, int RejectedInputFrames);
