using System.Net;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.Enums;
using PoTraffic.Shared.Ids;

namespace PoTraffic.Mobile.Services;

/// <summary>The OS side of geofencing: Android proximity alerts, iOS region monitoring.</summary>
public interface IGeofenceRegistrar
{
    /// <summary>Asks for "Allow all the time" location; false when the user declines.</summary>
    Task<bool> RequestPermissionAsync();

    void Register(CommuteFence fence);

    void Unregister(string routeId);
}

/// <summary>
/// Leaving a commute's start point starts that day's monitoring and takes one live reading, so
/// the user never has to remember to open the app; arriving at the end point can stop it (saving
/// the daily quota). Registrations are kept on disk so a reboot can restore them.
/// </summary>
public sealed class CommuteFences(OfflineStore store, IGeofenceRegistrar registrar)
{
    public const double RadiusMetres = 150;
    private const string StoreKey = "fences";

    public static List<CommuteFence> Load() => OfflineStore.LoadNow(StoreKey, MobileJsonContext.Default.ListCommuteFence) ?? [];

    public static CommuteFence? For(RouteId id) => Load().FirstOrDefault(f => f.RouteId == id.Value.ToString());

    /// <summary>Turns detection on (with the given stop-on-arrival choice) or off for a route.</summary>
    public async Task<string?> SetAsync(RouteDto route, bool on, bool stopOnArrival)
    {
        List<CommuteFence> fences = [.. Load().Where(f => f.RouteId != route.Id.Value.ToString())];
        registrar.Unregister(route.Id.Value.ToString());

        if (on)
        {
            if (Format.Coordinates(route.OriginCoordinates) is not { } o || Format.Coordinates(route.DestinationCoordinates) is not { } d)
            {
                return "This commute has no map position yet.";
            }

            if (!await registrar.RequestPermissionAsync())
            {
                return "Leaving-home detection needs location access set to \"Allow all the time\".";
            }

            CommuteFence fence = new(route.Id.Value.ToString(), Label(route), o.Lat, o.Lng, d.Lat, d.Lng, stopOnArrival);
            registrar.Register(fence);
            fences.Add(fence);
        }

        await store.SaveAsync(StoreKey, fences, MobileJsonContext.Default.ListCommuteFence);
        return null;
    }

    /// <summary>Re-registers everything (after a reboot, which clears the OS's alerts).</summary>
    public static void RestoreAll(IGeofenceRegistrar registrar)
    {
        foreach (CommuteFence fence in Load())
        {
            registrar.Register(fence);
        }
    }

    public static string Label(RouteDto route) => route.Name ?? $"{Format.Street(route.OriginAddress)} → {Format.Street(route.DestinationAddress)}";

    /// <summary>
    /// Runs when the OS reports a crossing — possibly with the app in the background or just
    /// relaunched, so it resolves services itself and reports by notification, not UI.
    /// </summary>
    public static async Task OnCrossingAsync(IServiceProvider services, string routeId, bool atOrigin, bool entering)
    {
        if (!Guid.TryParse(routeId, out Guid guid) || For(RouteId.From(guid)) is not { } fence)
        {
            return;
        }

        ApiClient api = services.GetRequiredService<ApiClient>();
        IAlarmScheduler notify = services.GetRequiredService<IAlarmScheduler>();
        int noteId = (BitConverter.ToInt32(guid.ToByteArray(), 4) & 0x3FFFFFFF) | 0x40000000;
        RouteId id = RouteId.From(guid);

        try
        {
            if (atOrigin && !entering)
            {
                await StartAsync(api, notify, noteId, id, fence);
            }
            else if (!atOrigin && entering && fence.StopOnArrival)
            {
                await StopAsync(api, notify, noteId, id, fence);
            }
        }
        catch (HttpRequestException)
        {
            notify.ShowNow(noteId, "PoTraffic couldn't reach the server", $"Open the app to time {fence.Label}.", routeId);
        }
    }

    private static async Task StartAsync(ApiClient api, IAlarmScheduler notify, int noteId, RouteId id, CommuteFence fence)
    {
        RouteDto? route = await api.GetRouteAsync(id);
        if (route is null || route.MonitoringStatus == MonitoringStatus.Deleted)
        {
            return;
        }

        if ((route.Windows.FirstOrDefault(w => w.IsActive) ?? (route.Windows.Count > 0 ? route.Windows[0] : null)) is not { } window)
        {
            notify.ShowNow(noteId, "You left — but there's no schedule", $"Give {fence.Label} a monitoring schedule to time it automatically.", fence.RouteId);
            return;
        }

        using HttpResponseMessage started = await api.StartWindowAsync(id, window.Id);
        if (started.StatusCode == HttpStatusCode.TooManyRequests)
        {
            notify.ShowNow(noteId, "Daily monitoring limit reached", $"{fence.Label} won't be timed today.", fence.RouteId);
            return;
        }

        (var live, _) = await api.CheckNowAsync(id);
        string now = live is { } r ? $"{Format.Minutes(r.DurationSeconds)} min right now ({Format.Distance(r.DistanceMetres)})" : "timing started";
        notify.ShowNow(noteId, $"On your way: {fence.Label}", $"{now}. PoTraffic is timing this trip.", fence.RouteId);
    }

    private static async Task StopAsync(ApiClient api, IAlarmScheduler notify, int noteId, RouteId id, CommuteFence fence)
    {
        RouteDto? route = await api.GetRouteAsync(id);
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        var session = (await api.GetSessionsAsync(id))?.FirstOrDefault(s => s.State == SessionState.Active && s.SessionDate == today);
        if (route?.Windows.FirstOrDefault(w => w.IsActive) is not { } window || session is null)
        {
            return;
        }

        using HttpResponseMessage stopped = await api.StopWindowAsync(id, window.Id, session.Id);
        if (stopped.IsSuccessStatusCode)
        {
            notify.ShowNow(noteId, $"Arrived: {fence.Label}", $"Stopped timing after {session.PollCount} samples.", fence.RouteId);
        }
    }
}
