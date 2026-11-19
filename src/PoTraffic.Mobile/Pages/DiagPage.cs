using PoTraffic.Mobile.Services;
using PoTraffic.Shared.DTOs.Admin;

namespace PoTraffic.Mobile.Pages;

/// <summary>System status (the web /diag page): one card per dependency, grouped API / Data / Services.</summary>
public sealed class DiagPage : ContentPage
{
    private readonly ApiClient _api;
    private readonly MobileConfig _config;
    private readonly VerticalStackLayout _body = new() { Spacing = 10, Padding = 16 };

    public DiagPage(ApiClient api, MobileConfig config)
    {
        _api = api;
        _config = config;
        Title = "System status";
        ToolbarItems.Add(new ToolbarItem("Refresh", null, async () => await LoadAsync()));
        Content = new ScrollView { Content = _body };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _body.Clear();
        _body.Add(Ui.H1("System status"));
        _body.Add(Ui.Muted(_config.BaseUrl));
        DateTime started = DateTime.UtcNow;
        try
        {
            DiagReportDto? report = await _api.GetDiagAsync();
            double ms = (DateTime.UtcNow - started).TotalMilliseconds;
            if (report is null)
            {
                _body.Add(Ui.Colored("The server returned no status.", Ui.Bad));
                return;
            }

            _body.Add(Ui.Colored($"{report.Status} · {report.Environment} · API answered in {ms:F0} ms", Badge(report.Status), 16));
            foreach (IGrouping<string, DiagCheckDto> group in report.Checks.GroupBy(c => c.Category))
            {
                _body.Add(Ui.H2(group.Key));
                foreach (DiagCheckDto check in group)
                {
                    _body.Add(Ui.Card(Ui.Stack(3,
                        Ui.Field(check.Name, Ui.Colored(check.Status, Badge(check.Status), 14)),
                        Ui.Muted($"{check.Purpose} · {check.LatencyMs:F0} ms"),
                        Ui.Muted(check.Detail))));
                }
            }

            _body.Add(Ui.Muted($"Checked {report.CheckedAtUtc.LocalDateTime:T} · app {AppInfo.Current.VersionString}"));
        }
        catch (HttpRequestException ex)
        {
            _body.Add(Ui.Colored($"Can't reach the API: {ex.Message}", Ui.Bad));
        }
    }

    private static Color Badge(string status) => status.ToLowerInvariant() switch
    {
        "healthy" or "ok" => Ui.Good,
        "degraded" or "warn" or "warning" => Ui.Warn,
        _ => Ui.Bad,
    };
}
