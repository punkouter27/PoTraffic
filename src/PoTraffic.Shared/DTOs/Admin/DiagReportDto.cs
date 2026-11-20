namespace PoTraffic.Shared.DTOs.Admin;

/// <summary>
/// Body of <c>GET /api/diag</c> (the /diag contract in AGENTS.md), rendered by the anonymous
/// <c>/diag</c> page and the mobile app. Anonymous-safe by construction: statuses, timings,
/// fixed plain-English labels, purposes and problems, the version and the environment name.
/// Never a connection string, key, URI, account name or exception message.
/// </summary>
/// <param name="Status">Worst status across every check: Healthy, Degraded or Unhealthy.</param>
/// <param name="Environment">Hosting environment name (Development, Testing, Production).</param>
/// <param name="Version">The build version, <c>YYMMDD</c> (the header badge shows it as <c>vYYMMDD</c>).</param>
/// <param name="CheckedAt">When the server ran the checks.</param>
/// <param name="CheckedMs">Server-side time spent running the checks.</param>
/// <param name="Checks">One row per dependency, grouped api / data / service.</param>
public sealed record DiagReportDto(
    string Status,
    string Environment,
    string Version,
    DateTimeOffset CheckedAt,
    double CheckedMs,
    DiagCheckDto[] Checks);

/// <summary>One dependency on the <c>/diag</c> page.</summary>
/// <param name="Name">Stable machine name (e.g. <c>storage</c>).</param>
/// <param name="Group"><see cref="DiagGroups"/>: api, data or service.</param>
/// <param name="Label">Display name, e.g. "Table Storage".</param>
/// <param name="Status">Healthy, Degraded or Unhealthy.</param>
/// <param name="Purpose">One fixed line saying what the dependency is for.</param>
/// <param name="Detail">A short plain-English state ("Working", "Mock provider").</param>
/// <param name="LatencyMs">How long the check took.</param>
/// <param name="Problem">What is wrong, in plain words, when the check is not Healthy; otherwise null.</param>
public sealed record DiagCheckDto(
    string Name,
    string Group,
    string Label,
    string Status,
    string Purpose,
    string Detail,
    double LatencyMs,
    string? Problem);

/// <summary>Values of <see cref="DiagCheckDto.Group"/>, in the order /diag shows them.</summary>
public static class DiagGroups
{
    public const string Api = "api";
    public const string Data = "data";
    public const string Service = "service";
}
