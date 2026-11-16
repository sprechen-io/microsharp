namespace MicroSharp.ReleaseGate;

/// <summary>
/// The commit must not request a publishing token.
/// </summary>
public sealed class ReleaseGateException : Exception
{
    public ReleaseGateException(string message)
        : base(message)
    {
    }
}
