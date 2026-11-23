using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoTraffic.Api.Infrastructure;

namespace PoTraffic.Unit.Infrastructure;

/// <summary>
/// /health, /health/json and /api/diag share ONE health report: the checks run at most once per
/// <see cref="CachedHealthReport.CacheFor"/> (60 s), concurrent callers share the run in flight,
/// and a caller's cancellation never reaches the shared run.
/// </summary>
public sealed class CachedHealthReportTests
{
    private sealed class CountingHealthChecks : HealthCheckService
    {
        public int Runs;
        public CancellationToken LastToken;
        public TaskCompletionSource? Gate;

        public override async Task<HealthReport> CheckHealthAsync(Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Runs);
            LastToken = cancellationToken;
            if (Gate is not null)
                await Gate.Task;
            return new HealthReport(new Dictionary<string, HealthReportEntry>
            {
                ["storage"] = new(HealthStatus.Healthy, "ok", TimeSpan.FromMilliseconds(Runs), null, null),
            }, TimeSpan.FromMilliseconds(1));
        }
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cache_lasts_about_a_minute() =>
        CachedHealthReport.CacheFor.Should().Be(TimeSpan.FromSeconds(60));

    [Fact]
    public async Task Calls_within_the_cache_window_share_one_run_and_one_checked_time()
    {
        var checks = new CountingHealthChecks();
        var clock = new ManualClock(T0);
        using var sut = new CachedHealthReport(checks, clock);

        CachedHealthReport.Snapshot first = await sut.GetAsync(CancellationToken.None);
        clock.Now = T0.AddSeconds(59);
        CachedHealthReport.Snapshot second = await sut.GetAsync(CancellationToken.None);

        checks.Runs.Should().Be(1);
        second.Should().BeSameAs(first);
        second.CheckedAt.Should().Be(T0);
    }

    [Fact]
    public async Task After_the_cache_window_the_checks_run_again()
    {
        var checks = new CountingHealthChecks();
        var clock = new ManualClock(T0);
        using var sut = new CachedHealthReport(checks, clock);

        await sut.GetAsync(CancellationToken.None);
        clock.Now = T0.AddSeconds(61);
        CachedHealthReport.Snapshot later = await sut.GetAsync(CancellationToken.None);

        checks.Runs.Should().Be(2);
        later.CheckedAt.Should().Be(T0.AddSeconds(61));
    }

    [Fact]
    public async Task Concurrent_callers_wait_for_the_one_run_in_flight()
    {
        var checks = new CountingHealthChecks { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var sut = new CachedHealthReport(checks, new ManualClock(T0));

        Task<CachedHealthReport.Snapshot>[] callers = [.. Enumerable.Range(0, 10).Select(_ => sut.GetAsync(CancellationToken.None))];
        checks.Gate.SetResult();
        CachedHealthReport.Snapshot[] results = await Task.WhenAll(callers);

        checks.Runs.Should().Be(1);
        results.Should().OnlyContain(r => ReferenceEquals(r, results[0]));
    }

    [Fact]
    public async Task A_callers_cancellation_token_is_not_passed_to_the_shared_run()
    {
        var checks = new CountingHealthChecks();
        using var sut = new CachedHealthReport(checks, new ManualClock(T0));
        using var cts = new CancellationTokenSource();

        await sut.GetAsync(cts.Token);

        checks.LastToken.CanBeCanceled.Should().BeFalse();
    }
}
