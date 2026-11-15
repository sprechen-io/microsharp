using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace MicroSharp.Browser;

/// <summary>
/// Optionale Registrierung. Aufrufer koennen <see cref="BrowserMicrophoneCapture"/> auch selbst erzeugen.
/// </summary>
public static class BrowserMicrophoneServiceCollectionExtensions
{
    /// <summary>
    /// Registriert <see cref="BrowserMicrophoneCapture"/> als scoped <see cref="IMicrophoneCapture"/>.
    /// Der Host muss <see cref="IJSRuntime"/> bereits registriert haben, wie Blazor das tut.
    /// </summary>
    public static IServiceCollection AddMicroSharpBrowser(
        this IServiceCollection services,
        BrowserMicrophoneOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IMicrophoneCapture>(provider => new BrowserMicrophoneCapture(
            provider.GetRequiredService<IJSRuntime>(),
            options));
        return services;
    }
}
