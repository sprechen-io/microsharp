namespace MicroSharp;

/// <summary>
/// What an adapter can deliver without opening a device for that query.
/// </summary>
/// <param name="AdapterName">Stable name of the adapter.</param>
/// <param name="CapturesOperatingSystemDevice">True only when the adapter opens an operating-system device.</param>
/// <param name="RequiresBrowserHost">True when capture needs browser APIs in the user's document.</param>
/// <param name="SampleRatesHz">Supported sample rates.</param>
public sealed record MicrophoneCapabilities(
    string AdapterName,
    bool CapturesOperatingSystemDevice,
    bool RequiresBrowserHost,
    IReadOnlyList<int> SampleRatesHz);
