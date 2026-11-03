using System.Net.Http.Json;
using PoTraffic.Client.Infrastructure.Http;
using Radzen;

namespace PoTraffic.Client.Infrastructure;

/// <summary>
/// The one user-initiated probe. The dashboard card and the route page used to carry a
/// copy each, with different labels ("Check now" / "Probe now") and different toasts
/// for the same POST — the one action that spends money should not have two
/// implementations that can drift.
/// </summary>
public static class CheckNow
{
    /// <summary>Runs a live check and reports the result as a toast. The result on success, else null.</summary>
    /// <param name="fx">
    /// When given, the result is also heard and seen: the check cue, pitched by the result
    /// against <paramref name="baselineSeconds"/>, and a burst when it came back faster.
    /// </param>
    /// <param name="baselineSeconds">The route's usual time for this slot, if the caller knows it.</param>
    public static async Task<CheckNowResponse?> RunAsync(
        HttpClient http,
        NotificationService notifications,
        Guid routeId,
        FxService? fx = null,
        double? baselineSeconds = null)
    {
        try
        {
            HttpResponseMessage resp = await http.PostAsync($"/api/routes/{routeId}/check-now", null);
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
                if (fx is not null && data is not null)
                    await fx.CheckResultAsync(data.DurationSeconds, baselineSeconds);
                return data;
            }

            Fail(notifications, resp.StatusCode switch
            {
                System.Net.HttpStatusCode.ServiceUnavailable => "Traffic provider unavailable — try again in a moment.",
                System.Net.HttpStatusCode.TooManyRequests => "Too many checks — wait a minute and try again.",
                System.Net.HttpStatusCode.PaymentRequired => "This month's traffic budget is used up — checks resume next month.",
                _ => $"Request failed ({(int)resp.StatusCode})."
            });
        }
        // Timeouts surface as TaskCanceledException and a bad body as JsonException; left
        // uncaught, either one raised Blazor's unhandled-error bar from a button click.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Fail(notifications, "Unable to connect. Please try again.");
        }
        return null;
    }

    /// <summary>
    /// A baseline for <see cref="RunAsync"/> where the caller has no slot-specific typical
    /// time: the median of the route's recent samples (the dashboard's 24-hour window).
    /// Null below three samples — too few to call anything "usual".
    /// </summary>
    public static double? MedianBaseline(IReadOnlyList<PoTraffic.Shared.DTOs.History.PollRecordDto> polls)
    {
        if (polls.Count < 3)
            return null;
        int[] sorted = [.. polls.Select(p => p.TravelDurationSeconds).Order()];
        return sorted[sorted.Length / 2];
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
