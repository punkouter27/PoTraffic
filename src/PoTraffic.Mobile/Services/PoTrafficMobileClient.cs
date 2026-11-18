using System.Net.Http.Json;
using PoTraffic.Shared.DTOs;
using PoTraffic.Shared.DTOs.Alerts;
using PoTraffic.Shared.DTOs.Auth;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.Ids;

namespace PoTraffic.Mobile.Services;

public sealed class PoTrafficMobileClient(HttpClient http, MobileConfig config, OfflineRouteCache cache)
{
    private Uri BuildUri(string relativePath) => new(new Uri(config.BaseUrl + "/"), relativePath.TrimStart('/'));

    public async Task<AuthMeResponse?> GetMeAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<AuthMeResponse>(BuildUri("/api/auth/me"), ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<RouteDto>> GetRoutesAsync(CancellationToken ct = default)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            return await cache.LoadRoutesAsync(ct).ConfigureAwait(false);
        }

        try
        {
            var page = await http.GetFromJsonAsync<PagedResult<RouteDto>>(BuildUri("/api/routes?page=1&pageSize=20"), ct).ConfigureAwait(false);
            var items = page?.Items ?? [];
            await cache.SaveRoutesAsync(items, ct).ConfigureAwait(false);
            return items;
        }
        catch (HttpRequestException)
        {
            return await cache.LoadRoutesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<CheckNowResponse?> CheckNowAsync(RouteId routeId, CancellationToken ct = default)
    {
        using var resp = await http.PostAsync(BuildUri($"/api/routes/{routeId.Value}/check-now"), content: null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<CheckNowResponse>(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(CancellationToken ct = default)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            return await cache.LoadAlertsAsync(ct).ConfigureAwait(false);
        }

        try
        {
            var items = await http.GetFromJsonAsync<List<AlertDto>>(BuildUri("/api/alerts"), ct).ConfigureAwait(false) ?? [];
            await cache.SaveAlertsAsync(items, ct).ConfigureAwait(false);
            return items;
        }
        catch (HttpRequestException)
        {
            return await cache.LoadAlertsAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task MarkAllAlertsReadAsync(CancellationToken ct = default)
    {
        using var resp = await http.PostAsync(BuildUri("/api/alerts/read-all"), content: null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }
}
