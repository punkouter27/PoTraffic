using FluentValidation;
using PoTraffic.API.Infrastructure.Storage;

namespace PoTraffic.API.Features.Alerts;

public sealed record SavePushSubscriptionCommand(UserId UserId, string Endpoint, string P256dh, string Auth);

public sealed class SavePushSubscriptionValidator : AbstractValidator<SavePushSubscriptionCommand>
{
    public SavePushSubscriptionValidator()
    {
        // Only a real browser push service: the server POSTs to this URL, so anything else
        // would turn the alert sender into a request forger.
        RuleFor(c => c.Endpoint)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(WebPushNotifier.IsPushServiceEndpoint)
            .WithErrorCode("INVALID_PUSH_SUBSCRIPTION");
        RuleFor(c => c.P256dh).NotEmpty().MaximumLength(256);
        RuleFor(c => c.Auth).NotEmpty().MaximumLength(256);
    }
}

/// <summary>Registers this browser for push, or refreshes its keys if it is already registered.</summary>
public sealed class SavePushSubscriptionCommandHandler(TableStorageContext db)
{
    private static readonly SavePushSubscriptionValidator Validator = new();

    public async Task Handle(SavePushSubscriptionCommand cmd, CancellationToken ct)
    {
        await Validator.ValidateAndThrowAsync(cmd, ct);

        UserPushSubscription? existing = db.PushSubscriptions
            .FirstOrDefault(s => s.UserId == cmd.UserId && s.Endpoint == cmd.Endpoint);
        if (existing is not null)
        {
            existing.P256dh = cmd.P256dh;
            existing.Auth = cmd.Auth;
        }
        else
        {
            db.Add(new UserPushSubscription
            {
                Id = PushSubscriptionId.New(),
                UserId = cmd.UserId,
                Endpoint = cmd.Endpoint,
                P256dh = cmd.P256dh,
                Auth = cmd.Auth,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
