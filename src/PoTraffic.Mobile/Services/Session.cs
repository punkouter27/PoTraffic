using PoTraffic.Shared.DTOs.Auth;
using PoTraffic.Shared.Enums;
using PoTraffic.Shared.Ids;

namespace PoTraffic.Mobile.Services;

/// <summary>Who is signed in, and their display choices — refreshed from /api/auth/me and /api/account/panels.</summary>
public sealed class Session(ApiClient api, SecureSessionStore tokens)
{
    private const string PanelsKey = "potraffic_panels";

    public AuthMeResponse? Me { get; private set; }

    public bool SignedIn => Me is { } me && me.UserId != UserId.Empty;

    public bool IsAdmin => string.Equals(Me?.Role, "Administrator", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extra panels the user turned on (Map, Weather, Sparklines); remembered for offline starts.</summary>
    public HashSet<AdvancedPanel> Panels { get; private set; } =
        [.. Preferences.Default.Get(PanelsKey, string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => Enum.TryParse(p, out AdvancedPanel x) ? x : (AdvancedPanel?)null).OfType<AdvancedPanel>()];

    public bool PanelOn(AdvancedPanel panel) => Panels.Contains(panel);

    /// <summary>Refreshes identity and panels; offline keeps the last known state.</summary>
    public async Task<bool> RefreshAsync()
    {
        if (!ApiClient.Online && Me is not null)
        {
            return SignedIn;
        }

        try
        {
            Me = await api.GetMeAsync();
            if (SignedIn && await api.GetPanelsAsync() is { } panels)
            {
                SetPanels(panels.Select(p => Enum.TryParse(p, true, out AdvancedPanel x) ? x : (AdvancedPanel?)null).OfType<AdvancedPanel>());
            }
        }
        catch (HttpRequestException)
        {
            // Offline or server down: a stored token still counts as signed in, so the cached
            // dashboard shows instead of the sign-in page.
            if (Me is null && !string.IsNullOrEmpty(await tokens.GetBearerTokenAsync()))
            {
                return true;
            }
        }

        return SignedIn;
    }

    public void SetPanels(IEnumerable<AdvancedPanel> panels)
    {
        Panels = [.. panels];
        Preferences.Default.Set(PanelsKey, string.Join(',', Panels));
    }

    public void Clear() => Me = null;
}
