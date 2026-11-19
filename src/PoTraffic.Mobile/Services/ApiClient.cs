using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PoTraffic.Shared.DTOs;
using PoTraffic.Shared.DTOs.Account;
using PoTraffic.Shared.DTOs.Admin;
using PoTraffic.Shared.DTOs.Alerts;
using PoTraffic.Shared.DTOs.Auth;
using PoTraffic.Shared.DTOs.History;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.Enums;
using PoTraffic.Shared.Ids;

namespace PoTraffic.Mobile.Services;

/// <summary>
/// Every call the web client makes, against the same API (mirrors PoTraffic.Client's
/// PoTrafficApiClient). Reads return null for 204/404; writes return the response so callers
/// can read status codes and problem details.
/// </summary>
public sealed class ApiClient(HttpClient http, MobileConfig config)
{
    private static readonly MobileJsonContext Json = MobileJsonContext.Default;

    public static bool Online => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    private Uri U(string path) => new(new Uri(config.BaseUrl + "/"), path.TrimStart('/'));

    private async Task<T?> GetAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken ct = default)
    {
        using HttpResponseMessage resp = await http.GetAsync(U(path), ct).ConfigureAwait(false);
        if (resp.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
            return default;
        }

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync(type, ct).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> PostAsync(string path) => http.PostAsync(U(path), null);

    private Task<HttpResponseMessage> PostAsync<T>(string path, T body, JsonTypeInfo<T> type) => http.PostAsJsonAsync(U(path), body, type);

    private Task<HttpResponseMessage> PutAsync<T>(string path, T body, JsonTypeInfo<T> type) => http.PutAsJsonAsync(U(path), body, type);

    // ── Auth ──
    public Task<ProvidersResponse?> GetProvidersAsync() => GetAsync("/api/auth/providers", Json.ProvidersResponse);

    public Task<HttpResponseMessage> GuestLoginAsync() => PostAsync("/api/auth/guest-login");

    public Task<AuthMeResponse?> GetMeAsync(CancellationToken ct = default) => GetAsync("/api/auth/me", Json.AuthMeResponse, ct);

    public Task<HttpResponseMessage> LogoutAsync() => PostAsync("/api/auth/logout");

    public Task<FeatureFlags?> GetFeatureFlagsAsync() => GetAsync("/api/system/features", Json.FeatureFlags);

    public Task<DiagReportDto?> GetDiagAsync() => GetAsync("/api/diag", Json.DiagReportDto);

    // ── Account ──
    public Task<ProfileDto?> GetProfileAsync() => GetAsync("/api/account/profile", Json.ProfileDto);

    public Task<QuotaDto?> GetQuotaAsync(CancellationToken ct = default) => GetAsync("/api/account/quota", Json.QuotaDto, ct);

    public Task<HttpResponseMessage> UpdateProfileAsync(string locale) => PutAsync("/api/account/profile", new UpdateProfileRequest(locale), Json.UpdateProfileRequest);

    public Task<HttpResponseMessage> SetTimeZoneAsync(string zoneId) => PutAsync("/api/account/timezone", new SetTimeZoneRequest(zoneId), Json.SetTimeZoneRequest);

    public Task<string[]?> GetPanelsAsync() => GetAsync("/api/account/panels", Json.StringArray);

    public Task<HttpResponseMessage> SetPanelAsync(AdvancedPanel panel, bool on) => PutAsync($"/api/account/panels/{panel}", new AdvancedPanelRequest(on), Json.AdvancedPanelRequest);

    public Task<HttpResponseMessage> ExportAccountAsync() => http.GetAsync(U("/api/account/export"));

    public Task<HttpResponseMessage> DeleteAccountAsync() => http.DeleteAsync(U("/api/account/"));

    // ── Admin ──
    public Task<SpendingDto?> GetSpendingAsync() => GetAsync("/api/admin/spending", Json.SpendingDto);

    public Task<List<UserDailyUsageDto>?> GetUsersAsync() => GetAsync("/api/admin/users", Json.ListUserDailyUsageDto);

    public Task<HttpResponseMessage> SetUserQuotaAsync(UserId userId, int? limit) => PutAsync($"/api/admin/users/{userId.Value}/daily-quota", new SetUserDailyQuotaRequest(limit), Json.SetUserDailyQuotaRequest);

    public Task<List<RecentVolatilityPointDto>?> GetRecentVolatilityAsync(int hours = 24) => GetAsync($"/api/admin/global-volatility/recent?hours={hours}", Json.ListRecentVolatilityPointDto);

    public Task<List<SystemConfigDto>?> GetConfigurationAsync() => GetAsync("/api/admin/configuration", Json.ListSystemConfigDto);

    public Task<HttpResponseMessage> SetConfigurationAsync(string key, string value) => PutAsync($"/api/admin/configuration/{Uri.EscapeDataString(key)}", new UpdateSystemConfigRequest(value), Json.UpdateSystemConfigRequest);

    // ── Alerts ──
    public Task<List<AlertDto>?> GetAlertsAsync(CancellationToken ct = default) => GetAsync("/api/alerts", Json.ListAlertDto, ct);

    public Task<HttpResponseMessage> MarkAlertReadAsync(AlertId id) => PostAsync($"/api/alerts/{id.Value}/read");

    public Task<HttpResponseMessage> MarkAllAlertsReadAsync() => PostAsync("/api/alerts/read-all");

    // ── Routes ──
    public Task<List<PlaceSuggestionDto>?> SearchPlacesAsync(string q, CancellationToken ct) => GetAsync($"/api/places/autocomplete?q={Uri.EscapeDataString(q)}", Json.ListPlaceSuggestionDto, ct);

    public Task<PagedResult<RouteDto>?> GetRoutesAsync(CancellationToken ct = default) => GetAsync("/api/routes?page=1&pageSize=100", Json.PagedResultRouteDto, ct);

    public Task<RouteDto?> GetRouteAsync(RouteId id, CancellationToken ct = default) => GetAsync($"/api/routes/{id.Value}", Json.RouteDto, ct);

    public Task<HttpResponseMessage> CreateRouteAsync(CreateRouteRequest request) => PostAsync("/api/routes", request, Json.CreateRouteRequest);

    public Task<HttpResponseMessage> CreateSampleRouteAsync() => PostAsync("/api/routes/sample", new CreateSampleRouteRequest((int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes), Json.CreateSampleRouteRequest);

    public Task<HttpResponseMessage> DeleteRouteAsync(RouteId id) => http.DeleteAsync(U($"/api/routes/{id.Value}"));

    public Task<HttpResponseMessage> SetPausedAsync(RouteId id, bool paused) => PutAsync($"/api/routes/{id.Value}/paused", new SetRoutePausedRequest(paused), Json.SetRoutePausedRequest);

    public Task<HttpResponseMessage> SetNameAsync(RouteId id, string? name) => PutAsync($"/api/routes/{id.Value}/name", new SetRouteNameRequest(name), Json.SetRouteNameRequest);

    public Task<HttpResponseMessage> SetAddressesAsync(RouteId id, string origin, string destination) => PutAsync($"/api/routes/{id.Value}/addresses", new SetRouteAddressesRequest(origin, destination), Json.SetRouteAddressesRequest);

    public Task<HttpResponseMessage> SetArriveByAsync(RouteId id, string? hhmm) => PutAsync($"/api/routes/{id.Value}/arrive-by", new SetArriveByRequest(hhmm), Json.SetArriveByRequest);

    public Task<HttpResponseMessage> CreateReturnTripAsync(RouteId id) => PostAsync($"/api/routes/{id.Value}/return-trip");

    public async Task<(CheckNowResponse? Result, HttpStatusCode Status)> CheckNowAsync(RouteId id)
    {
        using HttpResponseMessage resp = await PostAsync($"/api/routes/{id.Value}/check-now").ConfigureAwait(false);
        return resp.IsSuccessStatusCode
            ? (await resp.Content.ReadFromJsonAsync(Json.CheckNowResponse).ConfigureAwait(false), resp.StatusCode)
            : (null, resp.StatusCode);
    }

    public Task<RoutePathDto?> GetPathAsync(RouteId id) => GetAsync($"/api/routes/{id.Value}/path", Json.RoutePathDto);

    // ── Monitoring windows ──
    public Task<HttpResponseMessage> SaveWindowAsync(RouteId id, SaveWindowRequest request) => PostAsync($"/api/routes/{id.Value}/windows", request, Json.SaveWindowRequest);

    public Task<HttpResponseMessage> StartWindowAsync(RouteId id, WindowId windowId) => PostAsync($"/api/routes/{id.Value}/windows/{windowId.Value}/start");

    public Task<HttpResponseMessage> StopWindowAsync(RouteId id, WindowId windowId, SessionId sessionId) => PostAsync($"/api/routes/{id.Value}/windows/{windowId.Value}/stop", new StopSessionRequest(sessionId), Json.StopSessionRequest);

    // ── History ──
    public Task<List<SessionDto>?> GetSessionsAsync(RouteId id, CancellationToken ct = default) => GetAsync($"/api/routes/{id.Value}/sessions", Json.ListSessionDto, ct);

    public Task<PagedResult<PollRecordDto>?> GetPollHistoryAsync(RouteId id, int pageSize, DateTime sinceUtc, CancellationToken ct = default)
        => GetAsync($"/api/routes/{id.Value}/poll-history?page=1&pageSize={pageSize}&sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("o"))}", Json.PagedResultPollRecordDto, ct);

    public Task<DepartureDto?> GetDepartureAsync(RouteId id, DayOfWeek day, CancellationToken ct = default) => GetAsync($"/api/routes/{id.Value}/departure?dayOfWeek={(int)day}", Json.DepartureDto, ct);

    public Task<VolatilityHeatmapDto?> GetHeatmapAsync(RouteId id) => GetAsync($"/api/routes/{id.Value}/heatmap", Json.VolatilityHeatmapDto);

    public Task<WeatherImpactResponse?> GetWeatherImpactAsync(RouteId id) => GetAsync($"/api/routes/{id.Value}/weather-impact", Json.WeatherImpactResponse);

    /// <summary>The API's error text: <c>error</c>/<c>detail</c>/<c>title</c> from a JSON problem body.</summary>
    public static async Task<(string? Code, string Message)> ReadProblemAsync(HttpResponseMessage resp, string fallback)
    {
        try
        {
            JsonElement body = await resp.Content.ReadFromJsonAsync(Json.JsonElement).ConfigureAwait(false);
            string? Prop(string name) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string? firstValidation = body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Object
                ? errors.EnumerateObject().SelectMany(p => p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray() : []).Select(e => e.GetString()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                : null;
            return (Prop("error"), firstValidation ?? Prop("detail") ?? Prop("title") ?? fallback);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return (null, fallback);
        }
    }

    /// <summary>The window id a save returns (<c>{ "windowId": … }</c>).</summary>
    public static async Task<WindowId?> ReadWindowIdAsync(HttpResponseMessage resp)
    {
        JsonElement body = await resp.Content.ReadFromJsonAsync(Json.JsonElement).ConfigureAwait(false);
        return body.TryGetProperty("windowId", out JsonElement id) && id.TryGetGuid(out Guid g) ? WindowId.From(g) : null;
    }
}
