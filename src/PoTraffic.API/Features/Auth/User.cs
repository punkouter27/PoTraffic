using System.Text.Json.Serialization;

namespace PoTraffic.API.Features.Auth;

public sealed class User
{
    public UserId Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// IANA zone the browser reports (e.g. <c>America/Los_Angeles</c>), synced on every app
    /// load. Per-slot statistics bucket in this zone; null until the first sync, when the
    /// zone is guessed from <see cref="Locale"/> (see <c>UserTime.ZoneFor</c>).
    /// </summary>
    public string? TimeZoneId { get; set; }
    public bool IsGdprDeleteRequested { get; set; }
    public bool IsEmailVerified { get; set; }

    /// <summary>"Commuter" or "Administrator"</summary>
    public string Role { get; set; } = "Commuter";

    /// <summary>
    /// Per-user daily session limit set by an administrator. Null means the global
    /// <c>quota.daily.default</c> applies (see <c>PollRouteJob.DailySessionQuota</c>).
    /// </summary>
    public int? DailySessionQuota { get; set; }

    /// <summary>
    /// Identity provider that minted the most recent session for this user.
    /// One of: "guest", "microsoft". Used by the production Microsoft-only
    /// auth policy (Rule 13).
    /// </summary>
    public string AuthProvider { get; set; } = "microsoft";

    /// <summary>
    /// Advanced panels (Settings → Advanced) the user has switched on, by
    /// <c>AdvancedPanel</c> name. Defaults to the route map and weather impact on, card
    /// sparklines off. A stored row without this field (any account from before it existed)
    /// hydrates with the same default; an empty list means the user switched everything off.
    /// </summary>
    public List<string> AdvancedPanels { get; set; } = ["Map", "Weather"];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    [JsonIgnore]
    public ICollection<EntityRoute> Routes { get; set; } = new List<EntityRoute>();
}
