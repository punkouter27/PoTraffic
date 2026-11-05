using PoTraffic.API.Infrastructure.Storage;
using PoTraffic.Shared.DTOs.Alerts;

namespace PoTraffic.API.Features.Alerts;

public sealed record GetAlertsQuery(UserId UserId, bool UnreadOnly);

/// <summary>The caller's 50 most recent alerts, newest first (#1).</summary>
public sealed class GetAlertsQueryHandler(TableStorageContext db)
{
    private const int Limit = 50;

    public Task<List<AlertDto>> Handle(GetAlertsQuery query, CancellationToken ct)
    {
        IEnumerable<Alert> alerts = db.Alerts.Where(a => a.UserId == query.UserId);
        if (query.UnreadOnly)
            alerts = alerts.Where(a => !a.IsRead);

        return Task.FromResult(alerts
            .OrderByDescending(a => a.CreatedAt)
            .Take(Limit)
            .Select(AlertEvaluator.ToDto)
            .ToList());
    }
}
