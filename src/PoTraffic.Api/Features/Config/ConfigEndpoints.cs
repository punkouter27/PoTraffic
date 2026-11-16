using System.Diagnostics.CodeAnalysis;

namespace PoTraffic.Api.Features.Config;

/// <summary>
/// The public, read-only face of the Config slice: the feature flags the Blazor client needs
/// before sign-in. No authentication required — flags are not sensitive. Changing them is an
/// administrator operation (<c>PUT /api/admin/configuration/{key}</c>, Admin slice).
/// </summary>
public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        // §10 — expose UseMockProviders so the client can display the "USING MOCK DATA" banner.
        // Reads the same FeatureFlags provider resolution acts on, so the banner can't disagree
        // with which providers are actually answering.
        app.MapGet("/api/system/features", (FeatureFlags flags) =>
            Results.Ok(new FeatureFlagsResponse(flags.UseMockProviders, flags.EnableWeather)))
            .AllowAnonymous()
            .WithName("GetFeatureFlags")
            .WithTags("System");

        return app;
    }
}

/// <summary>Body of <c>GET /api/system/features</c>; typed so it appears in the OpenAPI document.</summary>
public sealed record FeatureFlagsResponse(bool UseMockProviders, bool EnableWeather);
