namespace MicroSharp.ReleaseGate;

/// <summary>
/// Der Commit darf keinen Publishing-Token anfordern.
/// </summary>
public sealed class ReleaseGateException : Exception
{
    public ReleaseGateException(string message)
        : base(message)
    {
    }
}
