using PoTraffic.API.Infrastructure.Storage;

namespace PoTraffic.API.Features.Config;

/// <summary>
/// The feature switches, read from <see cref="SystemConfiguration"/> on every access so an
/// administrator can flip them at runtime (<c>PUT /api/admin/configuration/{key}</c>) without a
/// redeploy. Registered once as a singleton over the singleton working set; reads are in-memory.
///
/// <para>
/// More than one component acts on the same answer — provider resolution swaps in the mocks,
/// the Places proxy answers with canned suggestions, and <c>/api/system/features</c> tells the
/// client whether to show the "USING MOCK DATA" banner — so they all read it here and cannot
/// disagree.
/// </para>
/// </summary>
public sealed class FeatureFlags(TableStorageContext db, bool mockProvidersForcedByHost)
{
    /// <summary>
    /// True when the host itself pins mock providers on, whatever the stored setting says: the
    /// Testing environment (no live keys, no bill) or <c>Features:UseMockProviders=true</c>.
    /// </summary>
    public bool MockProvidersForcedByHost { get; } = mockProvidersForcedByHost;

    /// <summary>Mock traffic, weather and holiday providers instead of the billed live ones.</summary>
    public bool UseMockProviders =>
        MockProvidersForcedByHost || SystemSettings.BoolOrDefault(db, SystemSettings.UseMockProvidersKey);

    /// <summary>
    /// Record weather with each poll. Defaults on: the conditions feed needs no key and no secret,
    /// and a route with weather recorded from day one can answer "is rain the reason" months
    /// later — switching it on late leaves a permanent blind spot in the history.
    /// </summary>
    public bool EnableWeather => SystemSettings.BoolOrDefault(db, SystemSettings.EnableWeatherKey);

    /// <summary>
    /// The host-level override, decided from configuration and environment at composition root.
    /// Health checks use it directly: a runtime switch to mocks does not take a production host's
    /// vault or Google key away, so their checks keep reporting the live dependencies.
    /// </summary>
    public static bool IsMockForcedByHost(IConfiguration configuration, IHostEnvironment environment)
        => environment.IsEnvironment("Testing") || configuration.GetValue<bool>("Features:UseMockProviders");
}
