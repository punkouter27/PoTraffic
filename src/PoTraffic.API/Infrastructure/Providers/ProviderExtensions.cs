using PoTraffic.Api.Features.Config;
using PoTraffic.Api.Infrastructure.Resilience;
using PoTraffic.Api.Infrastructure.Storage;
using PoTraffic.Api.Infrastructure.Testing;

namespace PoTraffic.Api.Infrastructure.Providers;

public static class ProviderExtensions
{
    /// <summary>
    /// Registers the Google Maps traffic provider plus the weather and holiday feeds and their
    /// mocks. Which one a scope gets is decided per resolution by <see cref="FeatureFlags"/>, so
    /// the admin-editable <c>feature.mockproviders.enabled</c> setting takes effect on the next
    /// request. A host that forces mocks (Testing, or Features:UseMockProviders=true) registers
    /// no live client at all. Each live <see cref="HttpClient"/> is wired into its named
    /// resilience pipeline.
    /// </summary>
    public static IServiceCollection AddTrafficProviders(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        bool forced = FeatureFlags.IsMockForcedByHost(configuration, environment);
        services.AddSingleton(sp => new FeatureFlags(sp.GetRequiredService<TableStorageContext>(), forced));

        // Weather is registered unconditionally so ExecutePollCommand can take the
        // dependency without a null check — the weather flag decides whether it is consulted.
        if (forced)
        {
            // Integration/E2E isolation or local dev — production providers are never wired.
            services.AddScoped<ITrafficProvider, MockTrafficProvider>();
            services.AddScoped<IWeatherProvider, MockWeatherProvider>();
            services.AddSingleton<IHolidayCalendar>(NoHolidayCalendar.Instance);
        }
        else
        {
            services.AddScoped<MockTrafficProvider>();
            services.AddScoped<MockWeatherProvider>();
            services.AddHttpClient<GoogleMapsTrafficProvider>()
                .AddResilienceHandler(ResiliencePipelineExtensions.TrafficPipeline);
            services.AddHttpClient<OpenMeteoWeatherProvider>()
                .AddResilienceHandler(ResiliencePipelineExtensions.WeatherPipeline);
            services.AddHttpClient<NagerHolidayCalendar>()
                .AddResilienceHandler(ResiliencePipelineExtensions.WeatherPipeline);

            services.AddScoped<ITrafficProvider>(sp => sp.GetRequiredService<FeatureFlags>().UseMockProviders
                ? sp.GetRequiredService<MockTrafficProvider>()
                : sp.GetRequiredService<GoogleMapsTrafficProvider>());
            services.AddScoped<IWeatherProvider>(sp => sp.GetRequiredService<FeatureFlags>().UseMockProviders
                ? sp.GetRequiredService<MockWeatherProvider>()
                : sp.GetRequiredService<OpenMeteoWeatherProvider>());
            services.AddTransient<IHolidayCalendar>(sp => sp.GetRequiredService<FeatureFlags>().UseMockProviders
                ? NoHolidayCalendar.Instance
                : sp.GetRequiredService<NagerHolidayCalendar>());
        }

        // Incident explanations for alerts. A no-op without TomTom:ApiKey, so it is safe to
        // register unconditionally (mock mode included — no key, no calls).
        services.AddHttpClient<IIncidentProvider, TomTomIncidentProvider>()
            .AddResilienceHandler(ResiliencePipelineExtensions.TrafficPipeline);

        return services;
    }
}
