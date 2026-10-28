using Microsoft.JSInterop;

namespace PoTraffic.Client.Infrastructure;

/// <summary>A panel that is off until the user asks for it in Settings → Advanced panels.</summary>
public enum AdvancedPanel
{
    /// <summary>The route drawn on a map, on the route page.</summary>
    Map,

    /// <summary>What rain and snow cost the route, on the route page.</summary>
    Weather,

    /// <summary>The 24-hour sparkline on each dashboard card.</summary>
    Sparklines,
}

/// <summary>
/// Progressive disclosure: which of the low-traffic panels this browser has opted in to.
///
/// <para>
/// The default experience is the commute list, the live status, the trend chart and the
/// schedule — the parts that answer "when do I leave". Everything else is real and works,
/// but it is evidence for a question most people never ask, so it waits behind a switch.
/// Every panel defaults OFF.
/// </para>
///
/// <para>
/// Stored per browser in <c>localStorage</c> next to the theme and density, not on the
/// server: like those, it is about this screen rather than this account. Read once by
/// <see cref="LoadAsync"/> in <c>Program.cs</c>, before the first render, so a page
/// already knows the answer when it draws and an opted-in panel never pops in a frame
/// late. A storage failure (private mode, blocked site data) reads as "off", which is
/// the default anyway.
/// </para>
/// </summary>
public sealed class UiPreferences
{
    private const string KeyPrefix = "pt-advanced-";

    private readonly IJSRuntime _js;
    private readonly HashSet<AdvancedPanel> _on = [];

    public UiPreferences(IJSRuntime js) => _js = js;

    /// <summary>Raised after a switch is flipped, so a page already showing can follow it.</summary>
    public event Action? Changed;

    public bool IsOn(AdvancedPanel panel) => _on.Contains(panel);

    /// <summary>Reads every stored switch. Called once at startup; never throws.</summary>
    public async Task LoadAsync()
    {
        foreach (AdvancedPanel panel in Enum.GetValues<AdvancedPanel>())
        {
            try
            {
                if (await _js.InvokeAsync<string?>("localStorage.getItem", Key(panel)) == "on")
                    _on.Add(panel);
            }
            catch
            {
                // No storage — every panel stays at its default, off.
            }
        }
    }

    public async Task SetAsync(AdvancedPanel panel, bool on)
    {
        if (on) _on.Add(panel);
        else _on.Remove(panel);
        Changed?.Invoke();

        try
        {
            // Off is the default, so it is stored as the key's absence.
            if (on)
                await _js.InvokeVoidAsync("localStorage.setItem", Key(panel), "on");
            else
                await _js.InvokeVoidAsync("localStorage.removeItem", Key(panel));
        }
        catch
        {
            // Private mode — the choice holds for this session and is forgotten after.
        }
    }

    private static string Key(AdvancedPanel panel) => KeyPrefix + panel.ToString().ToLowerInvariant();
}
