using PoTraffic.Shared.DTOs.History;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.Ids;

namespace PoTraffic.Mobile.Services;

/// <summary>The OS side of notifications: Android AlarmManager, iOS UNUserNotificationCenter.</summary>
public interface IAlarmScheduler
{
    Task<bool> RequestPermissionAsync();

    void Schedule(ScheduledLeaveAlert alert);

    void Cancel(int id);

    void ShowNow(int id, string title, string body, string routeId);
}

/// <summary>
/// "Leave by" notifications, armed with the OS so they fire with the app closed. For every route
/// the user switched them on for, each of the next 7 monitored days gets one alert at
/// <c>LeaveBy − lead</c>, from the same departure plan the dashboard shows (it needs an arrive-by
/// target). Re-armed whenever the dashboard refreshes, so a changing plan moves the alert.
/// </summary>
public sealed class LeaveAlerts(ApiClient api, OfflineStore store, IAlarmScheduler alarms)
{
    private const string RoutesKey = "potraffic_leave_alert_routes";
    private const string LeadKey = "potraffic_leave_alert_lead";
    private const string StoreKey = "leave-alerts";
    private static readonly TimeSpan MinRearmInterval = TimeSpan.FromMinutes(30);
    private DateTimeOffset _lastArm = DateTimeOffset.MinValue;

    public static int LeadMinutes
    {
        get => Preferences.Default.Get(LeadKey, 10);
        set => Preferences.Default.Set(LeadKey, Math.Clamp(value, 0, 60));
    }

    public static bool IsOn(RouteId id) => EnabledRoutes().Contains(id.Value.ToString());

    public async Task<bool> SetAsync(RouteDto route, bool on)
    {
        if (on && !await alarms.RequestPermissionAsync())
        {
            return false;
        }

        HashSet<string> routes = EnabledRoutes();
        if (on)
        {
            routes.Add(route.Id.Value.ToString());
        }
        else
        {
            routes.Remove(route.Id.Value.ToString());
        }

        Preferences.Default.Set(RoutesKey, string.Join(';', routes));
        await RearmAsync([route], force: true);
        return true;
    }

    /// <summary>Re-arms the alerts for the given routes (all known routes when called from the dashboard).</summary>
    public async Task RearmAsync(IReadOnlyCollection<RouteDto> routes, bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _lastArm < MinRearmInterval)
        {
            return;
        }

        _lastArm = DateTimeOffset.UtcNow;
        HashSet<string> enabled = EnabledRoutes();
        HashSet<string> touched = [.. routes.Select(r => r.Id.Value.ToString())];
        List<ScheduledLeaveAlert> previous = await store.LoadAsync(StoreKey, MobileJsonContext.Default.ListScheduledLeaveAlert) ?? [];

        // Alerts for routes outside this batch stay as they are.
        List<ScheduledLeaveAlert> kept = [.. previous.Where(a => !touched.Contains(a.RouteId) && enabled.Contains(a.RouteId))];
        foreach (ScheduledLeaveAlert stale in previous.Except(kept))
        {
            alarms.Cancel(stale.Id);
        }

        List<ScheduledLeaveAlert> armed = [.. kept];
        foreach (RouteDto route in routes.Where(r => enabled.Contains(r.Id.Value.ToString()) && r.ArriveBy is not null))
        {
            armed.AddRange(await PlanAsync(route));
        }

        foreach (ScheduledLeaveAlert alert in armed.Except(kept))
        {
            alarms.Schedule(alert);
        }

        await store.SaveAsync(StoreKey, armed, MobileJsonContext.Default.ListScheduledLeaveAlert);
    }

    private async Task<List<ScheduledLeaveAlert>> PlanAsync(RouteDto route)
    {
        HashSet<DayOfWeek> days = [.. route.Windows.SelectMany(w => Format.DaysOf(w.DaysOfWeek))];
        if (days.Count == 0)
        {
            days = [.. Enum.GetValues<DayOfWeek>()];
        }

        string label = route.Name ?? $"{Format.Street(route.OriginAddress)} → {Format.Street(route.DestinationAddress)}";
        Dictionary<DayOfWeek, DeparturePlanDto?> plans = [];
        List<ScheduledLeaveAlert> alerts = [];
        DateTimeOffset now = DateTimeOffset.Now;

        for (int offset = 0; offset < 7; offset++)
        {
            DateTime date = DateTime.Today.AddDays(offset);
            if (!days.Contains(date.DayOfWeek))
            {
                continue;
            }

            if (!plans.TryGetValue(date.DayOfWeek, out DeparturePlanDto? plan))
            {
                plan = plans[date.DayOfWeek] = (await api.GetDepartureAsync(route.Id, date.DayOfWeek))?.Plan;
            }

            if (plan?.LeaveBy is not { } leaveBy || !TimeOnly.TryParse(leaveBy, out TimeOnly leaveAt))
            {
                continue;
            }

            DateTimeOffset fireAt = new DateTimeOffset(date.Add(leaveAt.ToTimeSpan())).AddMinutes(-LeadMinutes);
            if (fireAt <= now)
            {
                continue;
            }

            string lead = LeadMinutes == 0 ? "now" : $"in {LeadMinutes} min";
            alerts.Add(new ScheduledLeaveAlert(
                StableId(route.Id, date),
                route.Id.Value.ToString(),
                fireAt,
                $"Leave {lead} — by {Format.WallClock(leaveBy)}",
                $"{label}: usually {Format.Minutes(plan.TypicalSeconds ?? 0)} min, up to {Format.Minutes(plan.WorstCaseSeconds ?? 0)} · on time {plan.OnTimePercent}% of {plan.SampleCount} trips"));
        }

        return alerts;
    }

    private static HashSet<string> EnabledRoutes()
        => [.. Preferences.Default.Get(RoutesKey, string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)];

    // Deterministic per route+date (HashCode is randomised per process), so the same alert keeps
    // its OS id across restarts and re-arming replaces it rather than duplicating it.
    private static int StableId(RouteId id, DateTime date)
        => (BitConverter.ToInt32(id.Value.ToByteArray(), 0) ^ (date.DayNumber() * 397)) & 0x7FFFFFFF;
}

internal static class DateExtensions
{
    public static int DayNumber(this DateTime date) => DateOnly.FromDateTime(date).DayNumber;
}
