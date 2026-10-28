using PoTraffic.API.Features.Config;
using PoTraffic.API.Infrastructure.Storage;
using PoTraffic.Shared.DTOs.Admin;

namespace PoTraffic.API.Features.Admin;

/// <summary>This month's provider spend against the monthly cap, for the Admin page.</summary>
public sealed record GetSpendingQuery;

public sealed class GetSpendingHandler(TableStorageContext db)
{
    public Task<SpendingDto> Handle(GetSpendingQuery query, CancellationToken ct)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int polls = SpendingCap.PollsThisMonth(db, now);
        decimal spend = polls * PollCostRates.PerPoll(db);
        decimal? cap = SpendingCap.MonthlyCap(db);

        return Task.FromResult(new SpendingDto(
            PollsThisMonth: polls,
            MonthToDateUsd: spend,
            CapUsd: cap,
            IsCapReached: cap is { } c && spend >= c,
            ResetsAtUtc: SpendingCap.MonthStart(now).AddMonths(1)));
    }
}
