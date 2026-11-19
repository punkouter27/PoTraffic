using PoTraffic.Mobile.Services;

namespace PoTraffic.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new AppShell());

    /// <summary>potraffic://route/{id} (a tapped leave-by or geofence notification): open that commute.</summary>
    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);
        string id = uri.AbsolutePath.Trim('/');
        if (uri.Scheme != MobileConfig.AppScheme || uri.Host != "route" || !Guid.TryParse(id, out Guid routeId))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            for (int i = 0; Shell.Current is null && i < 50; i++)
            {
                await Task.Delay(100);
            }

            if (Shell.Current is { } shell)
            {
                await shell.GoToAsync($"//commutes/route?id={routeId}");
            }
        });
    }
}
