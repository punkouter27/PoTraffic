using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PoTraffic.API.Features.Account;
using PoTraffic.API.Infrastructure.Security;
using PoTraffic.Shared.DTOs.Account;

namespace PoTraffic.API.Features.Account;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // Reads pass RequestAborted; writes pass CancellationToken.None so a closed tab cannot
        // abandon a half-applied change (same rule as every other slice).
        RouteGroupBuilder grp = app.MapGroup("/api/account")
            .RequireAuthorization("ProductionMicrosoftAuth")
            .WithTags("Account");

        grp.MapGet("/profile", async (ClaimsPrincipal user, GetProfileHandler handler, CancellationToken ct) =>
        {
            UserId userId = user.GetUserId();
            ProfileDto? profile = await handler.Handle(new GetProfileQuery(userId), ct);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .WithName("GetProfile")
        .Produces<ProfileDto>()
        .Produces(StatusCodes.Status404NotFound);

        grp.MapPut("/profile", async (
            ClaimsPrincipal user,
            [FromBody] UpdateProfileRequest body,
            UpdateProfileHandler handler) =>
        {
            UserId userId = user.GetUserId();
            ProfileDto? updated = await handler.Handle(new UpdateProfileCommand(userId, body.Locale), CancellationToken.None);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithName("UpdateProfile")
        .Produces<ProfileDto>()
        .Produces(StatusCodes.Status404NotFound);

        grp.MapPut("/timezone", async (
            ClaimsPrincipal user,
            [FromBody] SetTimeZoneRequest body,
            SetTimeZoneHandler handler) =>
        {
            bool found = await handler.Handle(new SetTimeZoneCommand(user.GetUserId(), body.TimeZoneId), CancellationToken.None);
            return found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("SetTimeZone")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        // Settings → Advanced switches, saved on the account so they follow the user.
        grp.MapGet("/panels", async (ClaimsPrincipal user, GetAdvancedPanelsHandler handler, CancellationToken ct) =>
        {
            string[]? panels = await handler.Handle(new GetAdvancedPanelsQuery(user.GetUserId()), ct);
            return panels is null ? Results.NotFound() : Results.Ok(panels);
        })
        .WithName("GetAdvancedPanels")
        .Produces<string[]>()
        .Produces(StatusCodes.Status404NotFound);

        grp.MapPut("/panels/{panel}", async (
            ClaimsPrincipal user,
            string panel,
            [FromBody] AdvancedPanelRequest body,
            SetAdvancedPanelHandler handler) =>
        {
            if (!Enum.TryParse(panel, ignoreCase: true, out PoTraffic.Shared.Enums.AdvancedPanel parsed)
                || !Enum.IsDefined(parsed))
                return Results.NotFound();

            bool found = await handler.Handle(
                new SetAdvancedPanelCommand(user.GetUserId(), parsed, body.On), CancellationToken.None);
            return found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("SetAdvancedPanel")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        grp.MapGet("/quota", async (ClaimsPrincipal user, GetQuotaHandler handler, CancellationToken ct) =>
        {
            UserId userId = user.GetUserId();
            QuotaDto? quota = await handler.Handle(new GetQuotaQuery(userId), ct);
            return quota is null ? Results.NotFound() : Results.Ok(quota);
        })
        .WithName("GetQuota")
        .Produces<QuotaDto>()
        .Produces(StatusCodes.Status404NotFound);

        // GDPR Art. 20 — download everything stored about the caller as one JSON file
        grp.MapGet("/export", async (ClaimsPrincipal user, ExportAccountHandler handler, CancellationToken ct) =>
        {
            AccountExport? export = await handler.Handle(new ExportAccountQuery(user.GetUserId()), ct);
            return export is null
                ? Results.NotFound()
                : Results.File(
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(export, ExportJson),
                    "application/json",
                    $"potraffic-export-{DateTime.UtcNow:yyyy-MM-dd}.json");
        })
        .WithName("ExportAccount");

        // FR-031: GDPR Art. 17 — self-service account deletion
        grp.MapDelete("/", async (ClaimsPrincipal user, DeleteAccountCommandHandler handler) =>
        {
            UserId userId = user.GetUserId();
            bool deleted = await handler.Handle(new DeleteAccountCommand(userId), CancellationToken.None);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteAccount")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static readonly System.Text.Json.JsonSerializerOptions ExportJson =
        new(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true };

}
