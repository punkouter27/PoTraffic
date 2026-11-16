using System.Globalization;
using PoTraffic.Api.Infrastructure.Storage;

namespace PoTraffic.Api.Features.Config;

/// <summary>One administrator-editable <see cref="SystemConfiguration"/> row.</summary>
/// <param name="Key">Row key, e.g. <c>cost.perpoll.googlemaps</c>.</param>
/// <param name="DefaultValue">Seeded at startup when the row is missing; also the fallback for an unparseable row.</param>
/// <param name="Description">Shown next to the value on the admin page.</param>
/// <param name="IsValid">The check a new value must pass before <c>PUT /api/admin/configuration/{key}</c> stores it.</param>
public sealed record SystemSetting(string Key, string DefaultValue, string Description, Func<string, bool> IsValid);

/// <summary>
/// The catalogue of runtime settings: every cost rate, limit and feature flag an administrator
/// can change without a redeploy. Seeding (<see cref="TableStorageContext.SeedDefaultConfigurationsIfMissing"/>),
/// validation (<c>UpdateSystemConfigurationValidator</c>) and every reader go through this one
/// list, so a new setting is one entry here and nothing can be editable but unseeded, or seeded
/// but unvalidated.
/// </summary>
public static class SystemSettings
{
    public const string DailyQuotaKey = "quota.daily.default";
    public const string MonthlyCapKey = "cost.monthly.cap.usd";
    public const string PerPollKey = "cost.perpoll.googlemaps";
    public const string PerGeocodeKey = "cost.pergeocode.googlemaps";
    public const string PerDirectionsKey = "cost.perdirections.googlemaps";
    public const string PerAutocompleteKey = "cost.perautocomplete.googlemaps";
    public const string UseMockProvidersKey = "feature.mockproviders.enabled";
    public const string EnableWeatherKey = "feature.weather.enabled";

    /// <summary>Every editable setting with its seeded default (Google's list prices per request for the rates).</summary>
    public static readonly IReadOnlyList<SystemSetting> All =
    [
        new(PerPollKey, "0.005", "Cost per poll - Google Maps", IsNonNegativeDecimal),
        new(PerGeocodeKey, "0.005", "Cost per geocode - Google Maps", IsNonNegativeDecimal),
        new(PerDirectionsKey, "0.005", "Cost per directions request - Google Maps", IsNonNegativeDecimal),
        new(PerAutocompleteKey, "0.00283", "Cost per address autocomplete request - Google Places", IsNonNegativeDecimal),
        new(MonthlyCapKey, "100", "Monthly provider spending cap (USD); polling pauses when reached, 0 = no cap", IsNonNegativeDecimal),
        new(DailyQuotaKey, QuotaConstants.DefaultDailyQuota.ToString(CultureInfo.InvariantCulture),
            "Default daily session quota per user",
            v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                 && n is >= 1 and <= SetUserDailyQuotaValidator.MaxQuota),
        new(UseMockProvidersKey, "false",
            "Use mock traffic, weather and holiday providers instead of the billed live ones (true/false). " +
            "Always on in Testing, or when the host sets Features:UseMockProviders=true.",
            IsBool),
        new(EnableWeatherKey, "true", "Record the weather at the origin with every poll (true/false)", IsBool),
    ];

    public static readonly IReadOnlyDictionary<string, SystemSetting> ByKey =
        All.ToDictionary(s => s.Key, StringComparer.Ordinal);

    /// <summary>The stored value, parsed; null when the row is missing or unparseable.</summary>
    public static decimal? ReadDecimal(TableStorageContext db, string key)
        => decimal.TryParse(Raw(db, key), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;

    /// <summary>The stored value, or the seeded default when the row is missing or unparseable.</summary>
    public static decimal DecimalOrDefault(TableStorageContext db, string key)
        => ReadDecimal(db, key) ?? decimal.Parse(ByKey[key].DefaultValue, CultureInfo.InvariantCulture);

    /// <inheritdoc cref="DecimalOrDefault"/>
    public static bool BoolOrDefault(TableStorageContext db, string key)
        => bool.TryParse(Raw(db, key), out bool value) ? value : bool.Parse(ByKey[key].DefaultValue);

    /// <summary>The stored value when it is a positive integer, otherwise the seeded default.</summary>
    public static int PositiveIntOrDefault(TableStorageContext db, string key)
        => int.TryParse(Raw(db, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : int.Parse(ByKey[key].DefaultValue, CultureInfo.InvariantCulture);

    private static string? Raw(TableStorageContext db, string key)
        => db.SystemConfigurations.FirstOrDefault(c => c.Key == key)?.Value;

    private static bool IsNonNegativeDecimal(string v) =>
        decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) && d >= 0;

    private static bool IsBool(string v) => bool.TryParse(v, out _);
}
