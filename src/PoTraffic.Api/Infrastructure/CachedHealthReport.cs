using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoTraffic.Api.Infrastructure;

/// <summary>
/// The one health report the app shares: <c>/health</c>, <c>/health/json</c> and <c>GET /api/diag</c>
/// all read it, and the registered checks (storage, scheduler) run at most once per
/// <see cref="CacheFor"/>, however many probes, deploy checks or anonymous /diag visitors arrive.
/// Concurrent callers wait for the one run in flight instead of starting their own. Each check has
/// its own timeout (<see cref="HealthCheckExtensions.CheckTimeout"/>), so a hung dependency becomes
/// an Unhealthy entry, never a hung request; the run ignores the caller's cancellation, so one
/// aborted request cannot poison the report every other caller shares.
/// </summary>
public sealed class CachedHealthReport(HealthCheckService healthChecks, TimeProvider clock) : IDisposable
{
    internal static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot? _cache;

    /// <summary>A health report and the moment its checks ran.</summary>
    public sealed record Snapshot(HealthReport Report, DateTimeOffset CheckedAt);

    public async Task<Snapshot> GetAsync(CancellationToken cancellationToken)
    {
        if (_cache is { } fresh && clock.GetUtcNow() - fresh.CheckedAt < CacheFor)
            return fresh;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is { } cached && clock.GetUtcNow() - cached.CheckedAt < CacheFor)
                return cached;

            DateTimeOffset at = clock.GetUtcNow();
            HealthReport report = await healthChecks.CheckHealthAsync(CancellationToken.None);
            _cache = new Snapshot(report, at);
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
