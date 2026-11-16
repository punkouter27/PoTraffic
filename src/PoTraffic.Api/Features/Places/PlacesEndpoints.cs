using System.Diagnostics.CodeAnalysis;
using PoTraffic.Api.Infrastructure.Resilience;
using PoTraffic.Api.Infrastructure.Security;

namespace PoTraffic.Api.Features.Places;

/// <summary>Address suggestions while typing — the BFF side of the route form's autocomplete.</summary>
public static class PlacesEndpoints
{
    internal static IServiceCollection AddPlacesServices(this IServiceCollection services)
    {
        services.AddHttpClient<GooglePlacesService>()
            .AddResilienceHandler(ResiliencePipelineExtensions.TrafficPipeline);
        return services;
    }

    public static IEndpointRouteBuilder MapPlacesEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/places/autocomplete?q=<partial address> — BFF-proxied suggestions (#9)
        app.MapGet("/api/places/autocomplete", async (
                string? q, GooglePlacesService places, CancellationToken ct) =>
                Results.Ok(await places.AutocompleteAsync(q, ct)))
            .RequireAuthorization("ProductionMicrosoftAuth")
            .RequireRateLimiting(RateLimitingExtensions.Autocomplete)
            .WithTags("Places");
        return app;
    }
}
