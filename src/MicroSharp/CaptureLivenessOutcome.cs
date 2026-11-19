namespace MicroSharp;

/// <summary>
/// The terminal result of observing one capture.
/// </summary>
public enum CaptureLivenessOutcome
{
    /// <summary>
    /// Observation ended before the current delivery deadline.
    /// </summary>
    Completed = 0,

    /// <summary>
    /// The first block did not arrive before the startup deadline.
    /// </summary>
    StartupTimeout = 1,

    /// <summary>
    /// A subsequent block did not arrive before the inter-block deadline.
    /// </summary>
    InterBlockTimeout = 2,
}
