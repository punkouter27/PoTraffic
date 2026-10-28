using System.Globalization;
using PoTraffic.API.Infrastructure.Storage;

namespace PoTraffic.API.Features.Config;

/// <summary>
/// The monthly provider budget. Month-to-date spend is every billed Google call since the
/// first of the current UTC month: scheduled polls (one <c>PollRecord</c> each, priced through
/// <see cref="PollCostRates"/>) plus the calls counted in <see cref="ProviderUsage"/> — Check
/// now, geocoding, directions and address autocomplete — each at its own rate. Once spend
/// reaches the cap, scheduled polls and Check now stop calling the provider until the month
/// rolls over or an administrator raises the cap; the poll chains stay armed meanwhile, so
/// monitoring resumes on its own. Route creation and address suggestions are counted but
/// not blocked, so nobody is locked out of setting up a commute.
/// </summary>
public static class SpendingCap
{
    public const string MonthlyCapKey = "cost.monthly.cap.usd";

    /// <summary>Seeded value. Zero or less (or an unparseable row) means no cap.</summary>
    public const decimal MonthlyCapDefault = 100m;

    /// <summary>
    /// Per-call rate keys and seeded defaults (Google's list prices per request). Check now
    /// is the same Distance Matrix call as a poll, so it shares the per-poll rate.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Key, decimal Default, string Description)> Rates =
        new Dictionary<string, (string, decimal, string)>
        {
            [ProviderCallKind.Geocode] = ("cost.pergeocode.googlemaps", 0.005m, "Cost per geocode - Google Maps"),
            [ProviderCallKind.Directions] = ("cost.perdirections.googlemaps", 0.005m, "Cost per directions request - Google Maps"),
            [ProviderCallKind.Autocomplete] = ("cost.perautocomplete.googlemaps", 0.00283m, "Cost per address autocomplete request - Google Places"),
        };

    /// <summary>Serialises find-or-create on a month's counter row across concurrent requests.</summary>
    private static readonly object CounterGate = new();

    /// <summary>The configured cap in USD, or null when there is none.</summary>
    public static decimal? MonthlyCap(TableStorageContext db)
        => ReadDecimal(db, MonthlyCapKey) is { } cap && cap > 0 ? cap : null;

    /// <summary>The USD rate for one call of <paramref name="kind"/>.</summary>
    public static decimal RateFor(TableStorageContext db, string kind)
        => Rates.TryGetValue(kind, out var rate)
            ? ReadDecimal(db, rate.Key) ?? rate.Default
            : PollCostRates.PerPoll(db);

    /// <summary>First instant of <paramref name="nowUtc"/>'s UTC month.</summary>
    public static DateTimeOffset MonthStart(DateTimeOffset nowUtc)
        => new(nowUtc.UtcDateTime.Year, nowUtc.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);

    private static string MonthKey(DateTimeOffset nowUtc)
        => nowUtc.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Counts one billed call. Only the in-memory working set changes; the row is written by
    /// the next <c>SaveChangesAsync</c>, so a burst of autocomplete keystrokes does not cost a
    /// storage write each.
    /// </summary>
    public static void Record(TableStorageContext db, string kind, DateTimeOffset nowUtc)
    {
        string month = MonthKey(nowUtc);
        string key = $"{month}|{kind}";
        lock (CounterGate)
        {
            ProviderUsage? row = db.ProviderUsages.FirstOrDefault(u => u.Key == key);
            if (row is null)
                db.Add(new ProviderUsage { Key = key, Month = month, Kind = kind, Count = 1 });
            else
                row.Count++;
        }
    }

    public static int PollsThisMonth(TableStorageContext db, DateTimeOffset nowUtc)
    {
        DateTimeOffset start = MonthStart(nowUtc);
        return db.PollRecords.Count(p => p.PolledAt >= start);
    }

    /// <summary>This month's billed calls by kind, scheduled polls first, each with its cost.</summary>
    public static IReadOnlyList<(string Kind, int Calls, decimal CostUsd)> MonthToDateLines(
        TableStorageContext db, DateTimeOffset nowUtc)
    {
        string month = MonthKey(nowUtc);
        int polls = PollsThisMonth(db, nowUtc);
        List<(string, int, decimal)> lines = [("Poll", polls, polls * PollCostRates.PerPoll(db))];
        lines.AddRange(db.ProviderUsages
            .Where(u => u.Month == month)
            .AsEnumerable()
            .OrderBy(u => u.Kind, StringComparer.Ordinal)
            .Select(u => (u.Kind, u.Count, u.Count * RateFor(db, u.Kind))));
        return lines;
    }

    public static decimal MonthToDateSpend(TableStorageContext db, DateTimeOffset nowUtc)
        => MonthToDateLines(db, nowUtc).Sum(l => l.CostUsd);

    /// <summary>True when a cap is set and this month's spend has reached it.</summary>
    public static bool IsReached(TableStorageContext db, DateTimeOffset nowUtc)
        => MonthlyCap(db) is { } cap && MonthToDateSpend(db, nowUtc) >= cap;

    private static decimal? ReadDecimal(TableStorageContext db, string key)
        => db.SystemConfigurations.FirstOrDefault(c => c.Key == key) is { } row
           && decimal.TryParse(row.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
}
