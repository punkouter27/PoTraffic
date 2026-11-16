using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using PoTraffic.Api.Infrastructure.Http;
using PoTraffic.Api.Infrastructure.Security;
using PoTraffic.Shared.DTOs.History;

namespace PoTraffic.Api.Features.History;

/// <summary>
/// Read-only history for one route: poll samples, sessions, departure advice, the heatmap and
/// the weather split. All require an authenticated (cookie) session. The route itself
/// (<c>GET /api/routes/{routeId}</c>) is mapped by the Routes slice.
/// </summary>
public static class HistoryEndpoints
{
    public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes
            .MapGroup("/api/routes/{routeId:guid}")
            .RequireAuthorization("ProductionMicrosoftAuth")
            .WithTags("History");

        // GET /api/routes/{routeId}/poll-history?page=1&pageSize=20&sinceUtc=2026-04-04T00:00:00Z
        group.MapGet("/poll-history", async (
            RouteId routeId,
            GetPollHistoryQueryHandler handler,
            HttpContext ctx,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] DateTime? sinceUtc = null) =>
        {
            UserId userId = ctx.User.GetUserId();
            var result = await handler.Handle(
                new GetPollHistoryQuery(routeId, userId, page, pageSize, sinceUtc), ctx.RequestAborted);
            return ConditionalJson.Ok(ctx, result);
        });

        // GET /api/routes/{routeId}/sessions
        group.MapGet("/sessions", async (
            RouteId routeId,
            GetSessionsQueryHandler handler,
            HttpContext ctx) =>
        {
            UserId userId = ctx.User.GetUserId();
            var result = await handler.Handle(new GetSessionsQuery(routeId, userId), ctx.RequestAborted);
            return ConditionalJson.Ok(ctx, result);
        });

        // GET /api/routes/{routeId}/departure?dayOfWeek=Monday — quickest slot + leave-by plan
        group.MapGet("/departure", async (
            RouteId routeId,
            GetDepartureQueryHandler handler,
            HttpContext ctx,
            [FromQuery] string dayOfWeek = "Monday") =>
        {
            UserId userId = ctx.User.GetUserId();
            DepartureDto? result = await handler.Handle(new GetDepartureQuery(routeId, userId, dayOfWeek), ctx.RequestAborted);
            return result is null ? Results.NoContent() : ConditionalJson.Ok(ctx, result);
        });

        // The weekday-comparison endpoint was removed with the bar chart it fed. It and
        // the heatmap were two renderings of the same aggregate, and the grid says
        // everything the bars said, per hour rather than per day.

        // GET /api/routes/{routeId}/heatmap — day-of-week × hour congestion grid (#5)
        group.MapGet("/heatmap", async (
            RouteId routeId,
            GetVolatilityHeatmapQueryHandler handler,
            HttpContext ctx) =>
        {
            UserId userId = ctx.User.GetUserId();
            var result = await handler.Handle(new GetVolatilityHeatmapQuery(routeId, userId), ctx.RequestAborted);
            return ConditionalJson.Ok(ctx, result);
        });

        // GET /api/routes/{routeId}/weather-impact — what each condition costs this route
        group.MapGet("/weather-impact", async (
            RouteId routeId,
            GetWeatherImpactQueryHandler handler,
            HttpContext ctx) =>
        {
            UserId userId = ctx.User.GetUserId();
            var result = await handler.Handle(new GetWeatherImpactQuery(routeId, userId), ctx.RequestAborted);
            return ConditionalJson.Ok(ctx, result);
        });

        return routes;
    }
}
