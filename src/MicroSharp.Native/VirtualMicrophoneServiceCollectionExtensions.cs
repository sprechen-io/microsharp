using Microsoft.Extensions.DependencyInjection;

namespace MicroSharp.Native;

/// <summary>
/// Optionale Registrierung. Aufrufer koennen <see cref="VirtualMicrophoneCapture"/> auch selbst erzeugen.
/// </summary>
public static class VirtualMicrophoneServiceCollectionExtensions
{
    /// <summary>
    /// Registriert eine einzelne virtuelle Aufnahme als Singleton.
    /// Die PCM-Quelle wird von der Sitzung nicht freigegeben.
    /// </summary>
    public static IServiceCollection AddMicroSharpVirtualMicrophone(
        this IServiceCollection services,
        VirtualMicrophoneOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton<IMicrophoneCapture>(new VirtualMicrophoneCapture(options));
        return services;
    }
}
