using CoreLocation;
using PoTraffic.Mobile.Services;
using UIKit;

namespace PoTraffic.Mobile;

/// <summary>
/// Commute start/end detection with CoreLocation region monitoring. iOS relaunches the app in the
/// background on a crossing, so the manager is created at launch (see MauiProgram) and listens
/// from then on; a background task buys time for the API calls.
/// </summary>
public sealed class IosGeofences : IGeofenceRegistrar
{
    private const char Separator = '|';
    private static CLLocationManager? s_manager;

    /// <summary>Called at app launch so region events delivered to a relaunched app are heard.</summary>
    public static void Listen()
    {
        if (s_manager is not null)
        {
            return;
        }

        s_manager = new CLLocationManager();
        s_manager.RegionLeft += (_, e) => OnCrossing(e.Region.Identifier, entering: false);
        s_manager.RegionEntered += (_, e) => OnCrossing(e.Region.Identifier, entering: true);
    }

    public async Task<bool> RequestPermissionAsync()
    {
        if (await Permissions.RequestAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted)
        {
            return false;
        }

        return await Permissions.RequestAsync<Permissions.LocationAlways>() == PermissionStatus.Granted;
    }

    public void Register(CommuteFence fence)
    {
        Listen();
        s_manager!.StartMonitoring(new CLCircularRegion(new CLLocationCoordinate2D(fence.OriginLat, fence.OriginLng), CommuteFences.RadiusMetres, $"{fence.RouteId}{Separator}o")
        {
            NotifyOnExit = true,
            NotifyOnEntry = false,
        });

        if (fence.StopOnArrival)
        {
            s_manager.StartMonitoring(new CLCircularRegion(new CLLocationCoordinate2D(fence.DestLat, fence.DestLng), CommuteFences.RadiusMetres, $"{fence.RouteId}{Separator}d")
            {
                NotifyOnExit = false,
                NotifyOnEntry = true,
            });
        }
    }

    public void Unregister(string routeId)
    {
        Listen();
        foreach (CLRegion region in s_manager!.MonitoredRegions.OfType<CLRegion>().Where(r => r.Identifier.StartsWith(routeId + Separator, StringComparison.Ordinal)).ToList())
        {
            s_manager.StopMonitoring(region);
        }
    }

    private static void OnCrossing(string identifier, bool entering)
    {
        string[] parts = identifier.Split(Separator);
        if (parts.Length != 2 || IPlatformApplication.Current?.Services is not { } services)
        {
            return;
        }

        nint task = UIApplication.SharedApplication.BeginBackgroundTask("commute-crossing", () => { });
        _ = Task.Run(async () =>
        {
            try
            {
                await CommuteFences.OnCrossingAsync(services, parts[0], atOrigin: parts[1] == "o", entering);
            }
            finally
            {
                UIApplication.SharedApplication.EndBackgroundTask(task);
            }
        });
    }
}
