using System.Net.Http.Json;
using System.Security.Claims;
using PoTraffic.Client.Services.Http;
using Microsoft.AspNetCore.Components.Authorization;
using PoTraffic.Shared.DTOs.Auth;

namespace PoTraffic.Client.Services.Auth;

/// <summary>
/// BFF <see cref="AuthenticationStateProvider"/> — the session lives in a
/// server-managed HttpOnly cookie the WASM app can never read. Auth state is
/// derived from GET /api/auth/me; the client holds no tokens.
/// </summary>
public sealed class CookieAuthenticationStateProvider(PoTrafficApiClient api, ClientCache cache)
    : AuthenticationStateProvider
{
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            using HttpResponseMessage response = await api.GetAuthMeAsync();
            if (!response.IsSuccessStatusCode)
                return Anonymous();

            AuthMeResponse? me = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.AuthMeResponse);
            if (me is null || me.UserId.IsEmpty)
                return Anonymous();

            cache.UseScope(me.UserId.ToString());

            if (!_zoneReported)
            {
                _zoneReported = true;
                _ = ReportTimeZoneAsync();
            }

            ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, me.UserId.ToString()),
                new Claim(ClaimTypes.Name, me.Email),
                new Claim(ClaimTypes.Email, me.Email),
                new Claim("email", me.Email),
                new Claim(ClaimTypes.Role, me.Role),
                new Claim("auth_provider", me.AuthProvider),
            ], authenticationType: "cookie", ClaimTypes.Name, ClaimTypes.Role);

            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (HttpRequestException)
        {
            return Anonymous();
        }
    }

    private bool _zoneReported;

    private async Task ReportTimeZoneAsync()
    {
        try
        {
            using HttpResponseMessage _ = await api.SetTimeZoneAsync(TimeZoneInfo.Local.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
    }

    public async Task LogoutAsync()
    {
        try
        {
            using HttpResponseMessage _ = await api.LogoutAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
        }

        await cache.ClearAllAsync();
        _zoneReported = false;

        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous()));
    }

    public void Refresh() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    private static AuthenticationState Anonymous() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));
}
