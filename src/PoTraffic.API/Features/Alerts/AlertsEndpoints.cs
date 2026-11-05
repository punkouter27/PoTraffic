using Microsoft.AspNetCore.Mvc;
using PoTraffic.API.Infrastructure.Security;

namespace PoTraffic.API.Features.Alerts;

public static class AlertsEndpoints
{
    public static IEndpointRouteBuilder MapAlertsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder alerts = app.MapGroup("/api/alerts")
            .RequireAuthorization("ProductionMicrosoftAuth").WithTags("Alerts");

        // GET /api/alerts?unreadOnly=true — recent alerts for the caller (#1)
        alerts.MapGet("", async (HttpContext ctx, GetAlertsQueryHandler handler, [FromQuery] bool unreadOnly = false) =>
            Results.Ok(await handler.Handle(new GetAlertsQuery(ctx.User.GetUserId(), unreadOnly), ctx.RequestAborted)));

        alerts.MapPost("{id:guid}/read", async (AlertId id, HttpContext ctx, MarkAlertsReadCommandHandler handler) =>
            await handler.Handle(new MarkAlertsReadCommand(ctx.User.GetUserId(), id), CancellationToken.None)
                ? Results.NoContent()
                : Results.NotFound());

        alerts.MapPost("read-all", async (HttpContext ctx, MarkAlertsReadCommandHandler handler) =>
        {
            await handler.Handle(new MarkAlertsReadCommand(ctx.User.GetUserId(), null), CancellationToken.None);
            return Results.NoContent();
        });

        RouteGroupBuilder push = app.MapGroup("/api/push")
            .RequireAuthorization("ProductionMicrosoftAuth").WithTags("Push");

        // GET /api/push/key — the VAPID public key a browser subscribes with
        push.MapGet("key", (VapidKeys vapid) => Results.Ok(new PushKeyResponse(vapid.PublicKey)));

        // POST /api/push/subscriptions — register (or refresh the keys of) this browser
        push.MapPost("subscriptions", async (
            HttpContext ctx, SavePushSubscriptionCommandHandler handler, [FromBody] PushSubscriptionRequest req) =>
        {
            await handler.Handle(
                new SavePushSubscriptionCommand(ctx.User.GetUserId(), req.Endpoint, req.P256dh, req.Auth),
                CancellationToken.None);
            return Results.NoContent();
        });

        // POST /api/push/unsubscribe — forget this browser
        push.MapPost("unsubscribe", async (
            HttpContext ctx, RemovePushSubscriptionCommandHandler handler, [FromBody] PushUnsubscribeRequest req) =>
        {
            await handler.Handle(new RemovePushSubscriptionCommand(ctx.User.GetUserId(), req.Endpoint), CancellationToken.None);
            return Results.NoContent();
        });

        return app;
    }
}

public sealed record PushKeyResponse(string PublicKey);
public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth);
public sealed record PushUnsubscribeRequest(string Endpoint);
