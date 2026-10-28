using System.Collections.Concurrent;

using PoTraffic.API.Infrastructure.Scheduling;
using PoTraffic.API.Infrastructure.Storage;



using Microsoft.Extensions.Logging;



using PoTraffic.API.Features.Routes;

using PoTraffic.Shared.Constants;

using PoTraffic.Shared.Enums;

namespace PoTraffic.API.Features.MonitoringWindows;

public sealed record StartWindowCommand(
    WindowId WindowId,
    UserId UserId);

/// <param name="SessionId">The day's session; null when the start only armed the chain
/// because the window is not open yet (<paramref name="WindowOpen"/> false).</param>
/// <param name="WindowOpen">False when "now" is outside the window: no session or quota was
/// spent, and the chain creates the session when it first polls inside the window.</param>
/// <param name="NextWindowStartUtc">When an armed-but-waiting window next opens.</param>
public sealed record StartWindowResult(
    bool IsSuccess,
    string? ErrorCode,   // RouteErrorCodes.NotFound | "QUOTA_EXCEEDED"
    int QuotaRemaining,
    SessionId? SessionId,
    bool WindowOpen = true,
    DateTimeOffset? NextWindowStartUtc = null);

public sealed class StartWindowCommandHandler
{
    // Per-route single-flight gate. The store is a process singleton with no compound-operation
    // isolation, so without this two concurrent Starts (double-click / client retry) can both
    // observe "no session yet", both pass the quota gate, and both enqueue a poll chain —
    // duplicate paid provider calls plus an orphaned, un-cancellable chain.
    private static readonly ConcurrentDictionary<RouteId, SemaphoreSlim> s_routeGates = new();

    private static SemaphoreSlim GateFor(RouteId routeId) =>
        s_routeGates.GetOrAdd(routeId, static _ => new SemaphoreSlim(1, 1));

    private readonly TableStorageContext _db;
    private readonly IJobScheduler _scheduler;
    private readonly ILogger<StartWindowCommandHandler> _logger;

    public StartWindowCommandHandler(
        TableStorageContext db,
        IJobScheduler scheduler,
        ILogger<StartWindowCommandHandler> logger)
    {
        _db = db;
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task<StartWindowResult> Handle(StartWindowCommand cmd, CancellationToken ct)
    {
        // 1. Load window, then resolve the owning route explicitly — TableStorageContext
        // has no navigation property to back the old `w.Route.UserId` access.
        MonitoringWindow? window = _db.MonitoringWindows
            .FirstOrDefault(w => w.Id == cmd.WindowId);

        if (window is null)
            return new StartWindowResult(false, RouteErrorCodes.NotFound, 0, null);

        EntityRoute? route = _db.GetOwnedRoute(window.RouteId, cmd.UserId, excludeDeleted: true);
        if (route is null)
            return new StartWindowResult(false, RouteErrorCodes.NotFound, 0, null);

        // Sessions and the daily quota are keyed by the window's local date, matching the
        // poll chain (PollRouteJob.EnsureActiveSessionAsync) — see MonitoringWindow.LocalDateAt.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly today = window.LocalDateAt(now);

        SemaphoreSlim gate = GateFor(route.Id);
        await gate.WaitAsync(ct);
        try
        {
            int todaySessionCount = PollRouteJob.SessionsUsedOn(_db, cmd.UserId, today);
            int dailyQuota = PollRouteJob.DailySessionQuota(_db);

            // 2. Idempotent guard — return existing session if one already exists for this route today
            MonitoringSession? existingSession = _db.MonitoringSessions
                .FirstOrDefault(s => s.RouteId == route.Id && s.SessionDate == today);

            if (existingSession is not null)
            {
                _logger.LogInformation(
                    "Start called for window {WindowId} but session {SessionId} already exists for today — idempotent return",
                    cmd.WindowId, existingSession.Id);
                return new StartWindowResult(true, null, Math.Max(0, dailyQuota - todaySessionCount), existingSession.Id);
            }

            // 3. Outside the window: arm the chain but spend nothing. A session bought now
            // would sit idle until the window opens (or, for a window that opens tomorrow,
            // be charged to the wrong day); the chain sleeps until the start and creates the
            // session itself on its first in-window poll, quota permitting.
            if (!PollRouteJob.IsWithinWindow(window, now))
            {
                DateTimeOffset? nextStart = PollRouteJob.NextWindowStart(window, now);

                // Opening later today with today's quota already gone: arming would only
                // sleep until the window and then skip it, so say so now.
                if (nextStart is { } opens
                    && window.LocalDateAt(opens) == today
                    && todaySessionCount >= dailyQuota)
                {
                    _logger.LogInformation("Quota exceeded for user {UserId} on {Date}", cmd.UserId, today);
                    return new StartWindowResult(false, "QUOTA_EXCEEDED", 0, null);
                }

                string armedJobId = ArmChain(route);
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Monitoring armed for window {WindowId}, route {RouteId}, job {JobId} — waiting for window start {NextStart:O}",
                    cmd.WindowId, route.Id, armedJobId, nextStart);

                return new StartWindowResult(
                    true, null, Math.Max(0, dailyQuota - todaySessionCount), null,
                    WindowOpen: false, NextWindowStartUtc: nextStart);
            }

            // 4. Inside the window: the session starts now, against today's quota.
            if (todaySessionCount >= dailyQuota)
            {
                _logger.LogInformation("Quota exceeded for user {UserId} on {Date}", cmd.UserId, today);
                return new StartWindowResult(false, "QUOTA_EXCEEDED", 0, null);
            }

            // 5. Create MonitoringSession (IsHolidayExcluded always false — PublicHolidays table removed S-09)
            var session = new MonitoringSession
            {
                Id = SessionId.New(),
                RouteId = route.Id,
                SessionDate = today,
                State = (int)SessionState.Active,
                IsHolidayExcluded = false,
                QuotaConsumed = 0,
                PollCount = 0
            };

            _db.Add(session);

            await _db.SaveChangesAsync(ct);

            // 6. Enqueue AFTER successful DB save — ensures no orphan job if SaveChanges fails.
            string jobId = ArmChain(route);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Monitoring started for window {WindowId}, route {RouteId}, session {SessionId}, job {JobId}",
                cmd.WindowId, route.Id, session.Id, jobId);

            int remaining = dailyQuota - todaySessionCount - 1;
            return new StartWindowResult(true, null, remaining, session.Id);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Enqueues a fresh poll chain and records its job id on the route (so DeleteRouteCommand
    /// and Stop can cancel it). Any prior chain is cancelled first so a second, un-cancellable
    /// chain is never left running. The caller saves.
    /// </summary>
    private string ArmChain(EntityRoute route)
    {
        if (route.JobChainId is { } priorJobId)
            _scheduler.Cancel(priorJobId);

        PollRouteJob routeJob = new(null!, null!, null!);
        string jobId = _scheduler.Enqueue(() => routeJob.Execute(route.Id));
        route.JobChainId = jobId;
        return jobId;
    }
}
