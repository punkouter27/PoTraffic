namespace PoTraffic.API.Features.Config;

/// <summary>
/// How many billed Google calls of one kind were made in one UTC month, for the calls that
/// leave no other trace. Scheduled polls are not counted here: each one already writes a
/// <c>PollRecord</c>, and <see cref="SpendingCap"/> prices those directly.
/// </summary>
public sealed class ProviderUsage
{
    /// <summary>Row key, <c>"{Month}|{Kind}"</c>, e.g. <c>"2026-10|Geocode"</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>UTC month, <c>"yyyy-MM"</c>.</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>One of <see cref="ProviderCallKind"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public int Count { get; set; }
}

/// <summary>The billed call kinds counted in <see cref="ProviderUsage"/>.</summary>
public static class ProviderCallKind
{
    /// <summary>An on-demand Distance Matrix call ("Check now").</summary>
    public const string CheckNow = "CheckNow";

    /// <summary>A Geocoding call (an address resolved when a route is created).</summary>
    public const string Geocode = "Geocode";

    /// <summary>A Directions call (the road shape drawn on the route map).</summary>
    public const string Directions = "Directions";

    /// <summary>A Places Autocomplete request (address suggestions while typing).</summary>
    public const string Autocomplete = "Autocomplete";
}
