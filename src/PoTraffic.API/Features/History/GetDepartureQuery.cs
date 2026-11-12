using PoTraffic.Api.Infrastructure.Storage;
using PoTraffic.Api.Infrastructure.Time;
using PoTraffic.Shared.DTOs.History;

namespace PoTraffic.Api.Features.History;

public sealed record GetDepartureQuery(RouteId RouteId, UserId UserId, string DayOfWeek);

/// <summary>
/// Everything about "when should I leave" for one route and weekday, from one pass over the
/// route's samples: the quickest slot of the day (<see cref="DepartureDto.Best"/>) and, when
/// the route has an arrive-by target, the leave-by plan (<see cref="DepartureDto.Plan"/>).
///
/// <para>
/// These were two endpoints the dashboard called back to back for every card, each loading
/// the same local-time samples. One query keeps the two answers consistent with each other
/// and halves the dashboard's per-route requests.
/// </para>
///
/// <para>
/// The plan deliberately does not reuse the best slot: the best slot is the lowest
/// <em>average</em>, and the average is exactly the number that makes you late one day in
/// two. The plan takes each 5-minute slot's 90th-percentile trip and recommends the latest
/// slot where even that trip arrives in time — "leave by 7:40 and you're on time 9 days out of 10".
/// </para>
/// </summary>
public sealed class GetDepartureQueryHandler(TableStorageContext db)
{
    /// <summary>Departures considered for the plan: this far back from the target arrival.</summary>
    internal const int SearchWindowMinutes = 180;
    private const int SlotMinutes = 5;

    /// <summary>Null when the route is not the caller's, or there is nothing to say yet.</summary>
    public Task<DepartureDto?> Handle(GetDepartureQuery query, CancellationToken ct)
    {
        EntityRoute? route = db.GetOwnedRoute(query.RouteId, query.UserId, excludeDeleted: true);
        if (route is null)
            return Task.FromResult<DepartureDto?>(null);

        // Weekday and time of day are the user's local ones (see UserTime). Day-of-week
        // specific, with an all-days fallback while the requested weekday is still sparse.
        TimeZoneInfo zone = db.ZoneFor(query.UserId);
        (List<(DateTimeOffset Local, int Seconds)> source, bool fellBack) =
            db.LocalPollsForDay(route.Id, zone, query.DayOfWeek);

        List<(int Minute, int Seconds)> samples =
            [.. source.Select(p => (p.Local.Hour * 60 + p.Local.Minute, p.Seconds))];

        OptimalDepartureDto? best = BestSlot(samples) is { } slot
            ? new OptimalDepartureDto(
                query.DayOfWeek,
                slot.Bucket,
                slot.MeanSeconds,
                slot.MeanSeconds * 0.95,
                slot.MeanSeconds * 1.05,
                SampleCount: samples.Count,
                DayOfWeekSpecific: !fellBack)
            : null;

        DeparturePlanDto? plan = null;
        if (route.ArriveBy is { } arriveBy)
        {
            string arriveText = arriveBy.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            plan = BuildPlan(samples, arriveBy.Hour * 60 + arriveBy.Minute) is { } p
                ? new DeparturePlanDto(
                    arriveText,
                    $"{p.LeaveByMinute / 60:D2}:{p.LeaveByMinute % 60:D2}",
                    p.MedianSeconds,
                    p.P90Seconds,
                    p.OnTimePercent,
                    p.SampleCount,
                    !fellBack)
                : new DeparturePlanDto(arriveText, null, null, null, 0, 0, !fellBack);
        }

        return Task.FromResult(best is null && plan is null ? null : new DepartureDto(best, plan));
    }

    internal sealed record Slot(int Bucket, double MeanSeconds);

    /// <summary>
    /// The earliest 5-minute slot whose mean trip is within 5% of the day's quickest. Null
    /// with no samples.
    /// </summary>
    /// <param name="samples">(local minute of day the trip started, trip seconds)</param>
    internal static Slot? BestSlot(IReadOnlyCollection<(int Minute, int Seconds)> samples)
    {
        if (samples.Count == 0)
            return null;

        List<Slot> slots = [.. samples
            .GroupBy(s => s.Minute / SlotMinutes * SlotMinutes)
            .Select(g => new Slot(g.Key, g.Average(s => (double)s.Seconds)))
            .OrderBy(s => s.Bucket)];

        double minMean = slots.Min(s => s.MeanSeconds);
        Slot first = slots.First(s => s.MeanSeconds <= minMean * 1.05);
        return first with { MeanSeconds = minMean };
    }

    internal sealed record Plan(int LeaveByMinute, int MedianSeconds, int P90Seconds, int OnTimePercent, int SampleCount);

    /// <summary>
    /// The latest slot, within <see cref="SearchWindowMinutes"/> before
    /// <paramref name="arriveByMinute"/>, whose 90th-percentile trip arrives in time. Leaving
    /// is assumed at the slot's start, so every sample in the slot left no earlier than that.
    /// </summary>
    /// <param name="samples">(local minute of day the trip started, trip seconds)</param>
    internal static Plan? BuildPlan(IEnumerable<(int Minute, int Seconds)> samples, int arriveByMinute)
    {
        int earliest = Math.Max(0, arriveByMinute - SearchWindowMinutes);
        return samples
            .Where(s => s.Minute >= earliest && s.Minute < arriveByMinute)
            .GroupBy(s => s.Minute / SlotMinutes * SlotMinutes)
            .Where(g => g.Count() >= QuotaConstants.BaselineMinSessionCount)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                List<int> sorted = [.. g.Select(s => s.Seconds).Order()];
                int p90 = sorted[(int)Math.Ceiling(0.9 * sorted.Count) - 1];
                int median = sorted[sorted.Count / 2];
                int onTime = g.Count(s => s.Minute * 60 + s.Seconds <= arriveByMinute * 60);
                return new Plan(g.Key, median, p90, (int)Math.Round(100.0 * onTime / sorted.Count), sorted.Count);
            })
            .FirstOrDefault(p => p.LeaveByMinute * 60 + p.P90Seconds <= arriveByMinute * 60);
    }
}
