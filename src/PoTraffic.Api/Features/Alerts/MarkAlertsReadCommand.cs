using PoTraffic.Api.Infrastructure.Storage;

namespace PoTraffic.Api.Features.Alerts;

/// <param name="AlertId">One alert, or null for every unread alert the user has.</param>
public sealed record MarkAlertsReadCommand(UserId UserId, AlertId? AlertId);

/// <summary>Marks one or all of the caller's alerts read. False when the one alert is not theirs.</summary>
public sealed class MarkAlertsReadCommandHandler(TableStorageContext db)
{
    public async Task<bool> Handle(MarkAlertsReadCommand cmd, CancellationToken ct)
    {
        List<Alert> alerts = cmd.AlertId is { } id
            ? [.. db.Alerts.Where(a => a.Id == id && a.UserId == cmd.UserId)]
            : [.. db.Alerts.Where(a => a.UserId == cmd.UserId && !a.IsRead)];

        if (cmd.AlertId is not null && alerts.Count == 0)
            return false;

        foreach (Alert alert in alerts)
            alert.IsRead = true;

        await db.SaveChangesAsync(ct);
        return true;
    }
}
