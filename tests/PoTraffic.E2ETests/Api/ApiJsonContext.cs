// filepath: tests/PoTraffic.E2ETests/Api/ApiJsonContext.cs
using System.Text.Json.Serialization;
using PoTraffic.Shared.DTOs.Account;
using PoTraffic.Shared.DTOs.Admin;
using PoTraffic.Shared.DTOs.Alerts;
using PoTraffic.Shared.DTOs.History;
using PoTraffic.Shared.DTOs.Routes;

namespace PoTraffic.E2ETests.Api;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the API scenarios —
/// avoids reflection at runtime, keeps the AOT trim warnings off the E2E project.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PagedResult<RouteDto>))]
[JsonSerializable(typeof(MonitoringWindowDto))]
[JsonSerializable(typeof(RouteDto))]
[JsonSerializable(typeof(SessionDto))]
[JsonSerializable(typeof(List<MonitoringWindowDto>))]
[JsonSerializable(typeof(List<SessionDto>))]
[JsonSerializable(typeof(ApiSessionFactory.GuestLoginResponse))]
[JsonSerializable(typeof(ProfileDto))]
[JsonSerializable(typeof(QuotaDto))]
[JsonSerializable(typeof(List<AlertDto>))]
[JsonSerializable(typeof(List<SystemConfigDto>))]
[JsonSerializable(typeof(PagedResult<PollRecordDto>))]
[JsonSerializable(typeof(VolatilityHeatmapDto))]
[JsonSerializable(typeof(WeatherImpactResponse))]
[JsonSerializable(typeof(Scenarios.RouteCreationSchedulingApiScenarios.ExecutePollBody))]
public sealed partial class ApiJsonContext : JsonSerializerContext;