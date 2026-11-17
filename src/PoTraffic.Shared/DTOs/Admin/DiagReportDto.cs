namespace PoTraffic.Shared.DTOs.Admin;

/// <summary>
/// Body of <c>GET /api/diag</c>, rendered by the <c>/diag</c> page. Anonymous-safe by
/// construction: names, statuses, fixed plain-English purposes and timings only.
/// <see cref="DiagCheckDto.Detail"/> is filled for administrators alone.
/// </summary>
public sealed record DiagReportDto(
    string Status,
    string Environment,
    DateTimeOffset CheckedAtUtc,
    List<DiagCheckDto> Checks);

/// <summary>One dependency on the <c>/diag</c> page.</summary>
/// <param name="Key">Stable health-check name (e.g. <c>storage</c>).</param>
/// <param name="Name">Friendly display name.</param>
/// <param name="Category"><c>storage</c> or <c>service</c> — which section it renders under.</param>
/// <param name="Status">Healthy, Degraded or Unhealthy.</param>
/// <param name="Purpose">One fixed line saying what the dependency is for.</param>
/// <param name="LatencyMs">How long the check took.</param>
/// <param name="Detail">The raw health-check description — administrators only, otherwise null.</param>
public sealed record DiagCheckDto(
    string Key,
    string Name,
    string Category,
    string Status,
    string Purpose,
    double LatencyMs,
    string? Detail);
