using System.Net;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using PoTraffic.Mobile.Services;
using PoTraffic.Shared.DTOs;
using PoTraffic.Shared.DTOs.History;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.Enums;
using PoTraffic.Shared.Ids;
using Map = Microsoft.Maui.Controls.Maps.Map;

namespace PoTraffic.Mobile.Pages;

/// <summary>One commute: the answer, today's samples, the map, schedule, heatmap, weather, and every edit.</summary>
public sealed class RouteDetailPage : ContentPage, IQueryAttributable
{
    private const string Overview = "Overview", Schedule = "Schedule", Heatmap = "Heatmap", Weather = "Weather";

    private readonly ApiClient _api;
    private readonly Session _session;
    private readonly LeaveAlerts _leaveAlerts;
    private readonly CommuteFences _fences;
    private readonly VerticalStackLayout _body = new() { Spacing = 10, Padding = new Thickness(16, 8, 16, 24) };
    private IDispatcherTimer? _timer;

    private RouteId _id;
    private string _tab = Overview;
    private RouteDto? _route;
    private bool _notFound;
    private List<PollRecordDto> _today = [];
    private PollRecordDto? _latest;
    private DeparturePlanDto? _plan;
    private SessionDto? _activeSession;
    private RoutePathDto? _path;
    private VolatilityHeatmapDto? _heatmap;
    private WeatherImpactResponse? _weather;
    private string? _message;

    public RouteDetailPage(ApiClient api, Session session, LeaveAlerts leaveAlerts, CommuteFences fences)
    {
        _api = api;
        _session = session;
        _leaveAlerts = leaveAlerts;
        _fences = fences;
        Title = "Commute";
        RefreshView refresh = new() { Content = new ScrollView { Content = _body } };
        refresh.Command = new Command(async () => { await LoadAsync(); refresh.IsRefreshing = false; });
        Content = refresh;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out object? raw) && Guid.TryParse(raw?.ToString(), out Guid id))
        {
            _id = RouteId.From(id);
        }

        if (query.TryGetValue("tab", out object? tab) && tab is string t)
        {
            _tab = t;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(30);
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
            Task<RouteDto?> routeTask = _api.GetRouteAsync(_id);
            Task<List<SessionDto>?> sessionsTask = _api.GetSessionsAsync(_id);
            Task<PagedResult<PollRecordDto>?> historyTask = _api.GetPollHistoryAsync(_id, 300, DateTime.UtcNow.AddHours(-24));
            Task<DepartureDto?> planTask = _api.GetDepartureAsync(_id, DateTime.Today.DayOfWeek);
            Task<RoutePathDto?> pathTask = _session.PanelOn(AdvancedPanel.Map) ? _api.GetPathAsync(_id) : Task.FromResult<RoutePathDto?>(null);
            Task<VolatilityHeatmapDto?> heatTask = _tab == Heatmap ? _api.GetHeatmapAsync(_id) : Task.FromResult(_heatmap);
            Task<WeatherImpactResponse?> weatherTask = _tab == Weather ? _api.GetWeatherImpactAsync(_id) : Task.FromResult(_weather);
            await Task.WhenAll(routeTask, sessionsTask, historyTask, planTask, pathTask, heatTask, weatherTask);

            _route = await routeTask;
            _notFound = _route is null;
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            _activeSession = (await sessionsTask)?.FirstOrDefault(s => s.State == SessionState.Active && s.SessionDate == today);
            List<PollRecordDto> recent = [.. ((await historyTask)?.Items ?? []).OrderBy(p => p.PolledAt)];
            _today = [.. recent.Where(p => p.PolledAt.LocalDateTime.Date == DateTime.Today)];
            _latest = recent.LastOrDefault();
            _plan = (await planTask)?.Plan;
            _path = await pathTask;
            _heatmap = await heatTask;
            _weather = await weatherTask;
        }
        catch (HttpRequestException)
        {
            _message = ApiClient.Online ? "The server didn't answer — showing what was loaded." : "You're offline.";
        }

        Render();
    }

    private string Origin => Format.Street(_route?.OriginAddress);

    private string Destination => Format.Street(_route?.DestinationAddress);

    private void Render()
    {
        _body.Clear();
        if (_notFound || _route is null)
        {
            _body.Add(_notFound ? Ui.Stack(8, Ui.H1("Commute not found"), Ui.Secondary("Back to commutes", () => Shell.Current.GoToAsync(".."))) : new ActivityIndicator { IsRunning = true });
            return;
        }

        Title = _route.Name ?? $"{Origin} → {Destination}";
        _body.Add(Ui.H1(Title));
        if (_message is not null)
        {
            _body.Add(Ui.Colored(_message, Ui.Warn));
            _message = null;
        }

        List<string> tabs = [Overview, .. _route.IsSample ? Array.Empty<string>() : [Schedule], Heatmap, .. _session.PanelOn(AdvancedPanel.Weather) ? [Weather] : Array.Empty<string>()];
        if (!tabs.Contains(_tab))
        {
            _tab = Overview;
        }

        _body.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = Ui.Tabs(tabs, _tab, async t => { _tab = t; Render(); if (t is Heatmap or Weather) { await LoadAsync(); } }) });

        switch (_tab)
        {
            case Schedule: RenderSchedule(); break;
            case Heatmap: RenderHeatmap(); break;
            case Weather: RenderWeather(); break;
            default: RenderOverview(); break;
        }
    }

    // ── Overview ──────────────────────────────────────────────────────────────
    private void RenderOverview()
    {
        RouteDto route = _route!;
        VerticalStackLayout answer = new() { Spacing = 4 };
        if (route.Name is not null)
        {
            answer.Add(Ui.Muted($"{Origin} → {Destination}"));
        }

        string range = _today.Count > 0 ? Format.MinuteRange(_today.Min(p => p.TravelDurationSeconds), _today.Max(p => p.TravelDurationSeconds)) + " today" : string.Empty;
        if (_plan is { LeaveBy: { } leaveBy } plan)
        {
            answer.Add(new Label { Text = $"Leave by {Format.WallClock(leaveBy)}", FontSize = 26, FontAttributes = FontAttributes.Bold });
            answer.Add(Ui.Muted($"On time on {plan.OnTimePercent}% of {plan.SampleCount} recorded trips — usually {Format.Minutes(plan.TypicalSeconds ?? 0)} min, up to {Format.Minutes(plan.WorstCaseSeconds ?? 0)} min.{(plan.DayOfWeekSpecific ? string.Empty : " Based on every day until today's history fills in.")}"));
            if (range.Length > 0)
            {
                answer.Add(Ui.Muted(range));
            }
        }
        else
        {
            answer.Add(new Label { Text = _today.Count == 0 ? "Still learning this commute" : range, FontSize = 22, FontAttributes = FontAttributes.Bold });
            if (_today.Count == 0)
            {
                answer.Add(Ui.Muted(Format.SamplesSoFar(0)));
            }
        }

        if (_plan is { LeaveBy: null })
        {
            answer.Add(Ui.Muted($"Not enough trips in the three hours before {Format.WallClock(_plan.ArriveBy)} to plan a departure yet — the monitoring window needs to cover when you'd leave."));
        }

        if (_today.LastOrDefault()?.HolidayName is { } holiday)
        {
            answer.Add(Ui.Muted($"Today is {holiday} — its trips are shown here but don't count toward your usual times."));
        }

        _body.Add(Ui.Card(answer));

        // Latest probe + live check.
        VerticalStackLayout probe = new() { Spacing = 4 };
        probe.Add(Ui.H2("Latest sample"));
        probe.Add(_latest is { } p
            ? Ui.Text($"{Format.Minutes(p.TravelDurationSeconds)} min · {Format.Distance(p.DistanceMetres)} · {Format.Clock(p.PolledAt.LocalDateTime)}{(p.IsRerouted ? " · rerouted" : string.Empty)}{(p.WeatherCondition is { } w ? $" · {w}" : string.Empty)}")
            : Ui.Muted("No samples yet."));
        if (_activeSession?.Schedule is { } sched)
        {
            probe.Add(Ui.Muted(sched.WindowIsOpenNow
                ? $"Sampling every {sched.PollIntervalMinutes} min{(sched.NextPollExpectedUtc is { } next ? $" · next ~{Format.Clock(next.LocalDateTime)}" : string.Empty)}"
                : sched.NextWindowOpenUtc is { } opens ? $"Next window opens {opens.LocalDateTime:ddd h:mm tt}" : "Window closed"));
        }

        if (!route.IsSample)
        {
            probe.Add(Ui.Secondary("Check now", CheckNowAsync));
        }

        _body.Add(Ui.Card(probe));

        if (_today.Count > 1)
        {
            _body.Add(Ui.Card(Ui.Stack(4, Ui.H2("Today"), LineChart.View(_today, 160, axes: true))));
        }

        if (_session.PanelOn(AdvancedPanel.Map) && _path is { } path && MapAvailable)
        {
            _body.Add(Ui.Card(Ui.Stack(4, Ui.H2("Route"), RouteMap(path),
                Ui.Muted($"Traffic: {path.TrafficLevel}{(path.LatestDurationSeconds is { } now ? $" · {Format.Minutes(now)} min now" : string.Empty)}{(path.TypicalDurationSeconds is { } typ ? $" · usually {Format.Minutes(typ)}" : string.Empty)}{(path.IsApproximate ? " · approximate line" : string.Empty)}"),
                path.Incident is { } inc ? Ui.Colored($"⚠ {inc.Description}", Ui.Warn) : new BoxView { HeightRequest = 0 })));
        }

        _body.Add(PhoneFeatures(route));
        _body.Add(Details(route));
    }

    private static bool MapAvailable =>
#if IOS || (ANDROID && HAS_MAPS_KEY)
        true;
#else
        false;
#endif

    private static Map RouteMap(RoutePathDto path)
    {
        Map map = new() { HeightRequest = 260, IsScrollEnabled = true };
        List<(double Lat, double Lng)> points = path.EncodedPolyline is { Length: > 0 } enc
            ? Format.DecodePolyline(enc)
            : [(path.OriginLat, path.OriginLng), (path.DestinationLat, path.DestinationLng)];
        Polyline line = new() { StrokeColor = Ui.Accent, StrokeWidth = 6 };
        foreach ((double lat, double lng) in points)
        {
            line.Geopath.Add(new Location(lat, lng));
        }

        map.MapElements.Add(line);
        map.Pins.Add(new Pin { Label = "Start", Location = new Location(path.OriginLat, path.OriginLng) });
        map.Pins.Add(new Pin { Label = "End", Location = new Location(path.DestinationLat, path.DestinationLng) });
        if (path.Incident is { } inc)
        {
            map.Pins.Add(new Pin { Label = "Incident", Address = inc.Description, Location = new Location(inc.Lat, inc.Lng) });
        }

        Location center = new((path.OriginLat + path.DestinationLat) / 2, (path.OriginLng + path.DestinationLng) / 2);
        double km = Location.CalculateDistance(path.OriginLat, path.OriginLng, path.DestinationLat, path.DestinationLng, DistanceUnits.Kilometers);
        map.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(Math.Max(1, km * 0.7))));
        return map;
    }

    /// <summary>The two phone-only features: leave-by alerts (item 2) and leaving-home detection (item 3).</summary>
    private View PhoneFeatures(RouteDto route)
    {
        VerticalStackLayout box = new() { Spacing = 6 };
        box.Add(Ui.H2("On this phone"));

        bool alertOn = LeaveAlerts.IsOn(route.Id);
        box.Add(Ui.Field($"Alert me {LeaveAlerts.LeadMinutes} min before I need to leave", Ui.Toggle(alertOn, async on =>
        {
            if (on && route.ArriveBy is null)
            {
                await Ui.Toast("Set an arrive-by time first — the alert is \"leave by\" for that time.");
                Render();
                return;
            }

            if (!await _leaveAlerts.SetAsync(route, on))
            {
                await Ui.Toast("Notifications are off for PoTraffic. Allow them in Settings to get leave-by alerts.");
                Render();
            }
        })));
        box.Add(Ui.Muted("Fires even when the app is closed, using this commute's leave-by plan for each scheduled day."));

        CommuteFence? fence = CommuteFences.For(route.Id);
        Switch stopToggle = Ui.Toggle(fence?.StopOnArrival ?? true, async stop =>
        {
            if (CommuteFences.For(route.Id) is not null)
            {
                await SetFenceAsync(route, true, stop);
            }
        });
        box.Add(Ui.Field($"Start timing when I leave {Origin}", Ui.Toggle(fence is not null, async on => await SetFenceAsync(route, on, stopToggle.IsToggled))));
        box.Add(Ui.Field($"…and stop when I reach {Destination}", stopToggle));
        box.Add(Ui.Muted("Uses your location in the background (\"Allow all the time\"). Android may take a few minutes to notice."));
        return Ui.Card(box);
    }

    private async Task SetFenceAsync(RouteDto route, bool on, bool stopOnArrival)
    {
        if (await _fences.SetAsync(route, on, stopOnArrival) is { } problem)
        {
            await Ui.Toast(problem);
            Render();
        }
    }

    private View Details(RouteDto route)
    {
        VerticalStackLayout box = new() { Spacing = 8 };
        box.Add(Ui.H2("Details"));

        Entry name = new() { Text = route.Name, Placeholder = "Name (e.g. To work)" };
        box.Add(Ui.Text("Name"));
        box.Add(name);
        box.Add(Ui.Secondary("Save name", () => Send(_api.SetNameAsync(route.Id, string.IsNullOrWhiteSpace(name.Text) ? null : name.Text.Trim()), "Couldn't rename.")));

        Switch hasArrive = new() { IsToggled = route.ArriveBy is not null };
        TimePicker arrive = new() { Time = route.ArriveBy is { } a && TimeOnly.TryParse(a, out TimeOnly t) ? t.ToTimeSpan() : new TimeSpan(9, 0, 0) };
        box.Add(Ui.Field("Arrive by", hasArrive));
        box.Add(arrive);
        box.Add(Ui.Secondary("Save arrive-by", async () =>
        {
            await Send(_api.SetArriveByAsync(route.Id, hasArrive.IsToggled ? Format.HHmm(arrive.Time ?? TimeSpan.FromHours(9)) : null), "Couldn't save the arrive-by time.");
            if (LeaveAlerts.IsOn(route.Id) && await _api.GetRouteAsync(route.Id) is { } fresh)
            {
                await _leaveAlerts.RearmAsync([fresh], force: true);
            }
        }));

        if (!route.IsSample)
        {
            Entry from = new() { Text = route.OriginAddress };
            Entry to = new() { Text = route.DestinationAddress };
            box.Add(Ui.Text("Addresses"));
            box.Add(from);
            box.Add(to);
            box.Add(Ui.Secondary("Save addresses", async () =>
            {
                if (string.IsNullOrWhiteSpace(from.Text) || string.IsNullOrWhiteSpace(to.Text))
                {
                    await Ui.Toast("Enter both addresses.");
                    return;
                }

                await Send(_api.SetAddressesAsync(route.Id, from.Text.Trim(), to.Text.Trim()), "Couldn't change the addresses.");
            }));

            bool paused = route.MonitoringStatus == MonitoringStatus.Paused;
            box.Add(Ui.Secondary(paused ? "Resume monitoring" : "Pause monitoring", () => Send(_api.SetPausedAsync(route.Id, !paused), "Couldn't change monitoring.")));
            if (route.ReturnRouteId is null)
            {
                box.Add(Ui.Secondary("Create return trip", async () =>
                {
                    using HttpResponseMessage resp = await _api.CreateReturnTripAsync(route.Id);
                    if (resp.IsSuccessStatusCode && resp.Headers.Location is { } loc && Guid.TryParse(loc.OriginalString.Split('/')[^1], out Guid back))
                    {
                        await Shell.Current.GoToAsync($"../route?id={back}");
                        return;
                    }

                    await Ui.Toast(resp.IsSuccessStatusCode ? "Return trip created." : (await ApiClient.ReadProblemAsync(resp, "Couldn't create the return trip.")).Message);
                }));
            }
            else
            {
                box.Add(Ui.Secondary("Open return trip", () => Shell.Current.GoToAsync($"../route?id={route.ReturnRouteId.Value.Value}")));
            }
        }

        box.Add(Ui.Danger("Delete commute", async () =>
        {
            if (await DisplayAlertAsync("Delete commute", "Delete this commute and its history?", "Delete", "Keep it"))
            {
                using HttpResponseMessage resp = await _api.DeleteRouteAsync(route.Id);
                if (resp.IsSuccessStatusCode)
                {
                    await _leaveAlerts.SetAsync(route, false);
                    await _fences.SetAsync(route, false, false);
                    await Shell.Current.GoToAsync("..");
                }
                else
                {
                    await Ui.Toast("Delete failed.");
                }
            }
        }));
        return Ui.Card(box);
    }

    private async Task Send(Task<HttpResponseMessage> call, string failure)
    {
        try
        {
            using HttpResponseMessage resp = await call;
            if (!resp.IsSuccessStatusCode)
            {
                await Ui.Toast((await ApiClient.ReadProblemAsync(resp, failure)).Message);
            }
        }
        catch (HttpRequestException)
        {
            await Ui.Toast("That needs a connection.");
        }

        await LoadAsync();
    }

    private async Task CheckNowAsync()
    {
        try
        {
            (CheckNowResponse? result, HttpStatusCode status) = await _api.CheckNowAsync(_id);
            _message = result is { } r
                ? $"Live: {Format.Minutes(r.DurationSeconds)} min ({Format.Distance(r.DistanceMetres)})."
                : status == HttpStatusCode.TooManyRequests ? "Too many live checks — try again in a minute." : "Live check failed.";
        }
        catch (HttpRequestException)
        {
            _message = "Live checks need a connection.";
        }

        await LoadAsync();
    }

    // ── Schedule ──────────────────────────────────────────────────────────────
    private void RenderSchedule()
    {
        RouteDto route = _route!;
        MonitoringWindowDto? window = route.Windows.FirstOrDefault(w => w.IsActive) ?? (route.Windows.Count > 0 ? route.Windows[0] : null);
        VerticalStackLayout box = new() { Spacing = 8 };
        box.Add(Ui.H2("Monitoring schedule"));
        if (window is not null)
        {
            box.Add(Ui.Text($"{Format.WallClock(window.StartTime)} – {Format.WindowEnd(window.EndTime)} · {Format.DaysLabel(window.DaysOfWeek)}"));
            box.Add(Ui.Muted(window.IsActive ? (_activeSession is { } s ? $"Monitoring today · {s.PollCount} samples" : "Scheduled") : "Not monitoring"));
        }
        else
        {
            box.Add(Ui.Muted("No schedule yet."));
        }

        if (window is not null && _activeSession is { } session)
        {
            box.Add(Ui.Secondary("Stop monitoring today", () => Send(_api.StopWindowAsync(route.Id, window.Id, session.Id), "Failed to stop monitoring. Please try again.")));
        }

        // Edit = save a replacement window, then start it (as the web does).
        TimePicker start = new() { Time = TimeOnly.TryParse(window?.StartTime, out TimeOnly st) ? st.ToTimeSpan() : TimeSpan.Zero };
        TimePicker end = new() { Time = TimeOnly.TryParse(window?.EndTime, out TimeOnly et) ? et.ToTimeSpan() : new TimeSpan(23, 59, 0) };
        byte mask = window is null ? (byte)0x7F : (byte)Format.Days.Where(d => Format.DaysOf(window.DaysOfWeek).Contains(d.Day)).Sum(d => (int)d.Bit);
        box.Add(Ui.H2(window is null ? "Set a schedule" : "Change schedule"));
        box.Add(Ui.Field("From", start));
        box.Add(Ui.Field("Until", end));
        box.Add(Ui.DayPicker(mask, m => mask = m));
        box.Add(Ui.Primary("Save schedule", async () =>
        {
            if (mask == 0)
            {
                await Ui.Toast("Select at least one day.");
                return;
            }

            TimeSpan s = start.Time ?? TimeSpan.Zero, e = end.Time ?? TimeSpan.Zero;
            if (s.Hours == e.Hours && s.Minutes == e.Minutes)
            {
                await Ui.Toast("End time must be different from start time.");
                return;
            }

            try
            {
                using HttpResponseMessage saved = await _api.SaveWindowAsync(route.Id, new SaveWindowRequest(Format.HHmm(s), Format.HHmm(e), mask, TimeZoneInfo.Local.Id, window?.Id));
                if (!saved.IsSuccessStatusCode || await ApiClient.ReadWindowIdAsync(saved) is not { } newId)
                {
                    await Ui.Toast(saved.IsSuccessStatusCode ? "Schedule saved, but monitoring could not be initialized." : (await ApiClient.ReadProblemAsync(saved, "Couldn't save the schedule.")).Message);
                    return;
                }

                using HttpResponseMessage started = await _api.StartWindowAsync(route.Id, newId);
                if (!started.IsSuccessStatusCode)
                {
                    await Ui.Toast(started.StatusCode == HttpStatusCode.TooManyRequests ? "Schedule saved, but daily monitoring quota is exhausted." : "Schedule saved, but monitoring could not be initialized.");
                }
            }
            catch (HttpRequestException)
            {
                await Ui.Toast("Unable to connect. Please try again.");
            }

            await LoadAsync();
        }));
        _body.Add(Ui.Card(box));
    }

    // ── Heatmap / Weather ─────────────────────────────────────────────────────
    private void RenderHeatmap()
    {
        if (_heatmap is not { TotalSamples: > 0 } map)
        {
            _body.Add(Ui.Muted(_heatmap is null ? "Loading…" : "Not enough samples for a heatmap yet."));
            return;
        }

        _body.Add(Ui.Card(Ui.Stack(6,
            Ui.H2("When it's slow"),
            Ui.Muted($"Mean travel time by day and hour against the usual {Format.Minutes(map.MedianDurationSeconds)} min · {map.TotalSamples} samples · {map.TimeZoneId}"),
            new GraphicsView { HeightRequest = 200, Drawable = new HeatmapChart(map) },
            Ui.Muted("Pale = quicker than usual · red = much slower."))));

        foreach (var worst in map.Cells.Where(c => c.SampleCount >= 3).OrderByDescending(c => c.MeanDurationSeconds).Take(3))
        {
            _body.Add(Ui.Muted($"Slowest: {worst.DayOfWeek} {worst.Hour:00}:{worst.Quarter * 15:00} — {Format.Minutes(worst.MeanDurationSeconds)} min (±{Format.Minutes(worst.StdDevDurationSeconds)})"));
        }
    }

    private void RenderWeather()
    {
        if (_weather is not { } w)
        {
            _body.Add(Ui.Muted("Loading…"));
            return;
        }

        VerticalStackLayout box = Ui.Stack(6, Ui.H2("Weather impact"), Ui.Muted($"{w.SampleCount} samples with weather · {w.UnknownCount} without · needs {w.MinimumSamples} per condition"));
        if (w.Slices.Count == 0)
        {
            box.Add(Ui.Muted("No weather data yet."));
        }

        foreach (WeatherImpactSlice slice in w.Slices)
        {
            string delta = slice.DeltaSeconds == 0 ? "no change" : $"{(slice.DeltaSeconds > 0 ? "+" : "−")}{Format.Minutes(Math.Abs(slice.DeltaSeconds))} min ({slice.PercentDelta:+0;-0}%)";
            box.Add(Ui.Field($"{slice.Condition} · {slice.SampleCount} samples{(slice.IsConfident ? string.Empty : " · not enough yet")}",
                Ui.Colored(delta, slice.DeltaSeconds > 60 ? Ui.Bad : slice.DeltaSeconds < -60 ? Ui.Good : Colors.Gray, 15)));
        }

        _body.Add(Ui.Card(box));
    }
}
