namespace MicroSharp;

/// <summary>
/// Result of a permission query without a running capture.
/// </summary>
/// <param name="State">The determined state.</param>
/// <param name="Detail">Optional description of the source.</param>
public sealed record MicrophonePermission(MicrophonePermissionState State, string? Detail);
