using PoTraffic.API.Infrastructure.Scheduling;
using PoTraffic.API.Infrastructure.Storage;
using PoTraffic.Shared.Enums;

namespace PoTraffic.API.Features.Routes;

/// <summary>
/// Pauses or resumes monitoring for a route. Paused keeps the schedule and the history but
/// stops the poll chain, so a commute nobody drives this month costs nothing until resumed.
/// Stop on the schedule only ends today's session; this is the switch that lasts.
/// </summary>
public sealed record SetRoutePausedCommand(RouteId RouteId, UserId UserId, bool Paused);

public sealed record SetRoutePausedRequest(bool Paused);

public sealed class SetRoutePausedHandler(TableStorageContext db, IJobScheduler scheduler)
{
    /// <summary>False when the route is missing, deleted, not the caller's, or the sample.</summary>
    public async Task<bool> Handle(SetRoutePausedCommand cmd, CancellationToken ct)
    {
        EntityRoute? route = db.GetOwnedRoute(cmd.RouteId, cmd.UserId, excludeDeleted: true);
        // The sample is never probed, so there is nothing to pause or resume.
        if (route is null || route.IsSample)
            return false;

        if (cmd.Paused)
        {
            if (route.JobChainId is { } chain)
                scheduler.Cancel(chain);
            route.JobChainId = null;
            route.MonitoringStatus = (int)MonitoringStatus.Paused;
        }
        else
        {
            route.MonitoringStatus = (int)MonitoringStatus.Active;
            // Re-arm only when there is a schedule to follow; PollRouteJob sleeps until it opens.
            if (route.JobChainId is null && db.Windows.Any(w => w.RouteId == route.Id && w.IsActive))
            {
                PollRouteJob job = new(null!, null!, null!);
                route.JobChainId = scheduler.Enqueue(() => job.Execute(route.Id));
            }
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
