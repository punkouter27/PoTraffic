using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PoTraffic.IntegrationTests.Helpers;
using PoTraffic.Shared.DTOs.Account;

namespace PoTraffic.IntegrationTests.Features.Account;

/// <summary>
/// Settings → Advanced switches live on the account: switching one on, reading it back and
/// switching it off round-trip through /api/account/panels, and unknown names are refused.
/// </summary>
public sealed class AdvancedPanelsIntegrationTests : BaseIntegrationTest
{
    [SkipUnlessAzuriteAvailable]
    public async Task Panels_RoundTripOnTheAccount()
    {
        HttpClient client = CreateClient();
        (await client.PostAsync("/api/auth/guest-login", content: null)).StatusCode
            .Should().Be(HttpStatusCode.OK, "guest login creates the user row");

        (await client.GetFromJsonAsync<string[]>("/api/account/panels"))
            .Should().BeEquivalentTo(["Map", "Weather"], "a new account starts with the map and weather on");

        (await client.PutAsJsonAsync("/api/account/panels/weather", new AdvancedPanelRequest(false))).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<string[]>("/api/account/panels"))
            .Should().Equal(["Map"]);

        (await client.PutAsJsonAsync("/api/account/panels/heatmap", new AdvancedPanelRequest(true))).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "the heatmap is not optional, so it is not a panel");

        (await client.PutAsJsonAsync("/api/account/panels/Map", new AdvancedPanelRequest(false))).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<string[]>("/api/account/panels"))
            .Should().BeEmpty();
    }
}
