using System.Globalization;
using PoTraffic.Mobile.Services;
using PoTraffic.Shared.DTOs.Admin;

namespace PoTraffic.Mobile.Pages;

/// <summary>Admin (Administrator role): usage per user with daily quotas, spending, global volatility, system configuration.</summary>
public sealed class AdminPage : ContentPage
{
    private const string Usage = "Usage", Spending = "Spending", Volatility = "Volatility", Config = "Config";

    private readonly ApiClient _api;
    private readonly VerticalStackLayout _body = new() { Spacing = 10, Padding = 16 };
    private string _tab = Usage;

    public AdminPage(ApiClient api)
    {
        _api = api;
        Title = "Admin";
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
        _body.Add(Ui.H1("Admin"));
        _body.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = Ui.Tabs([Usage, Spending, Volatility, Config], _tab, async t => { _tab = t; await RenderAsync(); }) });
        try
        {
            switch (_tab)
            {
                case Spending: await SpendingAsync(); break;
                case Volatility: await VolatilityAsync(); break;
                case Config: await ConfigAsync(); break;
                default: await UsageAsync(); break;
            }
        }
        catch (HttpRequestException ex)
        {
            _body.Add(Ui.Colored(ex.StatusCode == System.Net.HttpStatusCode.Forbidden ? "Admins only." : "Couldn't load — check the connection.", Ui.Bad));
        }
    }

    private async Task UsageAsync()
    {
        List<UserDailyUsageDto> users = await _api.GetUsersAsync() ?? [];
        if (users.Count == 0)
        {
            _body.Add(Ui.Muted("No users have signed in yet."));
        }

        foreach (UserDailyUsageDto user in users.OrderByDescending(u => u.TodayPollCount))
        {
            Entry quota = new() { Text = user.DailySessionQuota?.ToString(CultureInfo.InvariantCulture), Placeholder = "default", Keyboard = Keyboard.Numeric, WidthRequest = 90 };
            _body.Add(Ui.Card(Ui.Stack(4,
                new Label { Text = user.Email, FontAttributes = FontAttributes.Bold },
                Ui.Muted($"{user.Locale} · joined {user.CreatedAt.LocalDateTime:MMM d, yyyy} · last login {user.LastLoginAt?.LocalDateTime.ToString("MMM d", CultureInfo.CurrentCulture) ?? "never"}"),
                Ui.Text($"{user.TodayPollCount} polls today · ${user.TodayEstimatedCostUsd:F3}"),
                Ui.Muted(string.Join(" · ", user.ProviderBreakdown.Select(p => $"{p.Provider}: {p.PollCount} (${p.EstimatedCostUsd:F3})"))),
                Ui.Field("Daily limit (blank = default)", quota),
                Ui.Secondary("Save limit", async () =>
                {
                    int? limit = int.TryParse(quota.Text, out int n) ? n : null;
                    using HttpResponseMessage resp = await _api.SetUserQuotaAsync(user.UserId, limit);
                    await Ui.Toast(resp.IsSuccessStatusCode ? "Saved." : (await ApiClient.ReadProblemAsync(resp, "Couldn't save the limit.")).Message);
                }))));
        }
    }

    private async Task SpendingAsync()
    {
        if (await _api.GetSpendingAsync() is not { } s)
        {
            return;
        }

        VerticalStackLayout box = Ui.Stack(6,
            Ui.H2("This month"),
            Ui.Text($"${s.MonthToDateUsd:F2} of {(s.CapUsd is { } cap ? $"${cap:F2}" : "no cap")} · {s.PollsThisMonth} polls"),
            s.IsCapReached ? Ui.Colored("Spending cap reached — provider calls are paused.", Ui.Bad) : Ui.Muted($"Resets {s.ResetsAtUtc.LocalDateTime:MMM d}"));
        foreach (SpendingLineDto line in s.Lines ?? [])
        {
            box.Add(Ui.Field($"{line.Kind} · {line.Calls} calls", Ui.Text($"${line.CostUsd:F2}")));
        }

        _body.Add(Ui.Card(box));
    }

    private async Task VolatilityAsync()
    {
        List<RecentVolatilityPointDto> points = await _api.GetRecentVolatilityAsync() ?? [];
        _body.Add(Ui.H2("Mean travel time, last 24 h (all routes)"));
        if (points.Count < 2)
        {
            _body.Add(Ui.Muted("Not enough polls yet."));
            return;
        }

        _body.Add(Ui.Card(new GraphicsView
        {
            HeightRequest = 180,
            Drawable = new LineChart([.. points.OrderBy(p => p.PolledAt).Select(p => (p.PolledAt.LocalDateTime, p.MeanDurationSeconds / 60.0))], axes: true),
        }));
        _body.Add(Ui.Muted($"{points.Count} poll rounds · up to {points.Max(p => p.RouteCount)} routes per round"));
    }

    private async Task ConfigAsync()
    {
        List<SystemConfigDto> configs = await _api.GetConfigurationAsync() ?? [];
        if (configs.Count == 0)
        {
            _body.Add(Ui.Muted("No configuration entries."));
        }

        foreach (SystemConfigDto config in configs.OrderBy(c => c.Key))
        {
            VerticalStackLayout box = Ui.Stack(4, new Label { Text = config.Key, FontAttributes = FontAttributes.Bold }, Ui.Muted(config.Description));
            if (config.IsEditable && !config.IsSensitive)
            {
                Entry value = new() { Text = config.Value };
                box.Add(value);
                box.Add(Ui.Secondary("Save", async () =>
                {
                    using HttpResponseMessage resp = await _api.SetConfigurationAsync(config.Key, value.Text ?? string.Empty);
                    await Ui.Toast(resp.IsSuccessStatusCode ? "Saved." : (await ApiClient.ReadProblemAsync(resp, "Couldn't save.")).Message);
                }));
            }
            else
            {
                box.Add(Ui.Text(config.IsSensitive ? "•••••• (sensitive)" : config.Value));
            }

            _body.Add(Ui.Card(box));
        }
    }
}
