using FluentValidation;
using Microsoft.Extensions.Logging;
using PoTraffic.API.Infrastructure.Logging;
using PoTraffic.API.Infrastructure.Providers;
using PoTraffic.API.Infrastructure.Storage;
using PoTraffic.Shared.Constants;

namespace PoTraffic.API.Features.Routes;

/// <summary>
/// Changes a route's origin and/or destination — a typo, or a move across town — without
/// deleting the route and losing its schedule. Only an address that actually changed is
/// geocoded (each lookup is billed). The cached road shape and last incident belong to the
/// old addresses, so they are dropped; recorded samples stay, as the commute's history.
/// </summary>
public sealed record SetRouteAddressesCommand(
    RouteId RouteId,
    UserId UserId,
    string OriginAddress,
    string DestinationAddress);

public sealed record SetRouteAddressesRequest(string OriginAddress, string DestinationAddress);

/// <summary>Null error code on success; otherwise a <see cref="RouteErrorCodes"/> value.</summary>
public sealed record SetRouteAddressesResult(string? ErrorCode)
{
    public bool IsSuccess => ErrorCode is null;
}

public sealed class SetRouteAddressesValidator : AbstractValidator<SetRouteAddressesCommand>
{
    public SetRouteAddressesValidator()
    {
        RuleFor(x => x.OriginAddress).NotEmpty().MaximumLength(ValidationConstants.AddressMaxLength);
        RuleFor(x => x.DestinationAddress).NotEmpty().MaximumLength(ValidationConstants.AddressMaxLength);
    }
}

public sealed class SetRouteAddressesHandler(
    TableStorageContext db,
    ITrafficProvider provider,
    ILogger<SetRouteAddressesHandler> logger)
{
    private static readonly SetRouteAddressesValidator Validator = new();

    public async Task<SetRouteAddressesResult> Handle(SetRouteAddressesCommand cmd, CancellationToken ct)
    {
        await Validator.ValidateAndThrowAsync(cmd, ct);

        EntityRoute? route = db.GetOwnedRoute(cmd.RouteId, cmd.UserId, excludeDeleted: true);
        // The sample's addresses are part of its generated story; it is not editable.
        if (route is null || route.IsSample)
            return new SetRouteAddressesResult(RouteErrorCodes.NotFound);

        string origin = cmd.OriginAddress.Trim();
        string destination = cmd.DestinationAddress.Trim();

        string? originCoords = await ResolveAsync(origin, route.OriginAddress, route.OriginCoordinates, ct);
        if (originCoords is null)
            return new SetRouteAddressesResult(RouteErrorCodes.AddressNotFound);

        string? destCoords = await ResolveAsync(destination, route.DestinationAddress, route.DestinationCoordinates, ct);
        if (destCoords is null)
            return new SetRouteAddressesResult(RouteErrorCodes.AddressNotFound);

        if (originCoords == destCoords)
            return new SetRouteAddressesResult(RouteErrorCodes.SameCoordinates);

        bool moved = originCoords != route.OriginCoordinates || destCoords != route.DestinationCoordinates;
        route.OriginAddress = origin;
        route.OriginCoordinates = originCoords;
        route.DestinationAddress = destination;
        route.DestinationCoordinates = destCoords;
        if (moved)
        {
            route.PathPolyline = null;
            route.PathUnavailableAt = null;
            route.LastIncident = null;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Route {RouteId} addresses changed (moved: {Moved})", route.Id, moved);
        return new SetRouteAddressesResult(null);
    }

    /// <summary>Coordinates for <paramref name="address"/>, reusing the stored ones when it is unchanged.</summary>
    private async Task<string?> ResolveAsync(string address, string currentAddress, string currentCoords, CancellationToken ct)
    {
        if (string.Equals(address, currentAddress, StringComparison.OrdinalIgnoreCase))
            return currentCoords;

        string? coords = await provider.GeocodeAsync(address, ct);
        // Billed found or not, as on create.
        Config.SpendingCap.Record(db, Config.ProviderCallKind.Geocode, DateTimeOffset.UtcNow);
        if (coords is null)
            logger.LogWarning("Geocode failed for edited address {AddressRef}", PiiRedactor.Redact(address));
        return coords;
    }
}
