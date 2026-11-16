using Microsoft.Extensions.DependencyInjection;

namespace MicroSharp.Native;

/// <summary>
/// Optional registration. Callers may also construct <see cref="OpenAlMicrophoneCapture"/> themselves.
/// </summary>
public static class OpenAlMicrophoneServiceCollectionExtensions
{
    /// <summary>Registers the OpenAL capture as a singleton.</summary>
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
