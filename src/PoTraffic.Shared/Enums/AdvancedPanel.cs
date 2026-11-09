namespace PoTraffic.Shared.Enums;

/// <summary>
/// A panel the user switches on or off in Settings → Advanced. Saved on the user's account
/// (by name), so a choice follows them to every device. Map and Weather start on.
/// </summary>
public enum AdvancedPanel
{
    /// <summary>The route drawn on a map, on the route page.</summary>
    Map,

    /// <summary>What rain and snow cost the route, on the route page.</summary>
    Weather,

    /// <summary>The 24-hour sparkline on each dashboard card.</summary>
    Sparklines,
}
