using PoTraffic.Mobile.Services;

namespace PoTraffic.Mobile.Pages;

public partial class AlertsPage : ContentPage
{
    private readonly PoTrafficMobileClient _client;

    public AlertsPage(PoTrafficMobileClient client)
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

    private async void OnMarkReadClicked(object? sender, EventArgs e)
    {
        try
        {
            await _client.MarkAllAlertsReadAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private async Task RefreshAsync()
    {
        AlertsList.Clear();
        try
        {
            var alerts = await _client.GetAlertsAsync();
            StatusLabel.Text = $"Alerts: {alerts.Count} · Network: {Connectivity.Current.NetworkAccess}";
            foreach (var alert in alerts)
            {
                AlertsList.Add(new Label
                {
                    Text = $"[{alert.Kind}] {alert.Message} ({alert.TravelSeconds / 60}m vs {alert.BaselineSeconds / 60}m baseline)",
                    Opacity = alert.IsRead ? 0.6 : 1.0,
                });
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
    }
}
