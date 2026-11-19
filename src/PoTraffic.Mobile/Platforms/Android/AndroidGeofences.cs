using Android.App;
using Android.Content;
using Android.Locations;
using PoTraffic.Mobile.Services;
using AndroidApp = Android.App.Application;

namespace PoTraffic.Mobile;

/// <summary>
/// Commute start/end detection with the platform's own LocationManager proximity alerts — no
/// Play Services dependency. In the background Android checks them a few times an hour, so a
/// "you left" can trail the actual departure by some minutes.
/// </summary>
public sealed class AndroidGeofences : IGeofenceRegistrar
{
    private const string ExtraRoute = "route", ExtraOrigin = "origin";

    private static Context Ctx => AndroidApp.Context;

    public async Task<bool> RequestPermissionAsync()
    {
        // Foreground first: Android 11+ only offers "Allow all the time" after it.
        if (await Permissions.RequestAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted)
        {
            return false;
        }

        return await Permissions.RequestAsync<Permissions.LocationAlways>() == PermissionStatus.Granted;
    }

    public void Register(CommuteFence fence)
    {
        var manager = (LocationManager)Ctx.GetSystemService(Context.LocationService)!;
        try
        {
            manager.AddProximityAlert(fence.OriginLat, fence.OriginLng, (float)CommuteFences.RadiusMetres, -1, Pending(fence.RouteId, origin: true, PendingIntentFlags.UpdateCurrent)!);
            if (fence.StopOnArrival)
            {
                manager.AddProximityAlert(fence.DestLat, fence.DestLng, (float)CommuteFences.RadiusMetres, -1, Pending(fence.RouteId, origin: false, PendingIntentFlags.UpdateCurrent)!);
            }
        }
        catch (Java.Lang.SecurityException)
        {
            // Permission was withdrawn in Settings; the route page will ask again.
        }
    }

    public void Unregister(string routeId)
    {
        var manager = (LocationManager)Ctx.GetSystemService(Context.LocationService)!;
        foreach (bool origin in new[] { true, false })
        {
            if (Pending(routeId, origin, PendingIntentFlags.NoCreate) is { } pending)
            {
                manager.RemoveProximityAlert(pending);
                pending.Cancel();
            }
        }
    }

    // Mutable: the system adds KEY_PROXIMITY_ENTERING to the intent when it fires.
    private static PendingIntent? Pending(string routeId, bool origin, PendingIntentFlags flags)
    {
        var intent = new Intent(Ctx, typeof(GeofenceReceiver)).PutExtra(ExtraRoute, routeId).PutExtra(ExtraOrigin, origin);
        int code = (routeId.GetDeterministicHash() & 0x3FFFFFFF) * 2 + (origin ? 0 : 1);
        return PendingIntent.GetBroadcast(Ctx, code, intent, OperatingSystem.IsAndroidVersionAtLeast(31) ? flags | PendingIntentFlags.Mutable : flags);
    }

    internal static void OnCrossing(Context context, Intent intent, BroadcastReceiver receiver)
    {
        string? routeId = intent.GetStringExtra(ExtraRoute);
        if (routeId is null || IPlatformApplication.Current?.Services is not { } services)
        {
            return;
        }

        bool origin = intent.GetBooleanExtra(ExtraOrigin, true);
        bool entering = intent.GetBooleanExtra(LocationManager.KeyProximityEntering, false);

        // goAsync keeps the process alive for the API calls after OnReceive returns.
        BroadcastReceiver.PendingResult? pending = receiver.GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await CommuteFences.OnCrossingAsync(services, routeId, origin, entering);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}

[BroadcastReceiver(Exported = false)]
public sealed class GeofenceReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null && intent is not null)
        {
            AndroidGeofences.OnCrossing(context, intent, this);
        }
    }
}

internal static class StringHash
{
    /// <summary>string.GetHashCode is randomised per process; request codes must survive restarts.</summary>
    public static int GetDeterministicHash(this string s)
    {
        unchecked
        {
            int hash = 23;
            foreach (char c in s)
            {
                hash = hash * 31 + c;
            }

            return hash;
        }
    }
}
