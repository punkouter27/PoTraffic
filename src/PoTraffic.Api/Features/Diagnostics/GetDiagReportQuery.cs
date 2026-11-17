using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoTraffic.Shared.DTOs.Admin;

namespace PoTraffic.Api.Features.Diagnostics;

/// <summary>Builds the <c>GET /api/diag</c> report.</summary>
/// <param name="IncludeDetail">True for administrators: adds each check's raw description.</param>
public sealed record GetDiagReportQuery(bool IncludeDetail);

/// <summary>
/// Turns the registered health checks into the friendly <c>/diag</c> report. Everything a
/// signed-out caller sees is fixed text from <see cref="Describe"/> plus status and timing —
/// the health-check descriptions (which can name config keys or accounts) are passed through
/// only for administrators.
/// </summary>
public sealed class GetDiagReportHandler(HealthCheckService healthChecks, IHostEnvironment env)
{
    public async Task<DiagReportDto> Handle(GetDiagReportQuery query, CancellationToken ct)
    {
        HealthReport report = await healthChecks.CheckHealthAsync(ct);

        List<DiagCheckDto> checks = [.. report.Entries.Select(e =>
        {
            (string name, string category, string purpose) = Describe(e.Key);
            return new DiagCheckDto(
                Key: e.Key,
                Name: name,
                Category: category,
                Status: e.Value.Status.ToString(),
                Purpose: purpose,
                LatencyMs: Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                Detail: query.IncludeDetail ? e.Value.Description : null);
        })];

        return new DiagReportDto(report.Status.ToString(), env.EnvironmentName, DateTimeOffset.UtcNow, checks);
    }

    internal static (string Name, string Category, string Purpose) Describe(string key) => key switch
    {
        "storage" => ("Table Storage", "storage", "Keeps your commutes, schedules and every recorded trip."),
        "keyvault" => ("Key Vault", "service", "Holds the secrets that Microsoft sign-in needs."),
        "trafficProvider" => ("Google Maps traffic", "service", "Measures the travel time of each commute."),
        "scheduler" => ("Scheduler", "service", "Runs the timed traffic checks for monitored commutes."),
        _ => (key, "service", "Another dependency this app checks."),
    };
}
