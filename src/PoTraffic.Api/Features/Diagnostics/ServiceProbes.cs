using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoTraffic.Api.Features.Config;
using PoTraffic.Api.Platform;
using PoTraffic.Shared.DTOs.Admin;

namespace PoTraffic.Api.Features.Diagnostics;

/// <summary>
/// The live service rows of <c>/api/diag</c>: every external service the app really calls is
/// reached over the network here, never just looked up in configuration. Each probe costs nothing
/// (a metadata read, a free public API, or a deliberately incomplete request that the service
/// rejects only after it has checked the key), and results are cached for <see cref="CacheFor"/>,
/// so an anonymous visitor pressing Refresh can never run up a bill.
/// <para>
/// A service switched off on purpose (mock providers, no TomTom key, a local run without Key
/// Vault) is <see cref="Off"/>: neutral, and left out of the overall status. Only a service that
/// is meant to be on and does not answer is Degraded or Unhealthy.
/// </para>
/// </summary>
public sealed class ServiceProbes(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    IServiceProvider services,
    ILogger<ServiceProbes> logger) : IDisposable
{
    /// <summary>Status of a service that is switched off on purpose. Never counts toward the banner.</summary>
    public const string Off = "Off";

    /// <summary>The named HttpClient every probe uses (short timeout, no retries).</summary>
    public const string HttpClientName = "diag-probes";

    internal static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    private const string EntraBase = "https://login.microsoftonline.com/common";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, DiagCheckDto[] Rows)? _cache;

    public void Dispose() => _gate.Dispose();

    public async Task<DiagCheckDto[]> RunAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is { } cached && DateTimeOffset.UtcNow - cached.At < CacheFor)
                return cached.Rows;

            DiagCheckDto[] rows = await Task.WhenAll(
                Run("google-maps", "Google Maps", "Measures the travel time of each commute and suggests addresses.", GoogleMapsAsync, cancellationToken),
                Run("weather", "Open-Meteo weather", "Records the weather beside each traffic check.", WeatherAsync, cancellationToken, optional: true),
                Run("holidays", "Public holidays (Nager.Date)", "Marks public holidays so they do not skew the usual times.", HolidaysAsync, cancellationToken, optional: true),
                Run("incidents", "TomTom incidents", "Explains slowdowns with the incident on the route.", IncidentsAsync, cancellationToken, optional: true),
                Run("keyvault", "Key Vault", "Holds the secrets the app loads at startup.", KeyVaultAsync, cancellationToken),
                Run("microsoft-sign-in", "Microsoft sign-in", "Lets people sign in with a Microsoft account.", MicrosoftSignInAsync, cancellationToken),
                Run("telemetry", "Telemetry", "Sends logs and traces to Application Insights.", TelemetryAsync, cancellationToken, optional: true));

            _cache = (DateTimeOffset.UtcNow, rows);
            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>A probe's verdict. Detail and Problem are fixed text: never a host, key or exception message.</summary>
    internal readonly record struct Outcome(string Status, string Detail, string? Problem = null)
    {
        public static Outcome Healthy(string detail = "Checked with a live call") => new(nameof(HealthStatus.Healthy), detail);
        public static Outcome Degraded(string problem) => new(nameof(HealthStatus.Degraded), "Answered with a problem", problem);
        public static Outcome Unhealthy(string problem) => new(nameof(HealthStatus.Unhealthy), "Not working", problem);
        public static Outcome SwitchedOff(string detail) => new(Off, detail);
    }

    private delegate Task<Outcome> Probe(HttpClient http, CancellationToken cancellationToken);

    /// <param name="optional">The app keeps working without this service (a poll is recorded
    /// without weather, an alert without an incident), so an unreachable service is Degraded.</param>
    private async Task<DiagCheckDto> Run(string name, string label, string purpose, Probe probe, CancellationToken cancellationToken, bool optional = false)
    {
        long started = Stopwatch.GetTimestamp();
        Outcome outcome;
        try
        {
            using HttpClient http = httpClientFactory.CreateClient(HttpClientName);
            outcome = await probe(http, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "/api/diag probe {Probe} failed", name);
            outcome = optional
                ? Outcome.Degraded($"Not reachable ({ex.GetType().Name}); the app carries on without it.")
                : Outcome.Unhealthy($"Not reachable ({ex.GetType().Name}).");
        }

        // An Off row made no call, so it has no latency to report.
        double latency = outcome.Status == Off ? 0 : Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1);
        return new DiagCheckDto(name, DiagGroups.Service, label, outcome.Status, purpose, outcome.Detail, latency, outcome.Problem);
    }

    /// <summary>The host forces mock providers (Test, or Features:UseMockProviders): no live traffic calls at all.</summary>
    private bool MockHost => FeatureFlags.IsMockForcedByHost(configuration, environment);

    private string? Setting(string key) => configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
        && !value.StartsWith("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) ? value.Trim() : null;

    // ── Google Maps: a geocode request for a country that does not exist. Google checks the key
    //    before anything else (a bad key gets REQUEST_DENIED), then finds nothing. The verdict is
    //    cached for GoogleCacheFor, so even a visitor hammering Refresh makes at most a few
    //    dozen such calls a day, well inside Google's free monthly allowance.
    internal static readonly TimeSpan GoogleCacheFor = TimeSpan.FromMinutes(15);
    private (DateTimeOffset At, Outcome Outcome)? _google;

    private async Task<Outcome> GoogleMapsAsync(HttpClient http, CancellationToken ct)
    {
        if (MockHost)
            return Outcome.SwitchedOff("Mocked (sample travel times)");
        if (Setting("GoogleMaps:ApiKey") is not { } key)
            return Outcome.Unhealthy("The Google Maps key is missing, so travel times cannot be measured.");
        if (_google is { } cached && DateTimeOffset.UtcNow - cached.At < GoogleCacheFor)
            return cached.Outcome;

        using HttpResponseMessage response = await http.GetAsync(
            $"https://maps.googleapis.com/maps/api/geocode/json?components=country:ZZZZ&key={Uri.EscapeDataString(key)}", ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        Outcome outcome = body.Contains("REQUEST_DENIED", StringComparison.Ordinal)
            ? Outcome.Unhealthy("Google rejected the Maps key, so travel times cannot be measured.")
            : (int)response.StatusCode < 500
                ? Outcome.Healthy("Key accepted (live call)")
                : Outcome.Degraded($"Google Maps answered {(int)response.StatusCode}.");
        _google = (DateTimeOffset.UtcNow, outcome);
        return outcome;
    }

    // ── Open-Meteo: a real (free, keyless) one-variable forecast.
    private async Task<Outcome> WeatherAsync(HttpClient http, CancellationToken ct)
    {
        if (MockHost)
            return Outcome.SwitchedOff("Mocked (sample weather)");
        using HttpResponseMessage response = await http.GetAsync(
            "https://api.open-meteo.com/v1/forecast?latitude=47.6&longitude=-122.3&current=temperature_2m", ct);
        return response.IsSuccessStatusCode
            ? Outcome.Healthy()
            : Outcome.Degraded($"Open-Meteo answered {(int)response.StatusCode}; checks are recorded without weather.");
    }

    // ── Nager.Date: the free, keyless country list.
    private async Task<Outcome> HolidaysAsync(HttpClient http, CancellationToken ct)
    {
        if (MockHost)
            return Outcome.SwitchedOff("Mocked (no holidays)");
        using HttpResponseMessage response = await http.GetAsync("https://date.nager.at/api/v3/AvailableCountries", ct);
        return response.IsSuccessStatusCode
            ? Outcome.Healthy()
            : Outcome.Degraded($"Nager.Date answered {(int)response.StatusCode}; holidays are treated as normal days.");
    }

    // ── TomTom: incident details for a tiny box. Off without a key (the provider is a no-op then).
    private async Task<Outcome> IncidentsAsync(HttpClient http, CancellationToken ct)
    {
        if (Setting("TomTom:ApiKey") is not { } key)
            return Outcome.SwitchedOff("Off (no TomTom key)");
        using HttpResponseMessage response = await http.GetAsync(
            $"https://api.tomtom.com/traffic/services/5/incidentDetails?key={Uri.EscapeDataString(key)}&bbox=-122.34,47.60,-122.33,47.61&fields=%7Bincidents%7Btype%7D%7D", ct);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => Outcome.Healthy(),
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => Outcome.Degraded("TomTom rejected the key, so alerts carry no incident."),
            _ => Outcome.Degraded($"TomTom answered {(int)response.StatusCode}, so alerts carry no incident."),
        };
    }

    // ── Key Vault: lists one secret NAME with the app's own credential (no value is read).
    private async Task<Outcome> KeyVaultAsync(HttpClient http, CancellationToken ct)
    {
        if (services.GetService<KeyVaultSource>() is not { } source)
        {
            return environment.IsProduction()
                ? Outcome.Unhealthy("Key Vault is not connected, so the app's secrets are missing.")
                : Outcome.SwitchedOff("Not used (local settings)");
        }

        await foreach (Azure.Page<SecretProperties> page in source.Client.GetPropertiesOfSecretsAsync(ct).AsPages(pageSizeHint: 1).WithCancellation(ct))
        {
            _ = page;
            break;
        }

        return Outcome.Healthy("Listed secrets (live call)");
    }

    // ── Microsoft sign-in: reads the sign-in metadata, then asks for a token with the app's
    //    client secret. Only Entra's "bad/expired secret" codes (AADSTS7000215/7000222) fail.
    private async Task<Outcome> MicrosoftSignInAsync(HttpClient http, CancellationToken ct)
    {
        string? clientId = Setting("ExternalAuth:Microsoft:ClientId");
        string? secret = Setting("ExternalAuth:Microsoft:ClientSecret");
        if (clientId is null || secret is null)
        {
            return environment.IsProduction()
                ? Outcome.Unhealthy("Microsoft sign-in is not configured, so nobody can sign in.")
                : Outcome.SwitchedOff("Guest sign-in only (local)");
        }

        using (HttpResponseMessage metadata = await http.GetAsync($"{EntraBase}/v2.0/.well-known/openid-configuration", ct))
        {
            if (!metadata.IsSuccessStatusCode)
                return Outcome.Unhealthy($"The Microsoft sign-in service answered {(int)metadata.StatusCode}.");
        }

        using HttpResponseMessage token = await http.PostAsync($"{EntraBase}/oauth2/v2.0/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = secret,
            ["grant_type"] = "client_credentials",
            ["scope"] = "https://graph.microsoft.com/.default",
        }), ct);
        if (token.IsSuccessStatusCode)
            return Outcome.Healthy("Secret accepted (live call)");

        // Entra checks the secret before anything else, so only its two "secret" codes mean the
        // secret is wrong; a later refusal (tenant, grant) still proves the secret was accepted.
        // The AADSTS number is a public error code, safe to show.
        List<int> codes = await ReadEntraErrorCodesAsync(token, ct);
        int bad = codes.FirstOrDefault(code => code is 7000215 or 7000222);
        return bad != 0
            ? Outcome.Unhealthy($"Microsoft rejected the app's sign-in secret (AADSTS{bad}), so nobody can sign in.")
            : Outcome.Healthy(codes.Count > 0 ? $"Secret accepted (live call, then AADSTS{codes[0]})" : "Secret accepted (live call)");
    }

    private static async Task<List<int>> ReadEntraErrorCodesAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.TryGetProperty("error_codes", out JsonElement codes) && codes.ValueKind == JsonValueKind.Array
                ? [.. codes.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Number).Select(c => c.GetInt32())]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ── Application Insights: an empty batch to the ingestion endpoint (nothing is recorded).
    private async Task<Outcome> TelemetryAsync(HttpClient http, CancellationToken ct)
    {
        if (PoPlatform.ResolveAppInsightsConnectionString(configuration) is not { } connectionString)
            return Outcome.SwitchedOff("Off (no connection string)");

        string ingestion = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(kv => kv.Length == 2 && kv[0].Equals("IngestionEndpoint", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv[1])
            .FirstOrDefault() ?? "https://dc.services.visualstudio.com/";

        using HttpResponseMessage response = await http.PostAsync($"{ingestion.TrimEnd('/')}/v2.1/track",
            new StringContent("[]", Encoding.UTF8, "application/json"), ct);
        return (int)response.StatusCode < 500
            ? Outcome.Healthy()
            : Outcome.Degraded($"Application Insights answered {(int)response.StatusCode}, so telemetry may be lost.");
    }
}

/// <summary>
/// Registered by <c>AddPoTrafficKeyVault</c> only when the app really added Key Vault to its
/// configuration, so <c>/diag</c> probes the same vault with the same credential.
/// </summary>
public sealed record KeyVaultSource(SecretClient Client);
