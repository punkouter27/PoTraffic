using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using PoTraffic.Shared.Enums;

namespace PoTraffic.Client.Services;

/// <summary>
/// Progressive disclosure: which of the low-traffic panels the signed-in user has opted in to.
/// </summary>
public sealed class UiPreferences : IDisposable
{
    private const string LegacyKeyPrefix = "potraffic:advanced-";

    private readonly PoTrafficApiClient _api;
    private readonly IJSRuntime _js;
    private readonly AuthenticationStateProvider _auth;
    private readonly HashSet<AdvancedPanel> _on = [];

    public UiPreferences(PoTrafficApiClient api, IJSRuntime js, AuthenticationStateProvider auth)
    {
        _api = api;
        _js = js;
        _auth = auth;
        _auth.AuthenticationStateChanged += OnAuthChanged;
    }

    public event Action? Changed;

    public bool IsOn(AdvancedPanel panel) => _on.Contains(panel);

    public async Task LoadAsync()
    {
        AuthenticationState authState = await _auth.GetAuthenticationStateAsync();
        if (authState.User.Identity?.IsAuthenticated != true)
        {
            _on.Clear();
            Changed?.Invoke();
            return;
        }

        string[]? names = null;
        try
        {
            names = await _api.GetAccountPanelsAsync();
        }
        catch
        {
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
            return await _api.SaveAccountPanelAsync(panel, on);
        }
        catch
        {
            return false;
        }
    }

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
            }
        }
    }

    private void OnAuthChanged(Task<AuthenticationState> state) => _ = LoadAsync();

    public void Dispose() => _auth.AuthenticationStateChanged -= OnAuthChanged;
}
