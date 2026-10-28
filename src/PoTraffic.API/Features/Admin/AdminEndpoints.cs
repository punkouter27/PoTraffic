using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoTraffic.API.Features.Admin;
using PoTraffic.API.Infrastructure;
using PoTraffic.Shared.DTOs.Admin;
using PoTraffic.Shared.Enums;

namespace PoTraffic.API.Features.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // FR-022: All admin endpoints require Administrator role
        RouteGroupBuilder grp = app.MapGroup("/api/admin")
            .RequireAuthorization("AdminOnly")
            .RequireAuthorization("ProductionMicrosoftAuth")
            .WithTags("Admin");

        grp.MapGet("/users", async (GetUsersHandler handler, CancellationToken ct) =>
        {
            IReadOnlyList<UserDailyUsageDto> users = await handler.Handle(new GetUsersQuery(), ct);
            return Results.Ok(users);
        })
        .WithName("GetAdminUsers")
        .Produces<IReadOnlyList<UserDailyUsageDto>>();

        grp.MapGet("/configuration", async (GetSystemConfigurationHandler handler, CancellationToken ct) =>
        {
            IReadOnlyList<SystemConfigDto> configs = await handler.Handle(new GetSystemConfigurationQuery(), ct);
            return Results.Ok(configs);
        })
        .WithName("GetSystemConfiguration")
        .Produces<IReadOnlyList<SystemConfigDto>>();

        // GET /api/admin/global-volatility/recent?hours=24 — real-time 24h rolling time-series
        grp.MapGet("/global-volatility/recent", async (
            GetRecentVolatilityHandler handler,
            CancellationToken ct,
            [FromQuery] int hours = 24) =>
        {
            IReadOnlyList<RecentVolatilityPointDto> points = await handler.Handle(new GetRecentVolatilityQuery(hours), ct);
            return Results.Ok(points);
        })
        .WithName("GetRecentVolatility")
        .Produces<IReadOnlyList<RecentVolatilityPointDto>>();

        grp.MapGet("/spending", async (GetSpendingHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetSpendingQuery(), ct)))
        .WithName("GetSpending")
        .Produces<SpendingDto>();

        // Writes pass CancellationToken.None: a closed tab must not abandon a half-saved change.
        grp.MapPut("/users/{userId:guid}/daily-quota", async (
            UserId userId, SetUserDailyQuotaHandler handler, [FromBody] SetUserDailyQuotaRequest body) =>
        {
            bool found = await handler.Handle(new SetUserDailyQuotaCommand(userId, body.DailySessionQuota), CancellationToken.None);
            return found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("SetUserDailyQuota");

        grp.MapPut("/configuration/{key}", async (
            string key, UpdateSystemConfigurationHandler handler, [FromBody] UpdateSystemConfigRequest body) =>
        {
            bool found = await handler.Handle(new UpdateSystemConfigurationCommand(key, body.Value), CancellationToken.None);
            return found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("UpdateSystemConfiguration");
        return app;
    }
}

