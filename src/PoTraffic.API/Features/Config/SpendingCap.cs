using System.Globalization;
using PoTraffic.API.Infrastructure.Storage;

namespace PoTraffic.API.Features.Config;

/// <summary>
/// The monthly provider budget. Month-to-date spend is every poll recorded since the first
/// of the current UTC month, priced through <see cref="PollCostRates"/> — the same rate the
/// admin usage figures use, so the cap trips at the number the Admin page shows. Once spend
/// reaches the cap, scheduled polls and Check now stop calling the provider until the month
/// rolls over or an administrator raises the cap; the poll chains stay armed meanwhile, so
/// monitoring resumes on its own.
/// </summary>
public static class SpendingCap
{
    public const string MonthlyCapKey = "cost.monthly.cap.usd";

    /// <summary>Seeded value. Zero or less (or an unparseable row) means no cap.</summary>
    public const decimal MonthlyCapDefault = 100m;

    /// <summary>The configured cap in USD, or null when there is none.</summary>
    public static decimal? MonthlyCap(TableStorageContext db)
        => db.SystemConfigurations.FirstOrDefault(c => c.Key == MonthlyCapKey) is { } row
           && decimal.TryParse(row.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal cap)
           && cap > 0
            ? cap
            : null;

    /// <summary>First instant of <paramref name="nowUtc"/>'s UTC month.</summary>
    public static DateTimeOffset MonthStart(DateTimeOffset nowUtc)
        => new(nowUtc.UtcDateTime.Year, nowUtc.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);

    public static int PollsThisMonth(TableStorageContext db, DateTimeOffset nowUtc)
    {
        DateTimeOffset start = MonthStart(nowUtc);
        return db.PollRecords.Count(p => p.PolledAt >= start);
    }

    public static decimal MonthToDateSpend(TableStorageContext db, DateTimeOffset nowUtc)
        => PollsThisMonth(db, nowUtc) * PollCostRates.PerPoll(db);

    /// <summary>True when a cap is set and this month's spend has reached it.</summary>
    public static bool IsReached(TableStorageContext db, DateTimeOffset nowUtc)
        => MonthlyCap(db) is { } cap && MonthToDateSpend(db, nowUtc) >= cap;
}
