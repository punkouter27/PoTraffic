using PoTraffic.Mobile.Services;

namespace PoTraffic.Mobile.Pages;

public partial class CommutePage : ContentPage
{
    private readonly PoTrafficMobileClient _client;

    public CommutePage(PoTrafficMobileClient client)
    {
        _client = client;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await RefreshAsync();

    private async void OnCheckGpsClicked(object? sender, EventArgs e)
    {
        try
        {
            var loc = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
            GpsLabel.Text = loc is not null
                ? $"GPS: {loc.Latitude:F4}, {loc.Longitude:F4} (ready for geofence check)"
                : "GPS: unavailable";
        }
        catch (Exception ex)
        {
            GpsLabel.Text = $"GPS error: {ex.Message}";
        }
    }

    private async Task RefreshAsync()
    {
        RoutesList.Clear();
        try
        {
            var routes = await _client.GetRoutesAsync();
            StatusLabel.Text = $"Routes: {routes.Count} · Network: {Connectivity.Current.NetworkAccess}";
            foreach (var route in routes)
            {
                var card = new Border { Padding = 12, StrokeThickness = 1 };
                var stack = new VerticalStackLayout { Spacing = 6 };
                stack.Add(new Label { Text = route.Name ?? $"{route.OriginAddress} → {route.DestinationAddress}", FontAttributes = FontAttributes.Bold });
                stack.Add(new Label { Text = $"Status: {route.MonitoringStatus} · Arrive by: {route.ArriveBy ?? "none"}" });
                var actions = new HorizontalStackLayout { Spacing = 8 };
                var checkBtn = new Button { Text = "Check Now" };
                checkBtn.Clicked += async (_, _) =>
                {
                    try
                    {
                        var now = await _client.CheckNowAsync(route.Id);
                        StatusLabel.Text = now is not null
                            ? $"Live ETA: {now.DurationSeconds / 60} min ({now.DistanceMetres / 1000.0:F1} km)"
                            : "Checked.";
                    }
                    catch (Exception ex)
                    {
                        StatusLabel.Text = $"Check failed: {ex.Message}";
                    }
                };
                var navBtn = new Button { Text = "Open Maps" };
                navBtn.Clicked += async (_, _) =>
                {
                    await Map.Default.OpenAsync(new Placemark { FeatureName = route.DestinationAddress });
                };
                actions.Add(checkBtn);
                actions.Add(navBtn);
                stack.Add(actions);
                card.Content = stack;
                RoutesList.Add(card);
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
    }
}
