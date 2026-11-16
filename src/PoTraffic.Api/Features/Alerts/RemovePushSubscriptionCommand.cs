using PoTraffic.Api.Infrastructure.Storage;

namespace PoTraffic.Api.Features.Alerts;

public sealed record RemovePushSubscriptionCommand(UserId UserId, string Endpoint);

/// <summary>Forgets this browser's push registration. Idempotent.</summary>
public sealed class RemovePushSubscriptionCommandHandler(TableStorageContext db)
{
    public async Task Handle(RemovePushSubscriptionCommand cmd, CancellationToken ct)
    {
        db.RemoveRange(db.PushSubscriptions
            .Where(s => s.UserId == cmd.UserId && s.Endpoint == cmd.Endpoint)
            .ToList());
        await db.SaveChangesAsync(ct);
    }
}
