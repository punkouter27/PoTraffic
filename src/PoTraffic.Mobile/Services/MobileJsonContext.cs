using System.Text.Json;
using System.Text.Json.Serialization;
using PoTraffic.Shared.DTOs.Account;
using PoTraffic.Shared.DTOs.Admin;
using PoTraffic.Shared.DTOs.Alerts;
using PoTraffic.Shared.DTOs.Auth;
using PoTraffic.Shared.DTOs.History;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.DTOs;

namespace PoTraffic.Mobile.Services;

/// <summary>Everything the dashboard needs to paint before the network answers.</summary>
public sealed record DashboardSnapshot(
    List<RouteDto> Routes,
    QuotaDto? Quota,
    Dictionary<string, OptimalDepartureDto> Departures,
    Dictionary<string, DeparturePlanDto> Plans,
    Dictionary<string, List<PollRecordDto>> RecentPolls,
    DateTimeOffset SavedAt);

/// <summary>A leave-by notification armed with the OS (kept so a reboot can re-arm it).</summary>
public sealed record ScheduledLeaveAlert(int Id, string RouteId, DateTimeOffset FireAt, string Title, string Body);

/// <summary>A commute whose start point (and optionally end point) is watched.</summary>
public sealed record CommuteFence(string RouteId, string Label, double OriginLat, double OriginLng, double DestLat, double DestLng, bool StopOnArrival);

/// <summary>Source-generated JSON: trim-safe in Release, web defaults like the API.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AuthMeResponse))]
[JsonSerializable(typeof(ProvidersResponse))]
[JsonSerializable(typeof(FeatureFlags))]
[JsonSerializable(typeof(PagedResult<RouteDto>))]
[JsonSerializable(typeof(RouteDto))]
[JsonSerializable(typeof(RoutePathDto))]
[JsonSerializable(typeof(CreateRouteRequest))]
[JsonSerializable(typeof(CreateSampleRouteRequest))]
[JsonSerializable(typeof(SetRoutePausedRequest))]
[JsonSerializable(typeof(SetRouteNameRequest))]
[JsonSerializable(typeof(SetRouteAddressesRequest))]
[JsonSerializable(typeof(SetArriveByRequest))]
[JsonSerializable(typeof(SaveWindowRequest))]
[JsonSerializable(typeof(StopSessionRequest))]
[JsonSerializable(typeof(CheckNowResponse))]
[JsonSerializable(typeof(List<PlaceSuggestionDto>))]
[JsonSerializable(typeof(DepartureDto))]
[JsonSerializable(typeof(PagedResult<PollRecordDto>))]
[JsonSerializable(typeof(List<SessionDto>))]
[JsonSerializable(typeof(VolatilityHeatmapDto))]
[JsonSerializable(typeof(WeatherImpactResponse))]
[JsonSerializable(typeof(List<AlertDto>))]
[JsonSerializable(typeof(ProfileDto))]
[JsonSerializable(typeof(QuotaDto))]
[JsonSerializable(typeof(UpdateProfileRequest))]
[JsonSerializable(typeof(SetTimeZoneRequest))]
[JsonSerializable(typeof(AdvancedPanelRequest))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(DiagReportDto))]
[JsonSerializable(typeof(SpendingDto))]
[JsonSerializable(typeof(List<UserDailyUsageDto>))]
[JsonSerializable(typeof(SetUserDailyQuotaRequest))]
[JsonSerializable(typeof(List<RecentVolatilityPointDto>))]
[JsonSerializable(typeof(List<SystemConfigDto>))]
[JsonSerializable(typeof(UpdateSystemConfigRequest))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(DashboardSnapshot))]
[JsonSerializable(typeof(List<ScheduledLeaveAlert>))]
[JsonSerializable(typeof(List<CommuteFence>))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class MobileJsonContext : JsonSerializerContext;
