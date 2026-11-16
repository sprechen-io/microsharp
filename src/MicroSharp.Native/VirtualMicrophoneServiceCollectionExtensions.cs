using Microsoft.Extensions.DependencyInjection;

namespace MicroSharp.Native;

/// <summary>
/// Optional registration. Callers may also construct <see cref="VirtualMicrophoneCapture"/> themselves.
/// </summary>
public static class VirtualMicrophoneServiceCollectionExtensions
{
    /// <summary>
    /// Registers one virtual capture as a singleton.
    /// The session does not release the PCM source.
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
