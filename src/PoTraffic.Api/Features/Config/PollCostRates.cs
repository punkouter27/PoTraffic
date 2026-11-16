using PoTraffic.Api.Infrastructure.Storage;

namespace PoTraffic.Api.Features.Config;

/// <summary>
/// The per-poll provider rate, read from <see cref="SystemConfiguration"/> (admin-editable at
/// runtime, key <see cref="SystemSettings.PerPollKey"/>). The admin usage query and the per-user
/// quota query both price polls through here, so they cannot disagree.
/// </summary>
public static class PollCostRates
{
    /// <summary>The USD cost of a single poll; the seeded default when the row is missing or unparseable.</summary>
    public static decimal PerPoll(TableStorageContext db)
        => SystemSettings.DecimalOrDefault(db, SystemSettings.PerPollKey);
}
