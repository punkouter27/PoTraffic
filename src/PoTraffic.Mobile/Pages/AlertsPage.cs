using PoTraffic.Mobile.Services;
using PoTraffic.Shared.DTOs.Alerts;

namespace PoTraffic.Mobile.Pages;

/// <summary>Congestion and reroute alerts (the web's notification bell), newest first, kept for offline.</summary>
public sealed class AlertsPage : ContentPage
{
    private readonly ApiClient _api;
    private readonly OfflineStore _store;
    private readonly VerticalStackLayout _body = new() { Spacing = 10, Padding = 16 };

    public AlertsPage(ApiClient api, OfflineStore store)
    {
        _api = api;
        _store = store;
        Title = "Alerts";
        ToolbarItems.Add(new ToolbarItem("Mark all read", null, async () =>
        {
            try
            {
                using HttpResponseMessage _ = await _api.MarkAllAlertsReadAsync();
            }
            catch (HttpRequestException)
            {
            }

            await LoadAsync();
        }));
        RefreshView refresh = new() { Content = new ScrollView { Content = _body } };
        refresh.Command = new Command(async () => { await LoadAsync(); refresh.IsRefreshing = false; });
        Content = refresh;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        List<AlertDto> alerts;
        bool offline = false;
        try
        {
            alerts = await _api.GetAlertsAsync() ?? [];
            await _store.SaveAsync("alerts", alerts, MobileJsonContext.Default.ListAlertDto);
        }
        catch (HttpRequestException)
        {
            alerts = await _store.LoadAsync("alerts", MobileJsonContext.Default.ListAlertDto) ?? [];
            offline = true;
        }

        _body.Clear();
        _body.Add(Ui.H1("Alerts"));
        if (offline)
        {
            _body.Add(Ui.Colored("Offline — showing saved alerts.", Ui.Warn));
        }

        if (alerts.Count == 0)
        {
            _body.Add(Ui.Muted("No alerts. PoTraffic flags a commute here when it runs well over its usual time or reroutes."));
        }

        foreach (AlertDto alert in alerts.OrderByDescending(a => a.CreatedAt))
        {
            int over = Format.Minutes(alert.TravelSeconds - alert.BaselineSeconds);
            VerticalStackLayout content = Ui.Stack(3,
                new Label { Text = alert.Message, FontAttributes = alert.IsRead ? FontAttributes.None : FontAttributes.Bold, LineBreakMode = LineBreakMode.WordWrap },
                Ui.Muted($"{alert.Kind} · {Format.Minutes(alert.TravelSeconds)} min vs usual {Format.Minutes(alert.BaselineSeconds)}{(over > 0 ? $" (+{over})" : string.Empty)} · {alert.CreatedAt.LocalDateTime:MMM d, h:mm tt}"));
            _body.Add(Ui.Card(content, async () =>
            {
                try
                {
                    using HttpResponseMessage _ = await _api.MarkAlertReadAsync(alert.Id);
                }
                catch (HttpRequestException)
                {
                }

                await Shell.Current.GoToAsync($"route?id={alert.RouteId.Value}");
            }, alert.IsRead ? null : Ui.Warn));
        }
    }
}
