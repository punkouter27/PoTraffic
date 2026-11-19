using PoTraffic.Mobile.Services;
using PoTraffic.Shared.DTOs.Account;
using PoTraffic.Shared.Enums;

namespace PoTraffic.Mobile.Pages;

/// <summary>Account, usage, display panels, this phone's alerts, data controls, admin and server settings.</summary>
public sealed class SettingsPage : ContentPage
{
    private static readonly string[] Locales = ["en-IE", "en-GB", "en-US", "de-DE", "fr-FR"];

    private readonly ApiClient _api;
    private readonly Session _session;
    private readonly MobileConfig _config;
    private readonly EntraAuthService _entra;
    private readonly CookieJar _cookies;
    private readonly OfflineStore _store;
    private readonly IAlarmScheduler _alarms;
    private readonly VerticalStackLayout _body = new() { Spacing = 10, Padding = new Thickness(16, 8, 16, 32) };

    public SettingsPage(ApiClient api, Session session, MobileConfig config, EntraAuthService entra, CookieJar cookies, OfflineStore store, IAlarmScheduler alarms)
    {
        _api = api;
        _session = session;
        _config = config;
        _entra = entra;
        _cookies = cookies;
        _store = store;
        _alarms = alarms;
        Title = "Settings";
        Content = new ScrollView { Content = _body };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RenderAsync();
    }

    private async Task RenderAsync()
    {
        _body.Clear();
        _body.Add(Ui.H1("Settings"));
        bool signedIn = await _session.RefreshAsync();

        ProfileDto? profile = null;
        QuotaDto? quota = null;
        if (signedIn)
        {
            try
            {
                Task<ProfileDto?> p = _api.GetProfileAsync();
                Task<QuotaDto?> q = _api.GetQuotaAsync();
                profile = await p;
                quota = await q;
            }
            catch (HttpRequestException)
            {
            }
        }

        // Account
        VerticalStackLayout account = Ui.Stack(6, Ui.H2("Account"));
        if (signedIn)
        {
            account.Add(Ui.Text(_session.Me!.Email));
            account.Add(Ui.Muted($"{_session.Me.Role} · signed in with {_session.Me.AuthProvider}{(profile?.LastLoginAt is { } last ? $" · last sign-in {last.LocalDateTime:MMM d}" : string.Empty)}"));
            account.Add(Ui.Secondary("Sign out", SignOutAsync));
        }
        else
        {
            account.Add(Ui.Muted("Not signed in."));
            account.Add(Ui.Primary("Sign in", () => Shell.Current.GoToAsync("login")));
        }

        _body.Add(Ui.Card(account));

        if (signedIn)
        {
            if (quota is { } qu)
            {
                _body.Add(Ui.Card(Ui.Stack(4,
                    Ui.H2("Commutes"),
                    Ui.Text($"{qu.UsedToday} of {qu.DailyLimit} monitoring sessions used today ({qu.Remaining} left)"),
                    new ProgressBar { Progress = qu.DailyLimit == 0 ? 0 : Math.Min(1, qu.UsedToday / (double)qu.DailyLimit), ProgressColor = Ui.Accent },
                    Ui.Muted($"{qu.PollsToday} samples today · ~${qu.EstimatedCostTodayUsd:F2} today · ~${qu.ProjectedMonthlyCostUsd:F2}/month · resets {qu.ResetsAtUtc.LocalDateTime:h:mm tt}"))));
            }

            _body.Add(Ui.Card(Display(profile)));
        }

        _body.Add(Ui.Card(PhoneSection()));

        if (signedIn)
        {
            _body.Add(Ui.Card(DataSection()));
        }

        VerticalStackLayout more = Ui.Stack(6, Ui.H2("More"));
        if (_session.IsAdmin)
        {
            more.Add(Ui.Secondary("Admin", () => Shell.Current.GoToAsync("admin")));
        }

        more.Add(Ui.Secondary("System status", () => Shell.Current.GoToAsync("diag")));
        _body.Add(Ui.Card(more));

        _body.Add(Ui.Card(ServerSection()));
        _body.Add(Ui.Muted($"PoTraffic for {DeviceInfo.Current.Platform} · v{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})"));
    }

    private View Display(ProfileDto? profile)
    {
        VerticalStackLayout box = Ui.Stack(6, Ui.H2("Display"));
        Picker locale = new() { Title = "Locale", ItemsSource = Locales, SelectedItem = profile?.Locale };
        locale.SelectedIndexChanged += async (_, _) =>
        {
            if (locale.SelectedItem is string l && l != profile?.Locale)
            {
                await Call(_api.UpdateProfileAsync(l), $"Locale set to {l}.");
            }
        };
        box.Add(Ui.Field("Locale", locale));
        box.Add(Ui.Secondary($"Use this phone's time zone ({TimeZoneInfo.Local.Id})", () => Call(_api.SetTimeZoneAsync(TimeZoneInfo.Local.Id), "Time zone updated.")));

        box.Add(Ui.Text("Extra panels"));
        foreach ((AdvancedPanel panel, string label) in new[] { (AdvancedPanel.Map, "Route map"), (AdvancedPanel.Weather, "Weather impact"), (AdvancedPanel.Sparklines, "Trend lines on cards") })
        {
            box.Add(Ui.Field(label, Ui.Toggle(_session.PanelOn(panel), async on =>
            {
                try
                {
                    using HttpResponseMessage resp = await _api.SetPanelAsync(panel, on);
                    if (resp.IsSuccessStatusCode)
                    {
                        _session.SetPanels(on ? _session.Panels.Append(panel) : _session.Panels.Where(p => p != panel));
                    }
                }
                catch (HttpRequestException)
                {
                    await Ui.Toast("That needs a connection.");
                }
            })));
        }

        return box;
    }

    private View PhoneSection()
    {
        VerticalStackLayout box = Ui.Stack(6, Ui.H2("This phone"));
        Label lead = Ui.Text($"Leave-by alerts come {LeaveAlerts.LeadMinutes} min early");
        Stepper stepper = new() { Minimum = 0, Maximum = 60, Increment = 5, Value = LeaveAlerts.LeadMinutes };
        stepper.ValueChanged += (_, e) =>
        {
            LeaveAlerts.LeadMinutes = (int)e.NewValue;
            lead.Text = $"Leave-by alerts come {LeaveAlerts.LeadMinutes} min early";
        };
        box.Add(lead);
        box.Add(stepper);
        box.Add(Ui.Muted("Turn alerts on per commute, on the commute's page. New lead times apply the next time alerts are re-armed (opening the dashboard)."));
        box.Add(Ui.Secondary("Allow notifications", async () =>
            await Ui.Toast(await _alarms.RequestPermissionAsync() ? "Notifications are on." : "Notifications are off — turn them on in the phone's Settings app.")));
        box.Add(Ui.Secondary("Test a notification", () =>
        {
            _alarms.ShowNow(1, "Leave in 10 min — by 8:05 AM", "This is what a leave-by alert looks like.", string.Empty);
            return Task.CompletedTask;
        }));
        box.Add(Ui.Muted($"Watching {CommuteFences.Load().Count} commute start point(s) for leaving-home detection."));
        return box;
    }

    private View DataSection()
    {
        Entry confirm = new() { Placeholder = "Type DELETE to confirm" };
        return Ui.Stack(6,
            Ui.H2("Your data"),
            Ui.Secondary("Download my data", ExportAsync),
            Ui.Muted("Deleting your account removes every commute, sample and alert. It can't be undone."),
            confirm,
            Ui.Danger("Delete my account", async () =>
            {
                if (confirm.Text != "DELETE")
                {
                    await Ui.Toast("Type DELETE in capitals, exactly.");
                    return;
                }

                using HttpResponseMessage resp = await _api.DeleteAccountAsync();
                if (resp.IsSuccessStatusCode)
                {
                    await SignOutAsync();
                }
                else
                {
                    await Ui.Toast("The account couldn't be deleted. Try again.");
                }
            }));
    }

    private async Task ExportAsync()
    {
        try
        {
            using HttpResponseMessage resp = await _api.ExportAccountAsync();
            if (!resp.IsSuccessStatusCode)
            {
                await Ui.Toast("Export failed.");
                return;
            }

            string file = Path.Combine(FileSystem.CacheDirectory, resp.Content.Headers.ContentDisposition?.FileNameStar ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "potraffic-export.json");
            await File.WriteAllBytesAsync(file, await resp.Content.ReadAsByteArrayAsync());
            await Share.Default.RequestAsync(new ShareFileRequest("Your PoTraffic data", new ShareFile(file, "application/json")));
        }
        catch (HttpRequestException)
        {
            await Ui.Toast("Export needs a connection.");
        }
    }

    private View ServerSection()
    {
        Entry baseUrl = new() { Text = _config.BaseUrl, Keyboard = Keyboard.Url };
        Entry clientId = new() { Text = _config.EntraClientId, Placeholder = "Entra app client id (GUID)" };
        Entry tenant = new() { Text = _config.EntraTenantId, Placeholder = "common" };
        return Ui.Stack(6,
            Ui.H2("Server"),
            Ui.Muted("Debug builds reach the PC's API over USB (scripts/mobile-dev.ps1). Microsoft sign-in needs this app's redirect URI registered: " + MobileConfig.RedirectUri),
            Ui.Text("API address"), baseUrl,
            Ui.Text("Entra client id"), clientId,
            Ui.Text("Tenant"), tenant,
            Ui.Secondary("Save", async () =>
            {
                bool moved = baseUrl.Text?.TrimEnd('/') != _config.BaseUrl;
                _config.BaseUrl = baseUrl.Text ?? string.Empty;
                _config.EntraClientId = clientId.Text ?? string.Empty;
                _config.EntraTenantId = tenant.Text ?? string.Empty;
                if (moved)
                {
                    await SignOutAsync();
                }

                await Ui.Toast("Saved.");
            }));
    }

    private static async Task Call(Task<HttpResponseMessage> call, string success)
    {
        try
        {
            using HttpResponseMessage resp = await call;
            await Ui.Toast(resp.IsSuccessStatusCode ? success : (await ApiClient.ReadProblemAsync(resp, "That didn't save.")).Message);
        }
        catch (HttpRequestException)
        {
            await Ui.Toast("That needs a connection.");
        }
    }

    private async Task SignOutAsync()
    {
        try
        {
            using HttpResponseMessage _ = await _api.LogoutAsync();
        }
        catch (HttpRequestException)
        {
        }

        await _entra.SignOutAsync();
        _cookies.Clear();
        _session.Clear();
        OfflineStore.Clear();
        await Shell.Current.GoToAsync("login");
    }
}
