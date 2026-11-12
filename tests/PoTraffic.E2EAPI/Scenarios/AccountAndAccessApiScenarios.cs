using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using PoTraffic.Shared.DTOs.Account;
using PoTraffic.Shared.DTOs.Admin;
using PoTraffic.Shared.DTOs.Alerts;
using Xunit;

namespace PoTraffic.E2EAPI.Scenarios;

/// <summary>
/// The account, alerts and admin surfaces, plus the deny-by-default rule that guards them all,
/// against the live Testing host.
/// </summary>
public sealed class AccountAndAccessApiScenarios
{
    [ApiSkipUnlessReady]
    public async Task Account_ProfileQuotaAndTimeZone_ReflectTheSignedInUser()
    {
        (HttpClient client, string email) = await ApiSessionFactory.CreateGuestSessionAsync();
        using (client)
        {
            ProfileDto? profile = await client.GetFromJsonAsync("/api/account/profile", ApiJsonContext.Default.ProfileDto);
            Assert.Equal(email, profile!.Email);

            QuotaDto? quota = await client.GetFromJsonAsync("/api/account/quota", ApiJsonContext.Default.QuotaDto);
            Assert.True(quota!.DailyLimit > 0);
            Assert.Equal(quota.DailyLimit - quota.UsedToday, quota.Remaining);

            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PutAsJsonAsync("/api/account/timezone", new { timeZoneId = "America/New_York" })).StatusCode);
        }
    }

    [ApiSkipUnlessReady]
    public async Task Account_ExportThenDelete_RemovesEverythingStored()
    {
        (HttpClient client, string email) = await ApiSessionFactory.CreateGuestSessionAsync();
        using (client)
        {
            await ApiTestData.CreateRouteAsync(client);

            HttpResponseMessage export = await client.GetAsync("/api/account/export");
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            Assert.Equal("application/json", export.Content.Headers.ContentType?.MediaType);
            using (JsonDocument doc = JsonDocument.Parse(await export.Content.ReadAsStringAsync()))
            {
                JsonElement root = doc.RootElement;
                Assert.Equal(email, root.GetProperty("user").GetProperty("email").GetString());
                Assert.Equal(1, root.GetProperty("routes").GetArrayLength());
            }

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/account/")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/account/profile")).StatusCode);
        }
    }

    [ApiSkipUnlessReady]
    public async Task Alerts_ForANewUser_AreEmpty_AndMarkingReadIsScopedToTheCaller()
    {
        (HttpClient client, _) = await ApiSessionFactory.CreateGuestSessionAsync();
        using (client)
        {
            List<AlertDto>? alerts = await client.GetFromJsonAsync("/api/alerts", ApiJsonContext.Default.ListAlertDto);
            Assert.Empty(alerts!);

            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/alerts/read-all", content: null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.PostAsync($"/api/alerts/{Guid.NewGuid()}/read", content: null)).StatusCode);
        }
    }

    [ApiSkipUnlessReady]
    public async Task DenyByDefault_AnonymousGets401_CommuterGets403OnAdmin_PublicSurfacesStayOpen()
    {
        using HttpClient anonymous = ApiSessionFactory.CreateAnonymous();
        foreach (string url in new[] { "/api/routes", "/api/account/profile", "/api/alerts", "/api/admin/users" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        foreach (string url in new[] { "/api/system/features", "/health/live", "/health/ready", "/api/auth/providers" })
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(url)).StatusCode);

        (HttpClient commuter, _) = await ApiSessionFactory.CreateGuestSessionAsync();
        using (commuter)
        {
            foreach (string url in new[] { "/api/admin/users", "/api/admin/configuration", "/api/admin/spending", "/diag/keyvault" })
                Assert.Equal(HttpStatusCode.Forbidden, (await commuter.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await commuter.PutAsJsonAsync("/api/admin/configuration/feature.weather.enabled", new UpdateSystemConfigRequest("false"))).StatusCode);
        }
    }

    /// <summary>
    /// Reads the live configuration and writes each flag back unchanged, so the scenario proves
    /// the admin write path without changing what any other test (or a developer) sees.
    /// </summary>
    [ApiSkipUnlessReady]
    public async Task AdminConfiguration_ListsTheRuntimeSettings_AndValidatesEveryWrite()
    {
        using HttpClient admin = await ApiSessionFactory.CreateDevLoginSessionAsync(
            $"api-admin-{Guid.NewGuid():N}@test.invalid", "Administrator");

        List<SystemConfigDto>? configs = await admin.GetFromJsonAsync("/api/admin/configuration", ApiJsonContext.Default.ListSystemConfigDto);
        string[] runtimeKeys = ["feature.mockproviders.enabled", "feature.weather.enabled", "cost.perpoll.googlemaps", "quota.daily.default"];
        foreach (string key in runtimeKeys)
            Assert.True(configs!.Single(c => c.Key == key).IsEditable, $"{key} should be editable at runtime");

        SystemConfigDto weather = configs!.Single(c => c.Key == "feature.weather.enabled");
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync($"/api/admin/configuration/{weather.Key}", new UpdateSystemConfigRequest(weather.Value))).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PutAsJsonAsync("/api/admin/configuration/feature.weather.enabled", new UpdateSystemConfigRequest("sometimes"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PutAsJsonAsync("/api/admin/configuration/not.a.setting", new UpdateSystemConfigRequest("1"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/spending")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/users")).StatusCode);
    }
}
