using System.Text.Json;
using System.Text.Json.Serialization;
using PoTraffic.Shared.DTOs;
using PoTraffic.Shared.DTOs.Account;
using PoTraffic.Shared.DTOs.Admin;
using PoTraffic.Shared.DTOs.Alerts;
using PoTraffic.Shared.DTOs.Auth;
using PoTraffic.Shared.DTOs.History;
using PoTraffic.Shared.DTOs.Routes;

namespace PoTraffic.Client.Services.Http;

/// <summary>
/// Everything the dashboard renders, in one cacheable payload persisted by
/// <see cref="PoTraffic.Client.Services.ClientCache"/>.
/// </summary>
public sealed record DashboardSnapshot(
    List<RouteDto> Routes,
    QuotaDto? Quota,
    List<RouteInsight> Insights);

/// <summary>Per-route dashboard extras, kept as a list because the snapshot round-trips through JSON.</summary>
public sealed record RouteInsight(
    RouteId RouteId,
    OptimalDepartureDto? OptimalDeparture,
    List<PollRecordDto> RecentPolls,
    DeparturePlanDto? Plan = null);

/// <summary>Keys used with <see cref="PoTraffic.Client.Services.ClientCache"/>.</summary>
public static class ClientCacheKeys
{
    public const string Dashboard = "dashboard";
}

/// <summary>
/// Source-generated JSON metadata for every payload the WASM client sends or
/// receives. Required by the trim analyzer.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ProfileDto))]
[JsonSerializable(typeof(QuotaDto))]
[JsonSerializable(typeof(UpdateProfileRequest))]
[JsonSerializable(typeof(AdvancedPanelRequest))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<UserDailyUsageDto>))]
[JsonSerializable(typeof(List<RecentVolatilityPointDto>))]
[JsonSerializable(typeof(List<ConnectionHealthDto>))]
[JsonSerializable(typeof(List<SystemConfigDto>))]
[JsonSerializable(typeof(SpendingDto))]
[JsonSerializable(typeof(SetUserDailyQuotaRequest))]
[JsonSerializable(typeof(UpdateSystemConfigRequest))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(ProvidersResponse))]
[JsonSerializable(typeof(FeatureFlags))]
[JsonSerializable(typeof(AuthMeResponse))]
[JsonSerializable(typeof(PagedResult<RouteDto>))]
[JsonSerializable(typeof(RouteDto))]
[JsonSerializable(typeof(CreateRouteRequest))]
[JsonSerializable(typeof(List<PlaceSuggestionDto>))]
[JsonSerializable(typeof(CheckNowResponse))]
[JsonSerializable(typeof(RoutePathDto))]
[JsonSerializable(typeof(OptimalDepartureDto))]
[JsonSerializable(typeof(List<SessionDto>))]
[JsonSerializable(typeof(PagedResult<PollRecordDto>))]
[JsonSerializable(typeof(VolatilityHeatmapDto))]
[JsonSerializable(typeof(List<AlertDto>))]
[JsonSerializable(typeof(StopSessionRequest))]
[JsonSerializable(typeof(SaveWindowRequest))]
[JsonSerializable(typeof(SetTimeZoneRequest))]
[JsonSerializable(typeof(PushKeyResponse))]
[JsonSerializable(typeof(PushSubscriptionRequest))]
[JsonSerializable(typeof(PushUnsubscribeRequest))]
[JsonSerializable(typeof(SetArriveByRequest))]
[JsonSerializable(typeof(SetRouteNameRequest))]
[JsonSerializable(typeof(SetRoutePausedRequest))]
[JsonSerializable(typeof(SetRouteAddressesRequest))]
[JsonSerializable(typeof(DeparturePlanDto))]
[JsonSerializable(typeof(DepartureDto))]
[JsonSerializable(typeof(WeatherImpactResponse))]
[JsonSerializable(typeof(CreateSampleRouteRequest))]
[JsonSerializable(typeof(DashboardSnapshot))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
