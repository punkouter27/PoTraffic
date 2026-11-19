using System.Net;
using PoTraffic.Mobile.Services;
using PoTraffic.Shared.DTOs;
using PoTraffic.Shared.DTOs.Account;
using PoTraffic.Shared.DTOs.History;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.Enums;
using PoTraffic.Shared.Ids;

namespace PoTraffic.Mobile.Pages;

/// <summary>"Your commutes": one card per route, and the card is the answer (when to leave / how long).</summary>
public sealed class DashboardPage : ContentPage
{
    private const int ListToolsThreshold = 6;
    private static readonly TimeSpan SnapshotMaxAge = TimeSpan.FromHours(12);

    private readonly ApiClient _api;
    private readonly Session _session;
    private readonly OfflineStore _store;
    private readonly LeaveAlerts _alerts;
    private readonly RefreshView _refresh = new();
    private readonly VerticalStackLayout _body = new() { Spacing = 10, Padding = new Thickness(16, 8, 16, 24) };
    private IDispatcherTimer? _timer;

    private List<RouteDto> _routes = [];
    private Dictionary<string, OptimalDepartureDto> _departures = [];
    private Dictionary<string, DeparturePlanDto> _plans = [];
    private Dictionary<string, List<PollRecordDto>> _polls = [];
    private QuotaDto? _quota;
    private bool _loaded, _failed, _fromCache;
    private DateTimeOffset _updated;

    private string _search = string.Empty;
    private string _filter = "All";
    private string _sort = "Added";
    private bool _selecting;
    private readonly HashSet<RouteId> _selected = [];

    public DashboardPage(ApiClient api, Session session, OfflineStore store, LeaveAlerts alerts)
    {
        _api = api;
        _session = session;
        _store = store;
        _alerts = alerts;
        Title = "Commutes";
        ToolbarItems.Add(new ToolbarItem("Add", null, async () => await Shell.Current.GoToAsync("create")));
        _refresh.Content = new ScrollView { Content = _body };
        _refresh.Command = new Command(async () => { await LoadAsync(); _refresh.IsRefreshing = false; });
        Content = _refresh;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await _session.RefreshAsync())
        {
            await Shell.Current.GoToAsync("login");
            return;
        }

        if (!_loaded && await _store.LoadAsync("dashboard", MobileJsonContext.Default.DashboardSnapshot) is { } snap
            && DateTimeOffset.UtcNow - snap.SavedAt < SnapshotMaxAge)
        {
            Apply(snap);
            _fromCache = true;
            Render();
        }

        await LoadAsync();
        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(60);
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }

    private async void OnTick(object? sender, EventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            Task<PagedResult<RouteDto>?> routesTask = _api.GetRoutesAsync();
            Task<QuotaDto?> quotaTask = _api.GetQuotaAsync();
            List<RouteDto> routes = [.. (await routesTask)?.Items ?? []];
            QuotaDto? quota = await quotaTask;

            Dictionary<string, OptimalDepartureDto> departures = [];
            Dictionary<string, DeparturePlanDto> plans = [];
            Dictionary<string, List<PollRecordDto>> polls = [];
            DateTime since = DateTime.UtcNow.AddHours(-24);

            // Every route's pair goes out at once; one route failing never blanks the rest.
            await Task.WhenAll(routes.Where(r => r.IsSample || r.Windows.Any(w => w.IsActive)).Select(async r =>
            {
                try
                {
                    Task<DepartureDto?> dep = _api.GetDepartureAsync(r.Id, DateTime.Today.DayOfWeek);
                    Task<PagedResult<PollRecordDto>?> hist = _api.GetPollHistoryAsync(r.Id, 60, since);
                    string key = r.Id.Value.ToString();
                    if (await dep is { } d)
                    {
                        lock (departures)
                        {
                            if (d.Best is { } best)
                            {
                                departures[key] = best;
                            }

                            if (d.Plan is { } plan)
                            {
                                plans[key] = plan;
                            }
                        }
                    }

                    if ((await hist)?.Items is { Count: > 0 } items)
                    {
                        lock (polls)
                        {
                            polls[key] = [.. items];
                        }
                    }
                }
                catch (HttpRequestException)
                {
                }
            }));

            Apply(new DashboardSnapshot(routes, quota, departures, plans, polls, DateTimeOffset.UtcNow));
            _fromCache = false;
            _failed = false;
            await _store.SaveAsync("dashboard", new DashboardSnapshot(routes, quota, departures, plans, polls, DateTimeOffset.UtcNow), MobileJsonContext.Default.DashboardSnapshot);
            _ = _alerts.RearmAsync(routes);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await Shell.Current.GoToAsync("login");
            return;
        }
        catch (HttpRequestException)
        {
            _failed = true;
        }

        Render();
    }

    private void Apply(DashboardSnapshot s)
    {
        _routes = s.Routes;
        _quota = s.Quota;
        _departures = s.Departures;
        _plans = s.Plans;
        _polls = s.RecentPolls;
        _updated = s.SavedAt;
        _loaded = true;
    }

    private static bool IsMonitoring(RouteDto r) => r.MonitoringStatus == MonitoringStatus.Active || r.IsSample;

    private IEnumerable<RouteDto> Shown()
    {
        string q = _search.Trim();
        IEnumerable<RouteDto> list = _routes.Where(r =>
            (_filter == "All" || (_filter == "Monitoring") == IsMonitoring(r))
            && (q.Length == 0
                || (r.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || r.OriginAddress.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.DestinationAddress.Contains(q, StringComparison.OrdinalIgnoreCase)));

        return _sort switch
        {
            "Name" => list.OrderBy(r => r.Name ?? $"{r.OriginAddress} {r.DestinationAddress}", StringComparer.CurrentCultureIgnoreCase),
            "Newest" => list.OrderByDescending(r => r.CreatedAt),
            "Quickest" => list.OrderBy(r => _departures.TryGetValue(r.Id.Value.ToString(), out var o) ? o.PredictedDurationSeconds : double.MaxValue),
            _ => list,
        };
    }

    private void Render()
    {
        _body.Clear();
        int active = _routes.Count(IsMonitoring);
        _body.Add(Ui.H1("Your commutes"));
        if (_routes.Count > 0)
        {
            _body.Add(Ui.Muted($"{active} of {_routes.Count} monitoring · updated {Format.Clock(_updated.LocalDateTime)}"));
        }

        if (_failed || _fromCache)
        {
            _body.Add(Ui.Colored(ApiClient.Online ? "Showing saved data — the server didn't answer. Pull down to retry." : "You're offline — showing your last saved commutes.", Ui.Warn));
        }

        if (_quota is { } quota && quota.UsedToday >= quota.DailyLimit - 2)
        {
            _body.Add(Ui.Colored("Daily monitoring limit almost reached — sampling will pause once it's hit.", Ui.Warn));
        }

        if (!_loaded)
        {
            _body.Add(_failed
                ? Ui.Stack(8, Ui.H2("Couldn't load your commutes"), Ui.Muted(ApiClient.Online ? "The server didn't respond." : "You're offline."), Ui.Secondary("Retry now", LoadAsync))
                : new ActivityIndicator { IsRunning = true });
            return;
        }

        if (_routes.Count == 0)
        {
            _body.Add(Ui.Card(Ui.Stack(10,
                Ui.H2("No commutes yet"),
                Ui.Muted("Add the trip you make most. PoTraffic times it on a schedule and learns when to leave."),
                Ui.Primary("Add your first commute", () => Shell.Current.GoToAsync("create")),
                Ui.Secondary("Explore a sample commute", CreateSampleAsync))));
            return;
        }

        if (_routes.Count >= ListToolsThreshold || _search.Length > 0 || _filter != "All")
        {
            _body.Add(Toolbar());
        }

        List<RouteDto> shown = [.. Shown()];
        if (shown.Count == 0)
        {
            _body.Add(Ui.Stack(8, Ui.Muted("No commutes match."), Ui.Secondary("Clear search and filter", () => { _search = string.Empty; _filter = "All"; Render(); return Task.CompletedTask; })));
        }

        if (_selecting)
        {
            _body.Add(BulkBar(shown));
        }

        foreach (RouteDto route in shown)
        {
            _body.Add(RouteCard(route));
        }
    }

    private View Toolbar()
    {
        SearchBar search = new() { Placeholder = "Search commutes", Text = _search };
        search.SearchButtonPressed += (_, _) => { _search = search.Text ?? string.Empty; Render(); };
        search.TextChanged += (_, e) => { if (string.IsNullOrEmpty(e.NewTextValue) && _search.Length > 0) { _search = string.Empty; Render(); } };

        Picker filter = new() { Title = "Show", ItemsSource = new[] { "All", "Monitoring", "Paused" }, SelectedItem = _filter };
        filter.SelectedIndexChanged += (_, _) => { _filter = (string)filter.SelectedItem; Render(); };
        Picker sort = new() { Title = "Sort", ItemsSource = new[] { "Added", "Newest", "Name", "Quickest" }, SelectedItem = _sort };
        sort.SelectedIndexChanged += (_, _) => { _sort = (string)sort.SelectedItem; Render(); };
        Button select = new() { Text = _selecting ? "Done" : "Select" };
        select.Clicked += (_, _) => { _selecting = !_selecting; _selected.Clear(); Render(); };
        return Ui.Stack(4, search, Ui.Row(filter, sort, select));
    }

    private View BulkBar(List<RouteDto> shown)
    {
        List<RouteDto> chosen() => [.. shown.Where(r => _selected.Contains(r.Id))];
        return Ui.Card(Ui.Stack(8,
            Ui.Muted($"{_selected.Count} selected"),
            Ui.Row(
                Ui.Secondary("All", () => { foreach (var r in shown) { _selected.Add(r.Id); } Render(); return Task.CompletedTask; }),
                Ui.Secondary("Pause", () => BulkAsync(chosen(), r => _api.SetPausedAsync(r.Id, true))),
                Ui.Secondary("Resume", () => BulkAsync(chosen(), r => _api.SetPausedAsync(r.Id, false))),
                Ui.Danger("Delete", async () =>
                {
                    if (await DisplayAlertAsync("Delete commutes", $"Delete {_selected.Count} commute(s) and their history?", "Delete", "Keep them"))
                    {
                        await BulkAsync(chosen(), r => _api.DeleteRouteAsync(r.Id));
                    }
                }))));
    }

    private async Task BulkAsync(List<RouteDto> routes, Func<RouteDto, Task<HttpResponseMessage>> action)
    {
        int failed = 0;
        foreach (RouteDto route in routes)
        {
            try
            {
                using HttpResponseMessage resp = await action(route);
                failed += resp.IsSuccessStatusCode ? 0 : 1;
            }
            catch (HttpRequestException)
            {
                failed++;
            }
        }

        _selected.Clear();
        _selecting = false;
        if (failed > 0)
        {
            await Ui.Toast($"{failed} of {routes.Count} didn't go through.");
        }

        await LoadAsync();
    }

    private View RouteCard(RouteDto route)
    {
        string key = route.Id.Value.ToString();
        string answer = _plans.TryGetValue(key, out var plan) && plan.LeaveBy is { } leaveBy
            ? $"Leave by {Format.WallClock(leaveBy)}"
            : _departures.TryGetValue(key, out var best)
                ? best is { LowerBound: { } lo, UpperBound: { } hi }
                    ? $"{Format.Minutes(best.PredictedDurationSeconds)} min · {Format.Minutes(lo)}–{Format.Minutes(hi)} min"
                    : $"{Format.Minutes(best.PredictedDurationSeconds)} min"
                : Format.SamplesSoFar(_polls.GetValueOrDefault(key)?.Count ?? 0);

        Color stripe = IsMonitoring(route) ? (_departures.ContainsKey(key) ? Ui.Good : Ui.Warn) : Colors.Gray;
        VerticalStackLayout content = new() { Spacing = 4 };
        if (!string.IsNullOrEmpty(route.Name))
        {
            content.Add(new Label { Text = route.Name, FontAttributes = FontAttributes.Bold, FontSize = 16 });
        }

        content.Add(Ui.Muted($"{Format.Street(route.OriginAddress)} → {Format.Street(route.DestinationAddress)}{(route.IsSample ? "  · Sample" : string.Empty)}{(IsMonitoring(route) ? string.Empty : "  · Paused")}"));
        content.Add(new Label { Text = answer, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = _departures.ContainsKey(key) || plan is not null ? null : Colors.Gray });
        if (_session.PanelOn(AdvancedPanel.Sparklines) && _polls.TryGetValue(key, out var recent) && recent.Count > 1)
        {
            content.Add(LineChart.View(recent, 36, axes: false));
        }

        View inner = content;
        if (_selecting)
        {
            CheckBox box = new() { IsChecked = _selected.Contains(route.Id) };
            box.CheckedChanged += (_, e) => { if (e.Value) { _selected.Add(route.Id); } else { _selected.Remove(route.Id); } };
            Grid grid = new() { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
            grid.Add(box);
            grid.Add(content, 1);
            inner = grid;
        }

        Border card = Ui.Card(inner, _selecting ? null : () => Shell.Current.GoToAsync($"route?id={route.Id.Value}"), stripe);
        SemanticProperties.SetDescription(card, $"{Format.Street(route.OriginAddress)} to {Format.Street(route.DestinationAddress)}. {answer}");

        // Swipe left: check now. Swipe right: delete. Shortcuts to the route page's buttons.
        SwipeView swipe = new() { Content = card };
        swipe.LeftItems = [new SwipeItem { Text = "Delete", BackgroundColor = Ui.Bad, Command = new Command(async () => await DeleteAsync(route)) }];
        swipe.RightItems = [new SwipeItem { Text = "Check now", BackgroundColor = Ui.Accent, Command = new Command(async () => await CheckNowAsync(route)) }];
        return swipe;
    }

    private async Task CheckNowAsync(RouteDto route)
    {
        try
        {
            (CheckNowResponse? result, HttpStatusCode status) = await _api.CheckNowAsync(route.Id);
            await Ui.Toast(result is { } r
                ? $"{Format.Minutes(r.DurationSeconds)} min right now ({Format.Distance(r.DistanceMetres)})."
                : status == HttpStatusCode.TooManyRequests ? "Too many live checks — try again in a minute." : "Live check failed.");
            await LoadAsync();
        }
        catch (HttpRequestException)
        {
            await Ui.Toast("Live checks need a connection.");
        }
    }

    private async Task DeleteAsync(RouteDto route)
    {
        if (!await DisplayAlertAsync("Delete commute", $"Delete {CommuteFences.Label(route)} and its history?", "Delete", "Keep it"))
        {
            return;
        }

        try
        {
            using HttpResponseMessage resp = await _api.DeleteRouteAsync(route.Id);
            if (!resp.IsSuccessStatusCode)
            {
                await Ui.Toast("Delete failed — the commute is still there.");
            }
        }
        catch (HttpRequestException)
        {
            await Ui.Toast("Deleting needs a connection.");
        }

        await LoadAsync();
    }

    private async Task CreateSampleAsync()
    {
        try
        {
            using HttpResponseMessage resp = await _api.CreateSampleRouteAsync();
            if (!resp.IsSuccessStatusCode)
            {
                await Ui.Toast((await ApiClient.ReadProblemAsync(resp, "Couldn't create the sample.")).Message);
            }
        }
        catch (HttpRequestException)
        {
            await Ui.Toast("That needs a connection.");
        }

        await LoadAsync();
    }
}
