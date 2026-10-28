using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoTraffic.Shared.DTOs.Admin;

namespace PoTraffic.API.Infrastructure;

/// <summary>
/// Builds the per-dependency connection status list served at <c>/health/connections</c>
/// and rendered by the <c>/health</c> Blazor page.
///
/// <para>
/// Statuses only — never a secret value. The registered checks already cover the scheduler's
/// last tick ("scheduler") and the Google Maps key ("trafficProvider"), and both know when
/// mock providers make them moot. This list used to append its own copy of each, which
/// disagreed with them: a Testing host showed the scheduler both Healthy and Unhealthy, and
/// a missing key as red even with mock data switched on.
/// </para>
/// </summary>
internal static class ConnectionHealthReporter
{
    internal static async Task<IReadOnlyList<ConnectionHealthDto>> BuildAsync(
        HealthCheckService healthCheckService,
        CancellationToken ct)
    {
        HealthReport report = await healthCheckService.CheckHealthAsync(ct);

        return [.. report.Entries.Select(e => new ConnectionHealthDto(
            Name: e.Key,
            Status: e.Value.Status.ToString(),
            Description: e.Value.Description,
            DurationMs: e.Value.Duration.TotalMilliseconds))];
    }
}
