
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PoTraffic.API.Infrastructure.Security;
using PoTraffic.Shared.DTOs.Routes;

namespace PoTraffic.API.Features.MonitoringWindows;

public static class WindowsEndpoints
{
    public static IEndpointRouteBuilder MapWindowsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/routes/{routeId:guid}/windows")
            .RequireAuthorization("ProductionMicrosoftAuth")
            .WithTags("Windows");

        // No GET or DELETE: the client reads windows off the route payload and edits a
        // window by replacing it (POST with ReplacesWindowId), so neither had a caller.
        group.MapPost("", CreateWindow);
        group.MapPost("{windowId:guid}/start", StartWindow);
        group.MapPost("{windowId:guid}/stop", StopWindow);

        return app;
    }

    private static UserId? ExtractUserId(ClaimsPrincipal user)
        => user.GetUserIdOrNull();

    // POST /api/routes/{routeId}/windows
    private static async Task<IResult> CreateWindow(
        RouteId routeId,
        HttpContext context,
        CreateWindowCommandHandler handler,
        [FromBody] CreateWindowRequest request)
    {
        UserId? userId = ExtractUserId(context.User);
        if (userId is null) return Results.Unauthorized();

        if (!TimeOnly.TryParse(request.StartTime, out TimeOnly start))
            return Results.BadRequest(new { error = "INVALID_START_TIME" });

        if (!TimeOnly.TryParse(request.EndTime, out TimeOnly end))
            return Results.BadRequest(new { error = "INVALID_END_TIME" });

        CreateWindowResult result = await handler.Handle(
            new CreateWindowCommand(routeId, userId.Value, start, end, request.DaysOfWeekMask, request.TimeZoneId, request.ReplacesWindowId), CancellationToken.None);

        return result.IsSuccess
            ? Results.Created($"/api/routes/{routeId}/windows/{result.WindowId}", new { windowId = result.WindowId })
            : result.ErrorCode switch
            {
                RouteErrorCodes.NotFound => Results.NotFound(),
                "WINDOW_ALREADY_ACTIVE" => Results.Conflict(new { error = result.ErrorCode }),
                _ => Results.UnprocessableEntity(new { error = result.ErrorCode })
            };
    }

    // POST /api/routes/{routeId}/windows/{windowId}/start
    private static async Task<IResult> StartWindow(
        RouteId routeId,
        WindowId windowId,
        HttpContext context,
        StartWindowCommandHandler handler)
    {
        UserId? userId = ExtractUserId(context.User);
        if (userId is null) return Results.Unauthorized();

        StartWindowResult result = await handler.Handle(new StartWindowCommand(windowId, userId.Value), CancellationToken.None);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                RouteErrorCodes.NotFound => Results.NotFound(),
                "QUOTA_EXCEEDED" => Results.StatusCode(429),
                _ => Results.UnprocessableEntity(new { error = result.ErrorCode })
            };
        }

        // status/nextWindowStartUtc are additive: "waiting" means the chain is armed but the
        // window has not opened, so sessionId is null and no quota was spent yet.
        return Results.Ok(new
        {
            sessionId = result.SessionId,
            quotaRemaining = result.QuotaRemaining,
            status = result.WindowOpen ? "active" : "waiting",
            nextWindowStartUtc = result.NextWindowStartUtc
        });
    }

    // POST /api/routes/{routeId}/windows/{windowId}/stop
    private static async Task<IResult> StopWindow(
        RouteId routeId,
        WindowId windowId,
        HttpContext context,
        StopWindowCommandHandler handler,
        [FromBody] StopWindowRequest request)
    {
        UserId? userId = ExtractUserId(context.User);
        if (userId is null) return Results.Unauthorized();

        bool stopped = await handler.Handle(new StopWindowCommand(request.SessionId, userId.Value), CancellationToken.None);
        return stopped ? Results.NoContent() : Results.NotFound();
    }
}

// Request DTOs scoped to this endpoint only
public sealed record CreateWindowRequest(
    string StartTime,
    string EndTime,
    byte DaysOfWeekMask,
    string? TimeZoneId = null,
    WindowId? ReplacesWindowId = null);

public sealed record StopWindowRequest(SessionId SessionId);
