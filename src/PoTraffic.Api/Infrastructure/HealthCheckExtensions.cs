using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoTraffic.Api.Features.Diagnostics;
using PoTraffic.Api.Infrastructure.Storage;
using System.Diagnostics.CodeAnalysis;

namespace PoTraffic.Api.Infrastructure;

internal static class HealthCheckExtensions
{
    /// <summary>Longest any one health check may run; past it the check is reported Unhealthy.</summary>
    internal static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registers the app's health checks (storage reachability and scheduler liveness, both
    /// free) and the live <see cref="ServiceProbes"/> behind <c>/api/diag</c>: every external
    /// service is called over the network there, with a short timeout and a 60 s cache.
    /// </summary>
    internal static IServiceCollection AddPoTrafficHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<StorageHealthCheck>("storage", timeout: CheckTimeout)
            .AddCheck<SchedulerHealthCheck>("scheduler", timeout: CheckTimeout);

        // One report, cached for CachedHealthReport.CacheFor (60 s), behind /health, /health/json
        // and /api/diag alike.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CachedHealthReport>();

        services.AddHttpClient(ServiceProbes.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(8));
        services.AddSingleton<ServiceProbes>();

        return services;
    }

    /// <summary>
    /// Maps the anonymous health endpoints (the /diag contract in AGENTS.md). /health and
    /// /health/json read the shared <see cref="CachedHealthReport"/> (60 s), like /api/diag:
    /// <list type="bullet">
    /// <item><c>/health</c> — readiness: 200 while every dependency is Healthy or Degraded, 503
    /// when one is Unhealthy. Plain text, no detail.</item>
    /// <item><c>/health/ready</c> — 503 while <see cref="TableStorageContext.HydrateAsync"/> is in
    /// flight, 200 once hydrated. Lets App Service gate traffic without depending on hydration
    /// during host bind.</item>
    /// <item><c>/health/json</c> — per-dependency JSON status for DB and external APIs. This is
    /// the machine-readable feed used by post-deploy smoke and triage scripts.</item>
    /// </list>
    /// <c>/health/live</c> (liveness, with the <c>X-Commit</c> header) is PoPlatform.MapPoLiveness;
    /// the friendly report is <c>GET /api/diag</c> (Features/Diagnostics), shown by the <c>/diag</c> page.
    /// </summary>
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    internal static WebApplication MapPoTrafficHealthChecks(this WebApplication app)
    {
        app.MapGet("/health", async (HttpContext http, CachedHealthReport health) =>
            {
                HealthReport report = (await health.GetAsync(http.RequestAborted)).Report;
                http.Response.Headers.CacheControl = "no-store";
                return Results.Text(report.Status.ToString(), "text/plain",
                    statusCode: report.Status == HealthStatus.Unhealthy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
            })
            .AllowAnonymous();

        app.MapGet("/health/ready", (TableStorageContext ctx) =>
            ctx.IsHydrated
                ? Results.Ok(new { status = "ready", durable = ctx.IsDurable })
                : Results.Json(new { status = "hydrating", durable = ctx.IsDurable }, statusCode: 503))
            .AllowAnonymous();

        app.MapGet("/health/json", async (HttpContext http, CachedHealthReport health) =>
            {
                HealthReport report = (await health.GetAsync(http.RequestAborted)).Report;
                http.Response.Headers.CacheControl = "no-store";
                return Results.Json(new
                {
                    status = report.Status.ToString(),
                    entries = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        durationMs = e.Value.Duration.TotalMilliseconds,
                    }),
                }, statusCode: report.Status == HealthStatus.Unhealthy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
            })
            .AllowAnonymous();

        return app;
    }
}
