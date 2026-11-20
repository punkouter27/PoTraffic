using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoTraffic.Shared.DTOs.Admin;

namespace PoTraffic.Api.Features.Diagnostics;

/// <summary>
/// Turns the registered health checks into the anonymous <c>GET /api/diag</c> report
/// (<see cref="DiagReportDto"/>). Everything in it is fixed text from <see cref="Describe"/>
/// plus status and timing: a health check's own description (which can name config keys or
/// accounts) is never copied, nor is an exception.
/// </summary>
public sealed class DiagReportHandler(HealthCheckService healthChecks, IHostEnvironment env)
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    /// <summary>The <c>YYMMDD</c> BuildVersion Directory.Build.props stamps on every assembly.</summary>
    internal static readonly string Version =
        typeof(DiagReportHandler).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildVersion")?.Value ?? "unknown";

    public async Task<DiagReportDto> Handle(CancellationToken ct)
    {
        long started = Clock.GetTimestamp();
        HealthReport report = await healthChecks.CheckHealthAsync(ct);

        List<DiagCheckDto> checks =
        [
            // Answering this request is the check; the page measures the round trip itself.
            new("api", DiagGroups.Api, "PoTraffic API", nameof(HealthStatus.Healthy),
                "The server that answers this app's requests.", "Answering", 0, null),
        ];

        foreach ((string key, HealthReportEntry entry) in report.Entries)
        {
            (string label, string group, string purpose, string problem) = Describe(key);
            checks.Add(new DiagCheckDto(
                Name: key,
                Group: group,
                Label: label,
                Status: entry.Status.ToString(),
                Purpose: purpose,
                Detail: entry.Status switch
                {
                    HealthStatus.Healthy => "Working",
                    HealthStatus.Degraded => "Partly working",
                    _ => "Not working",
                },
                LatencyMs: Math.Round(entry.Duration.TotalMilliseconds, 1),
                Problem: entry.Status == HealthStatus.Healthy ? null : problem));
        }

        HealthStatus overall = checks.Min(c => Enum.TryParse(c.Status, out HealthStatus s) ? s : HealthStatus.Unhealthy);
        return new DiagReportDto(
            overall.ToString(),
            env.EnvironmentName,
            Version,
            Clock.GetUtcNow(),
            Math.Round(Clock.GetElapsedTime(started).TotalMilliseconds, 1),
            [.. checks]);
    }

    internal static (string Label, string Group, string Purpose, string Problem) Describe(string key) => key switch
    {
        "storage" => ("Table Storage", DiagGroups.Data, "Keeps your commutes, schedules and every recorded trip.",
            "Storage is not reachable, so commutes cannot be loaded or saved."),
        "keyvault" => ("Key Vault", DiagGroups.Service, "Holds the secrets that Microsoft sign-in needs.",
            "The sign-in secret did not load from Key Vault, so Microsoft sign-in will fail."),
        "trafficProvider" => ("Google Maps traffic", DiagGroups.Service, "Measures the travel time of each commute.",
            "The Google Maps key is missing, so travel times cannot be measured."),
        "scheduler" => ("Scheduler", DiagGroups.Service, "Runs the timed traffic checks for monitored commutes.",
            "Timed traffic checks are not running."),
        _ => (key, DiagGroups.Service, "Another dependency this app checks.", "This dependency is not working."),
    };
}
