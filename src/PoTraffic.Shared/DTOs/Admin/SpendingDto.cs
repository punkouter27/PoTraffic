namespace PoTraffic.Shared.DTOs.Admin;

/// <summary>
/// This month's provider spend against the cap. <see cref="CapUsd"/> is null when no cap is
/// set; when <see cref="IsCapReached"/> is true, scheduled polls and Check now are paused.
/// </summary>
public sealed record SpendingDto(
    int PollsThisMonth,
    decimal MonthToDateUsd,
    decimal? CapUsd,
    bool IsCapReached,
    DateTimeOffset ResetsAtUtc);

/// <summary>Body of <c>PUT /api/admin/users/{userId}/daily-quota</c>; null restores the default.</summary>
public sealed record SetUserDailyQuotaRequest(int? DailySessionQuota);

/// <summary>Body of <c>PUT /api/admin/configuration/{key}</c>.</summary>
public sealed record UpdateSystemConfigRequest(string Value);
