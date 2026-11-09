using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using PoTraffic.Client.Infrastructure.Http;
using PoTraffic.Shared.DTOs.Account;
using PoTraffic.Shared.Enums;

namespace PoTraffic.Client.Infrastructure;

/// <summary>
/// Progressive disclosure: which of the low-traffic panels the signed-in user has opted in to.
///
/// <para>
/// The core experience is the commute list, the live status, the trend chart, the
/// schedule and the weekly heatmap. The panels here sit behind a switch; the account's
/// default (server-side, <c>User.AdvancedPanels</c>) is the route map and weather impact
/// on, card sparklines off.
/// </para>
///
/// <para>
/// Saved on the user's account (<c>/api/account/panels</c>), so a choice follows them to
/// every device. Read once at startup in <c>Program.cs</c> — before the first render, so a
/// page already knows the answer when it draws — and again whenever the auth state changes
/// (a sign-in without a page reload). Signed out, or on any failure, everything reads as
/// "off", which is the default anyway.
/// </para>
///
/// <para>
/// Uses its own HttpClient without the app's 401→login redirect: an anonymous visitor on
/// the login page must not be bounced by a preference read.
/// </para>
/// </summary>
public sealed class UiPreferences : IDisposable
{
    /// <summary>The per-browser keys these switches lived under before they moved to the account.</summary>
    private const string LegacyKeyPrefix = "pt-advanced-";

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private readonly AuthenticationStateProvider _auth;
    private readonly HashSet<AdvancedPanel> _on = [];

    public UiPreferences(NavigationManager nav, IJSRuntime js, AuthenticationStateProvider auth)
    {
        _http = new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
        _js = js;
        _auth = auth;
        _auth.AuthenticationStateChanged += OnAuthChanged;
    }

    /// <summary>Raised after the switches are (re)loaded or changed.</summary>
    public event Action? Changed;

    public bool IsOn(AdvancedPanel panel) => _on.Contains(panel);

    /// <summary>Reads the account's switches. Never throws; signed out reads as all off.</summary>
    public async Task LoadAsync()
    {
        string[]? names = null;
        try
        {
            HttpResponseMessage resp = await _http.GetAsync("/api/account/panels");
            if (resp.IsSuccessStatusCode)
                names = await resp.Content.ReadFromJsonAsync(AppJsonContext.Default.StringArray);
        }
        catch
        {
            // Offline or server down — keep whatever was loaded before.
            return;
        }

        _on.Clear();
        foreach (string name in names ?? [])
        {
            if (Enum.TryParse(name, out AdvancedPanel panel))
                _on.Add(panel);
        }

        if (names is not null)
            await MigrateLegacyAsync();

        Changed?.Invoke();
    }

    public async Task SetAsync(AdvancedPanel panel, bool on)
    {
        if (on) _on.Add(panel);
        else _on.Remove(panel);
        Changed?.Invoke();

        await SaveAsync(panel, on);
    }

    private async Task<bool> SaveAsync(AdvancedPanel panel, bool on)
    {
        try
        {
            HttpResponseMessage resp = await _http.PutAsJsonAsync(
                $"/api/account/panels/{panel}", new AdvancedPanelRequest(on),
                AppJsonContext.Default.AdvancedPanelRequest);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            // Offline — the choice holds for this session.
            return false;
        }
    }

    /// <summary>
    /// One-time move of switches this browser stored before they went on the account: each
    /// "on" is saved to the account (union, never turning anything off) and the local key
    /// is removed once the save lands.
    /// </summary>
    private async Task MigrateLegacyAsync()
    {
        foreach (AdvancedPanel panel in Enum.GetValues<AdvancedPanel>())
        {
            string key = LegacyKeyPrefix + panel.ToString().ToLowerInvariant();
            try
            {
                if (await _js.InvokeAsync<string?>("localStorage.getItem", key) != "on")
                    continue;

                if (_on.Contains(panel) || await SaveAsync(panel, true))
                {
                    _on.Add(panel);
                    await _js.InvokeVoidAsync("localStorage.removeItem", key);
                }
            }
            catch
            {
                // No storage — nothing to migrate.
            }
        }
    }

    private void OnAuthChanged(Task<AuthenticationState> state) => _ = LoadAsync();

    public void Dispose()
    {
        _auth.AuthenticationStateChanged -= OnAuthChanged;
        _http.Dispose();
    }
}
