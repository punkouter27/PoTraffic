using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace PoTraffic.Integration.Features.Diagnostics;

/// <summary>
/// GET /api/diag feeds the anonymous /diag page, so it must never carry secrets or internals.
/// The host is seeded with recognisable fake secrets; none may appear in the anonymous body.
/// </summary>
public sealed class DiagReportIntegrationTests : BaseIntegrationTest
{
    private const string FakeMapsKey = "AIzaSENTINEL-maps-key-0000";
    private const string FakeClientSecret = "sentinel-oauth-secret-0000";
    private const string FakeAccountName = "sentinelacct42";

    protected override void ConfigureHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleMaps:ApiKey"] = FakeMapsKey,
            ["ExternalAuth:Microsoft:ClientSecret"] = FakeClientSecret,
            ["AzureTable:AccountName"] = FakeAccountName,
        }));

    private static readonly string[] Forbidden =
    [
        FakeMapsKey, FakeClientSecret, FakeAccountName,
        "AccountKey", "devstoreaccount1", "DefaultEndpointsProtocol", "SharedAccessSignature",
        "://", "vault.azure.net", "core.windows.net", "/subscriptions/", "tenant",
        "@", ":\\", "Exception", "   at ", "ClientSecret", "ApiKey",
    ];

    [SkipUnlessAzuriteAvailable]
    public async Task Anonymous_report_lists_every_check_without_secrets_or_internals()
    {
        HttpResponseMessage response = await CreateClient().GetAsync("/api/diag");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("environment").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("version").GetString().Should().MatchRegex("^[0-9]{6}$");
        JsonElement checks = doc.RootElement.GetProperty("checks");
        checks.EnumerateArray().Select(c => c.GetProperty("name").GetString())
            .Should().Contain(["api", "storage", "scheduler", "google-maps", "weather", "holidays", "incidents", "keyvault", "microsoft-sign-in", "telemetry"]);
        checks.EnumerateArray().Select(c => c.GetProperty("group").GetString())
            .Should().OnlyContain(g => g == "api" || g == "data" || g == "service");

        AssertNoSecrets(body, "/api/diag");
    }

    /// <summary>
    /// The Test host mocks every live provider and has no vault, sign-in app, TomTom key or
    /// telemetry: those rows are "Off" (switched off on purpose, no call made) and the overall
    /// status ignores them, so the report is Healthy rather than Degraded.
    /// </summary>
    [SkipUnlessAzuriteAvailable]
    public async Task Services_switched_off_on_purpose_are_Off_and_left_out_of_the_overall_status()
    {
        using JsonDocument doc = JsonDocument.Parse(await CreateClient().GetStringAsync("/api/diag"));
        JsonElement[] checks = [.. doc.RootElement.GetProperty("checks").EnumerateArray()];
        string StatusOf(string name) => checks.Single(c => c.GetProperty("name").GetString() == name).GetProperty("status").GetString()!;

        foreach (string name in new[] { "google-maps", "weather", "holidays", "incidents", "keyvault", "microsoft-sign-in", "telemetry", "scheduler" })
        {
            StatusOf(name).Should().Be("Off", $"{name} is switched off on purpose in the Test host");
            checks.Single(c => c.GetProperty("name").GetString() == name).GetProperty("latencyMs").GetDouble().Should().Be(0);
        }

        StatusOf("storage").Should().Be("Healthy");
        doc.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [SkipUnlessAzuriteAvailable]
    public async Task Anonymous_health_json_carries_no_secrets_or_internals()
    {
        HttpResponseMessage response = await CreateClient().GetAsync("/health/json");

        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"storage\"");
        AssertNoSecrets(body, "/health/json");
    }

    private static void AssertNoSecrets(string body, string route)
    {
        foreach (string s in Forbidden)
            body.Should().NotContainEquivalentOf(s, $"anonymous {route} must not expose '{s}'");
    }

    [SkipUnlessAzuriteAvailable]
    public async Task Admin_gets_the_same_report_and_the_keyvault_probe_stays_admin_only()
    {
        HttpClient anonymous = CreateClient();
        (await anonymous.GetAsync("/api/diag/keyvault")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        HttpClient admin = CreateClient();
        (await admin.PostAsJsonAsync("/e2e/dev-login", new { Email = "diag-admin@test.invalid", Role = "Administrator" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await admin.GetStringAsync("/api/diag"));
        doc.RootElement.GetProperty("checks").EnumerateArray()
            .Should().OnlyContain(c => c.GetProperty("detail").ValueKind == JsonValueKind.String);
        (await admin.GetAsync("/api/diag/keyvault")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
