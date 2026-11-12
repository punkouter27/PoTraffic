using System.Net.Http.Json;
using PoTraffic.Client.Services;
using PoTraffic.Client.Services.Http;
using PoTraffic.Shared.DTOs;
using Radzen;

namespace PoTraffic.Client.Features.Routes;

/// <summary>
/// The one user-initiated probe shared by the dashboard card and the route page.
/// </summary>
public static class CheckNow
{
    public static async Task<CheckNowResponse?> RunAsync(
        PoTrafficApiClient api,
        NotificationService notifications,
        RouteId routeId)
    {
        try
        {
            HttpResponseMessage resp = await api.CheckNowAsync(routeId);
            if (resp.IsSuccessStatusCode)
            {
                CheckNowResponse? data = await resp.Content.ReadFromJsonAsync(AppJsonContext.Default.CheckNowResponse);
                notifications.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Info,
                    Summary = "Right now",
                    Detail = data is not null
                        ? $"{TextFormatting.Minutes(data.DurationSeconds)} min · {TextFormatting.Distance(data.DistanceMetres)}"
                        : "Live travel time retrieved",
                    Duration = 6000
                });
                return data;
            }

            string fallback = resp.StatusCode switch
            {
                System.Net.HttpStatusCode.ServiceUnavailable => "Traffic provider unavailable — try again in a moment.",
                System.Net.HttpStatusCode.TooManyRequests => "Too many checks — wait a minute and try again.",
                System.Net.HttpStatusCode.PaymentRequired => "This month's traffic budget is used up — checks resume next month.",
                _ => $"Request failed ({(int)resp.StatusCode})."
            };
            ProblemDetailsParser.ParsedError parsed = await ProblemDetailsParser.ParseAsync(resp, fallback);
            Fail(notifications, parsed.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Fail(notifications, "Unable to connect. Please try again.");
        }
        return null;
    }

    private static void Fail(NotificationService notifications, string detail) =>
        notifications.Notify(new NotificationMessage
        {
            Severity = NotificationSeverity.Error,
            Summary = "Check failed",
            Detail = detail,
            Duration = 5000
        });
}
