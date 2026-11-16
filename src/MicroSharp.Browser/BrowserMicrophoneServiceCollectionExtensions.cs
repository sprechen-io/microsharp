using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace MicroSharp.Browser;

/// <summary>
/// Optional registration. Callers may also construct <see cref="BrowserMicrophoneCapture"/> themselves.
/// </summary>
public static class BrowserMicrophoneServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BrowserMicrophoneCapture"/> as a scoped <see cref="IMicrophoneCapture"/>.
    /// The host must already have registered <see cref="IJSRuntime"/>, as Blazor does.
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
