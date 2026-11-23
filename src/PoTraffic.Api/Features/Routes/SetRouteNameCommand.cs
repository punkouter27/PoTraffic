using FluentValidation;
using PoTraffic.Api.Infrastructure.Storage;

namespace PoTraffic.Api.Features.Routes;

/// <summary>Names a route; blank clears the name.</summary>
public sealed record SetRouteNameCommand(RouteId RouteId, UserId UserId, string? Name);

public sealed record SetRouteNameRequest(string? Name);

public sealed class SetRouteNameValidator : AbstractValidator<SetRouteNameCommand>
{
    public const int MaxLength = 60;

    public SetRouteNameValidator() =>
        RuleFor(c => c.Name).MaximumLength(MaxLength);
}

public sealed class SetRouteNameHandler(TableStorageContext db, IValidator<SetRouteNameCommand> validator)
{
    public async Task<bool> Handle(SetRouteNameCommand cmd, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(cmd, ct);

        EntityRoute? route = db.GetOwnedRoute(cmd.RouteId, cmd.UserId, excludeDeleted: true);
        if (route is null)
            return false;

        route.Name = string.IsNullOrWhiteSpace(cmd.Name) ? null : cmd.Name.Trim();
        await db.SaveChangesAsync(ct);
        return true;
    }
}
