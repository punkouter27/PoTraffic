using FluentValidation;
using PoTraffic.API.Infrastructure.Storage;

namespace PoTraffic.API.Features.Admin;

/// <summary>Sets one user's daily session limit; null returns them to the global default.</summary>
public sealed record SetUserDailyQuotaCommand(UserId UserId, int? DailySessionQuota);

public sealed class SetUserDailyQuotaValidator : AbstractValidator<SetUserDailyQuotaCommand>
{
    /// <summary>Generous ceiling: a typo of an extra zero should fail, not bill a month.</summary>
    public const int MaxQuota = 200;

    public SetUserDailyQuotaValidator() =>
        RuleFor(c => c.DailySessionQuota)
            .InclusiveBetween(1, MaxQuota)
            .When(c => c.DailySessionQuota is not null);
}

public sealed class SetUserDailyQuotaHandler(TableStorageContext db)
{
    private static readonly SetUserDailyQuotaValidator Validator = new();

    /// <summary>False when the user does not exist.</summary>
    public async Task<bool> Handle(SetUserDailyQuotaCommand cmd, CancellationToken ct)
    {
        await Validator.ValidateAndThrowAsync(cmd, ct);

        User? user = db.Users.FirstOrDefault(u => u.Id == cmd.UserId);
        if (user is null)
            return false;

        user.DailySessionQuota = cmd.DailySessionQuota;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
