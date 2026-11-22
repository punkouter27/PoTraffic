using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoTraffic.Api.Infrastructure.Scheduling;
using PoTraffic.Shared.DTOs.Admin;

namespace PoTraffic.Api.Features.Diagnostics;

/// <summary>
/// Builds the anonymous <c>GET /api/diag</c> report (<see cref="DiagReportDto"/>): the registered
/// health checks (storage, scheduler) plus the live <see cref="ServiceProbes"/> rows. Everything
/// in it is fixed text from <see cref="Describe"/> or the probes plus status and timing: a health
/// check's own description (which can name config keys or accounts) is never copied, nor is an
/// exception. Rows that are <see cref="ServiceProbes.Off"/> (switched off on purpose) are shown
/// but left out of the overall status.
/// </summary>
public sealed class DiagReportHandler(HealthCheckService healthChecks, ServiceProbes probes, IServiceProvider services, IHostEnvironment env)
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    /// <summary>The <c>YYMMDD</c> BuildVersion Directory.Build.props stamps on every assembly.</summary>
    internal static readonly string Version =
        typeof(DiagReportHandler).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildVersion")?.Value ?? "unknown";

    public async Task<DiagReportDto> Handle(CancellationToken ct)
    {
        long started = Clock.GetTimestamp();
        Task<DiagCheckDto[]> serviceRows = probes.RunAsync(ct);
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
            if (key == "scheduler" && services.GetService<IJobScheduler>() is null or NoOpJobScheduler)
            {
                // The Test host runs no timed checks on purpose.
                checks.Add(new DiagCheckDto(key, group, label, ServiceProbes.Off, purpose, "Off (no timed checks on this host)", 0, null));
                continue;
            }

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

        checks.AddRange(await serviceRows);

        HealthStatus overall = checks.Where(c => c.Status != ServiceProbes.Off)
            .Min(c => Enum.TryParse(c.Status, out HealthStatus s) ? s : HealthStatus.Unhealthy);
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
        "scheduler" => ("Scheduler", DiagGroups.Service, "Runs the timed traffic checks for monitored commutes.",
            "Timed traffic checks are not running."),
        _ => (key, DiagGroups.Service, "Another dependency this app checks.", "This dependency is not working."),
    };
}
