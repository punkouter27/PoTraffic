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

    protected override void ConfigureHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleMaps:ApiKey"] = FakeMapsKey,
            ["ExternalAuth:Microsoft:ClientSecret"] = FakeClientSecret,
        }));

    [SkipUnlessAzuriteAvailable]
    public async Task Anonymous_report_lists_every_check_without_secrets_or_internals()
    {
        HttpResponseMessage response = await CreateClient().GetAsync("/api/diag");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("environment").GetString().Should().NotBeNullOrEmpty();
        JsonElement checks = doc.RootElement.GetProperty("checks");
        checks.EnumerateArray().Select(c => c.GetProperty("key").GetString())
            .Should().Contain(["storage", "keyvault", "trafficProvider", "scheduler"]);
        checks.EnumerateArray().Should().OnlyContain(c => c.GetProperty("detail").ValueKind == JsonValueKind.Null,
            "raw check descriptions are for administrators only");

        string[] forbidden =
        [
            FakeMapsKey, FakeClientSecret,
            "AccountKey", "devstoreaccount1", "DefaultEndpointsProtocol", "SharedAccessSignature",
            "://", "vault.azure.net", "core.windows.net", "/subscriptions/", "tenant",
            "@", ":\\", "Exception", "   at ", "ClientSecret", "ApiKey",
        ];
        foreach (string s in forbidden)
            body.Should().NotContainEquivalentOf(s, $"the anonymous diag report must not expose '{s}'");
    }

    [SkipUnlessAzuriteAvailable]
    public async Task Admin_gets_check_detail_but_keyvault_probe_stays_admin_only()
    {
        HttpClient anonymous = CreateClient();
        (await anonymous.GetAsync("/api/diag/keyvault")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        HttpClient admin = CreateClient();
        (await admin.PostAsJsonAsync("/e2e/dev-login", new { Email = "diag-admin@test.invalid", Role = "Administrator" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await admin.GetStringAsync("/api/diag"));
        doc.RootElement.GetProperty("checks").EnumerateArray()
            .Should().Contain(c => c.GetProperty("detail").ValueKind == JsonValueKind.String);
        (await admin.GetAsync("/api/diag/keyvault")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
