using Microsoft.Extensions.DependencyInjection;

namespace MicroSharp.Native;

/// <summary>
/// Optionale Registrierung. Aufrufer koennen <see cref="OpenAlMicrophoneCapture"/> auch selbst erzeugen.
/// </summary>
public static class OpenAlMicrophoneServiceCollectionExtensions
{
    /// <summary>Registriert die OpenAL-Aufnahme als Singleton.</summary>
    public static IServiceCollection AddMicroSharpOpenAlMicrophone(
        this IServiceCollection services,
        OpenAlMicrophoneOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton<IMicrophoneCapture>(new OpenAlMicrophoneCapture(options));
        return services;
    }
}
