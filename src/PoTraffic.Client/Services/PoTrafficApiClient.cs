using System.Net;
using System.Net.Http.Json;
using PoTraffic.Client.Services.Http;
using PoTraffic.Shared.DTOs;
using PoTraffic.Shared.DTOs.Account;
using PoTraffic.Shared.DTOs.Admin;
using PoTraffic.Shared.DTOs.Alerts;
using PoTraffic.Shared.DTOs.Auth;
using PoTraffic.Shared.DTOs.History;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.Enums;

namespace PoTraffic.Client.Services;

/// <summary>
/// Single typed HTTP client for all PoTraffic API endpoints.
/// Components never construct raw "/api/..." URL strings or bypass <see cref="AppJsonContext"/>.
/// </summary>
public sealed class PoTrafficApiClient(HttpClient http)
{
    // ── Auth & System ──────────────────────────────────────────────────────
    public Task<ProvidersResponse?> GetAuthProvidersAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync("/api/auth/providers", AppJsonContext.Default.ProvidersResponse, ct);

    public Task<HttpResponseMessage> GuestLoginAsync(CancellationToken ct = default) =>
        http.PostAsync("/api/auth/guest-login", content: null, ct);

    public Task<HttpResponseMessage> GetAuthMeAsync(CancellationToken ct = default) =>
        http.GetAsync("/api/auth/me", ct);

    public Task<HttpResponseMessage> LogoutAsync(CancellationToken ct = default) =>
        http.PostAsync("/api/auth/logout", content: null, ct);

    public Task<FeatureFlags?> GetFeatureFlagsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync("/api/system/features", AppJsonContext.Default.FeatureFlags, ct);

    public Task<List<ConnectionHealthDto>?> GetConnectionHealthAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync("/health/connections", AppJsonContext.Default.ListConnectionHealthDto, ct);

    // ── Account & Push ─────────────────────────────────────────────────────
    public Task<ProfileDto?> GetProfileAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync("/api/account/profile", AppJsonContext.Default.ProfileDto, ct);

    public Task<QuotaDto?> GetQuotaAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync("/api/account/quota", AppJsonContext.Default.QuotaDto, ct);

    public Task<HttpResponseMessage> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default) =>
        http.PutAsJsonAsync("/api/account/profile", request, AppJsonContext.Default.UpdateProfileRequest, ct);

    public Task<HttpResponseMessage> SetTimeZoneAsync(string timeZoneId, CancellationToken ct = default) =>
        http.PutAsJsonAsync("/api/account/timezone", new SetTimeZoneRequest(timeZoneId), AppJsonContext.Default.SetTimeZoneRequest, ct);

    public Task<HttpResponseMessage> DeleteAccountAsync(CancellationToken ct = default) =>
        http.DeleteAsync("/api/account/", ct);

    public async Task<string[]?> GetAccountPanelsAsync(CancellationToken ct = default)
    {
        using HttpResponseMessage resp = await http.GetAsync("/api/account/panels", ct);
        return resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync(AppJsonContext.Default.StringArray, ct)
            : null;
    }

    public async Task<bool> SaveAccountPanelAsync(AdvancedPanel panel, bool on, CancellationToken ct = default)
    {
        using HttpResponseMessage resp = await http.PutAsJsonAsync(
            $"/api/account/panels/{panel}",
            new AdvancedPanelRequest(on),
            AppJsonContext.Default.AdvancedPanelRequest,
            ct);
        return resp.IsSuccessStatusCode;
    }

    public Task<PushKeyResponse?> GetPushKeyAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync("/api/push/key", AppJsonContext.Default.PushKeyResponse, ct);

    public Task<HttpResponseMessage> SubscribePushAsync(PushSubscriptionRequest request, CancellationToken ct = default) =>
        http.PostAsJsonAsync("/api/push/subscriptions", request, AppJsonContext.Default.PushSubscriptionRequest, ct);

    public Task<HttpResponseMessage> UnsubscribePushAsync(PushUnsubscribeRequest request, CancellationToken ct = default) =>
        http.PostAsJsonAsync("/api/push/unsubscribe", request, AppJsonContext.Default.PushUnsubscribeRequest, ct);

    // ── Admin ──────────────────────────────────────────────────────────────
    public Task<SpendingDto?> GetAdminSpendingAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync("/api/admin/spending", AppJsonContext.Default.SpendingDto, ct);

    public Task<List<UserDailyUsageDto>?> GetAdminUsersAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync("/api/admin/users", AppJsonContext.Default.ListUserDailyUsageDto, ct);

    public Task<HttpResponseMessage> SetUserDailyQuotaAsync(UserId userId, int? limit, CancellationToken ct = default) =>
        http.PutAsJsonAsync($"/api/admin/users/{userId}/daily-quota", new SetUserDailyQuotaRequest(limit), AppJsonContext.Default.SetUserDailyQuotaRequest, ct);

    public Task<List<RecentVolatilityPointDto>?> GetAdminRecentVolatilityAsync(int hours = 24, CancellationToken ct = default) =>
        http.GetFromJsonAsync($"/api/admin/global-volatility/recent?hours={hours}", AppJsonContext.Default.ListRecentVolatilityPointDto, ct);

    public Task<List<SystemConfigDto>?> GetAdminConfigurationAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync("/api/admin/configuration", AppJsonContext.Default.ListSystemConfigDto, ct);

    public Task<HttpResponseMessage> UpdateAdminConfigurationAsync(string key, string value, CancellationToken ct = default) =>
        http.PutAsJsonAsync($"/api/admin/configuration/{Uri.EscapeDataString(key)}", new UpdateSystemConfigRequest(value), AppJsonContext.Default.UpdateSystemConfigRequest, ct);

    // ── Alerts ─────────────────────────────────────────────────────────────
    public Task<List<AlertDto>?> GetAlertsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync("/api/alerts", AppJsonContext.Default.ListAlertDto, ct);

    public Task<HttpResponseMessage> MarkAlertReadAsync(AlertId alertId, CancellationToken ct = default) =>
        http.PostAsync($"/api/alerts/{alertId}/read", content: null, ct);

    public Task<HttpResponseMessage> MarkAllAlertsReadAsync(CancellationToken ct = default) =>
        http.PostAsync("/api/alerts/read-all", content: null, ct);

    // ── Places ─────────────────────────────────────────────────────────────
    public Task<List<PlaceSuggestionDto>?> SearchPlacesAsync(string query, CancellationToken ct = default) =>
        http.GetFromJsonAsync($"/api/places/autocomplete?q={Uri.EscapeDataString(query)}", AppJsonContext.Default.ListPlaceSuggestionDto, ct);

    // ── Routes & Windows & History ─────────────────────────────────────────
    public Task<PagedResult<RouteDto>?> GetRoutesAsync(int page = 1, int pageSize = 100, CancellationToken ct = default) =>
        http.GetFromJsonAsync($"/api/routes?page={page}&pageSize={pageSize}", AppJsonContext.Default.PagedResultRouteDto, ct);

    public async Task<(RouteDto? Route, bool NotFound)> GetRouteAsync(RouteId routeId, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.GetAsync($"/api/routes/{routeId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return (null, true);
        response.EnsureSuccessStatusCode();
        RouteDto? dto = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.RouteDto, ct);
        return (dto, false);
    }

    public Task<HttpResponseMessage> CreateRouteAsync(CreateRouteRequest request, CancellationToken ct = default) =>
        http.PostAsJsonAsync("/api/routes", request, AppJsonContext.Default.CreateRouteRequest, ct);

    public Task<HttpResponseMessage> CreateSampleRouteAsync(int utcOffsetMinutes, CancellationToken ct = default) =>
        http.PostAsJsonAsync("/api/routes/sample", new CreateSampleRouteRequest(utcOffsetMinutes), AppJsonContext.Default.CreateSampleRouteRequest, ct);

    public Task<HttpResponseMessage> DeleteRouteAsync(RouteId routeId, CancellationToken ct = default) =>
        http.DeleteAsync($"/api/routes/{routeId}", ct);

    public Task<HttpResponseMessage> SetRoutePausedAsync(RouteId routeId, bool paused, CancellationToken ct = default) =>
        http.PutAsJsonAsync($"/api/routes/{routeId}/paused", new SetRoutePausedRequest(paused), AppJsonContext.Default.SetRoutePausedRequest, ct);

    public Task<HttpResponseMessage> SetRouteNameAsync(RouteId routeId, string? name, CancellationToken ct = default) =>
        http.PutAsJsonAsync($"/api/routes/{routeId}/name", new SetRouteNameRequest(name), AppJsonContext.Default.SetRouteNameRequest, ct);

    public Task<HttpResponseMessage> SetRouteAddressesAsync(RouteId routeId, SetRouteAddressesRequest request, CancellationToken ct = default) =>
        http.PutAsJsonAsync($"/api/routes/{routeId}/addresses", request, AppJsonContext.Default.SetRouteAddressesRequest, ct);

    public Task<HttpResponseMessage> SetArriveByAsync(RouteId routeId, string? arriveBy, CancellationToken ct = default) =>
        http.PutAsJsonAsync($"/api/routes/{routeId}/arrive-by", new SetArriveByRequest(arriveBy), AppJsonContext.Default.SetArriveByRequest, ct);

    public Task<HttpResponseMessage> CreateReturnTripAsync(RouteId routeId, CancellationToken ct = default) =>
        http.PostAsync($"/api/routes/{routeId}/return-trip", content: null, ct);

    public Task<HttpResponseMessage> CheckNowAsync(RouteId routeId, CancellationToken ct = default) =>
        http.PostAsync($"/api/routes/{routeId}/check-now", content: null, ct);

    public Task<HttpResponseMessage> SaveWindowAsync(RouteId routeId, SaveWindowRequest request, CancellationToken ct = default) =>
        http.PostAsJsonAsync($"/api/routes/{routeId}/windows", request, AppJsonContext.Default.SaveWindowRequest, ct);

    public Task<HttpResponseMessage> StartWindowAsync(RouteId routeId, WindowId windowId, CancellationToken ct = default) =>
        http.PostAsync($"/api/routes/{routeId}/windows/{windowId}/start", content: null, ct);

    public Task<HttpResponseMessage> StopWindowAsync(RouteId routeId, WindowId windowId, SessionId sessionId, CancellationToken ct = default) =>
        http.PostAsJsonAsync($"/api/routes/{routeId}/windows/{windowId}/stop", new StopSessionRequest(sessionId), AppJsonContext.Default.StopSessionRequest, ct);

    public Task<List<SessionDto>?> GetSessionsAsync(RouteId routeId, CancellationToken ct = default) =>
        http.GetFromJsonAsync($"/api/routes/{routeId}/sessions", AppJsonContext.Default.ListSessionDto, ct);

    public Task<PagedResult<PollRecordDto>?> GetPollHistoryAsync(RouteId routeId, int page, int pageSize, string sinceUtc, CancellationToken ct = default) =>
        http.GetFromJsonAsync($"/api/routes/{routeId}/poll-history?page={page}&pageSize={pageSize}&sinceUtc={Uri.EscapeDataString(sinceUtc)}", AppJsonContext.Default.PagedResultPollRecordDto, ct);

    public Task<VolatilityHeatmapDto?> GetHeatmapAsync(RouteId routeId, CancellationToken ct = default) =>
        http.GetFromJsonAsync($"/api/routes/{routeId}/heatmap", AppJsonContext.Default.VolatilityHeatmapDto, ct);

    public Task<WeatherImpactResponse?> GetWeatherImpactAsync(RouteId routeId, CancellationToken ct = default) =>
        http.GetFromJsonAsync($"/api/routes/{routeId}/weather-impact", AppJsonContext.Default.WeatherImpactResponse, ct);

    public async Task<RoutePathDto?> GetRoutePathOptionalAsync(RouteId routeId, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.GetAsync($"/api/routes/{routeId}/path", ct);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.RoutePathDto, ct);
    }

    public async Task<DeparturePlanDto?> GetDeparturePlanOptionalAsync(RouteId routeId, int dayOfWeek, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.GetAsync($"/api/routes/{routeId}/departure?dayOfWeek={dayOfWeek}", ct);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.DeparturePlanDto, ct);
    }

    public async Task<DepartureDto?> GetDepartureOptionalAsync(RouteId routeId, int dayOfWeek, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.GetAsync($"/api/routes/{routeId}/departure?dayOfWeek={dayOfWeek}", ct);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.DepartureDto, ct);
    }

    public async Task<OptimalDepartureDto?> GetOptimalDepartureOptionalAsync(RouteId routeId, int dayOfWeek, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.GetAsync($"/api/routes/{routeId}/optimal-departure?dayOfWeek={dayOfWeek}", ct);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.OptimalDepartureDto, ct);
    }
}
