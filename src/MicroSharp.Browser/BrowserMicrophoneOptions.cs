namespace MicroSharp.Browser;

/// <summary>
/// Einstellungen des Browser-Adapters. Sie erzwingen keinen DI-Container.
/// </summary>
public sealed class BrowserMicrophoneOptions
{
    /// <summary>Pfad des ES-Moduls in den statischen Web-Assets.</summary>
    public const string DefaultModulePath = "./_content/MicroSharp.Browser/microsharp-capture.js";

    /// <summary>Modulpfad fuer <c>import</c>.</summary>
    public string ModulePath { get; init; } = DefaultModulePath;

    /// <summary>Wunsch an den Browser. <see langword="null"/> laesst die Browser-Vorgabe.</summary>
    public bool? EchoCancellation { get; init; } = true;

    /// <summary>Wunsch an den Browser. <see langword="null"/> laesst die Browser-Vorgabe.</summary>
    public bool? NoiseSuppression { get; init; } = true;

    /// <summary>Wunsch an den Browser. <see langword="null"/> laesst die Browser-Vorgabe.</summary>
    public bool? AutoGainControl { get; init; } = true;

    /// <summary>
    /// Haelt den Audiokontext mit einem unhoerbaren Signal offen.
    /// Der Vorgabewert ist aus, damit eine Aufnahme ohne diesen Wunsch keinen zusaetzlichen Klang erzeugt.
    /// </summary>
    public bool KeepAudioContextAlive { get; init; }
}
