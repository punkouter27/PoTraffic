using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using PoTraffic.Shared.DTOs.History;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.Enums;
using Xunit;

namespace PoTraffic.E2ETests.Api.Scenarios;

/// <summary>
/// Route CRUD and per-route history against the live Testing host — the endpoints the dashboard
/// and the route page are built on. Each scenario signs in a fresh GUEST and creates its own data.
/// </summary>
public sealed class RoutesApiScenarios
{
    [ApiSkipUnlessReady]
    public async Task Route_CreateReadListDelete_RoundTrips()
    {
        (HttpClient client, _) = await ApiSessionFactory.CreateGuestSessionAsync();
        using (client)
        {
            RouteDto created = await ApiTestData.CreateRouteAsync(client);
            Assert.Equal(MonitoringStatus.Active, created.MonitoringStatus);
            Assert.NotEmpty(created.Windows);

            RouteDto? fetched = await client.GetFromJsonAsync($"/api/routes/{created.Id}", ApiJsonContext.Default.RouteDto);
            Assert.Equal(created.Id, fetched!.Id);
            Assert.Equal(created.OriginAddress, fetched.OriginAddress);

            PagedResult<RouteDto>? page = await client.GetFromJsonAsync("/api/routes?page=1&pageSize=50", ApiJsonContext.Default.PagedResultRouteDto);
            Assert.Contains(page!.Items, r => r.Id == created.Id);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/routes/{created.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/routes/{created.Id}")).StatusCode);
            page = await client.GetFromJsonAsync("/api/routes?page=1&pageSize=50", ApiJsonContext.Default.PagedResultRouteDto);
            Assert.DoesNotContain(page!.Items, r => r.Id == created.Id);
        }
    }

    [ApiSkipUnlessReady]
    public async Task Route_WithInvalidInput_IsRejectedWith422()
    {
        (HttpClient client, _) = await ApiSessionFactory.CreateGuestSessionAsync();
        using (client)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/routes",
                new CreateRouteRequest(OriginAddress: "", DestinationAddress: "Somewhere", Provider: RouteProvider.GoogleMaps));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

            HttpResponseMessage noDays = await client.PostAsJsonAsync("/api/routes",
                new CreateRouteRequest("Here", "There", RouteProvider.GoogleMaps, DaysOfWeekMask: 0));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, noDays.StatusCode);
        }
    }

    [ApiSkipUnlessReady]
    public async Task Route_BelongingToSomeoneElse_LooksLikeItDoesNotExist()
    {
        (HttpClient owner, _) = await ApiSessionFactory.CreateGuestSessionAsync();
        (HttpClient stranger, _) = await ApiSessionFactory.CreateGuestSessionAsync();
        using (owner)
        using (stranger)
        {
            RouteDto route = await ApiTestData.CreateRouteAsync(owner);

            Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/routes/{route.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/routes/{route.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await stranger.PutAsJsonAsync($"/api/routes/{route.Id}/name", new { name = "mine now" })).StatusCode);

            PagedResult<RouteDto>? strangersRoutes = await stranger.GetFromJsonAsync("/api/routes", ApiJsonContext.Default.PagedResultRouteDto);
            Assert.DoesNotContain(strangersRoutes!.Items, r => r.Id == route.Id);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/routes/{route.Id}")).StatusCode);
        }
    }

    [ApiSkipUnlessReady]
    public async Task Route_Settings_NamePauseAndArriveBy_AreSavedOnTheRoute()
    {
        (HttpClient client, _) = await ApiSessionFactory.CreateGuestSessionAsync();
        using (client)
        {
            RouteDto route = await ApiTestData.CreateRouteAsync(client);

            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PutAsJsonAsync($"/api/routes/{route.Id}/name", new { name = "School run" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PutAsJsonAsync($"/api/routes/{route.Id}/arrive-by", new { arriveBy = "08:45" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PutAsJsonAsync($"/api/routes/{route.Id}/paused", new { paused = true })).StatusCode);

            RouteDto? saved = await client.GetFromJsonAsync($"/api/routes/{route.Id}", ApiJsonContext.Default.RouteDto);
            Assert.Equal("School run", saved!.Name);
            Assert.Equal("08:45", saved.ArriveBy);
            Assert.Equal(MonitoringStatus.Paused, saved.MonitoringStatus);

            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PutAsJsonAsync($"/api/routes/{route.Id}/paused", new { paused = false })).StatusCode);
            saved = await client.GetFromJsonAsync($"/api/routes/{route.Id}", ApiJsonContext.Default.RouteDto);
            Assert.Equal(MonitoringStatus.Active, saved!.MonitoringStatus);
        }
    }

    [ApiSkipUnlessReady]
    public async Task History_ForANewRoute_AnswersEveryQueryWithAnEmptyResult()
    {
        (HttpClient client, _) = await ApiSessionFactory.CreateGuestSessionAsync();
        using (client)
        {
            RouteDto route = await ApiTestData.CreateRouteAsync(client);
            string baseUrl = $"/api/routes/{route.Id}";

            PagedResult<PollRecordDto>? polls = await client.GetFromJsonAsync(
                $"{baseUrl}/poll-history?page=1&pageSize=20", ApiJsonContext.Default.PagedResultPollRecordDto);
            Assert.Equal(0, polls!.TotalCount);

            Assert.Empty((await client.GetFromJsonAsync($"{baseUrl}/sessions", ApiJsonContext.Default.ListSessionDto))!);

            VolatilityHeatmapDto? heatmap = await client.GetFromJsonAsync($"{baseUrl}/heatmap", ApiJsonContext.Default.VolatilityHeatmapDto);
            Assert.Equal(0, heatmap!.TotalSamples);

            WeatherImpactResponse? weather = await client.GetFromJsonAsync($"{baseUrl}/weather-impact", ApiJsonContext.Default.WeatherImpactResponse);
            Assert.Equal(0, weather!.SampleCount);

            // Not enough history to recommend anything yet: 204 rather than a made-up answer.
            Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"{baseUrl}/departure?dayOfWeek=Monday")).StatusCode);
        }
    }

    [ApiSkipUnlessReady]
    public async Task SampleRoute_ComesWithGeneratedHistory()
    {
        (HttpClient client, _) = await ApiSessionFactory.CreateGuestSessionAsync();
        using (client)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/routes/sample", new { utcOffsetMinutes = 0 });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            RouteDto? sample = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.RouteDto);
            Assert.True(sample!.IsSample);

            PagedResult<PollRecordDto>? polls = await client.GetFromJsonAsync(
                $"/api/routes/{sample.Id}/poll-history?page=1&pageSize=5", ApiJsonContext.Default.PagedResultPollRecordDto);
            Assert.True(polls!.TotalCount > 0, "a sample route exists to show history before any real poll");
            Assert.True(polls.Items.Count is > 0 and <= 5, "poll-history honours pageSize");

            VolatilityHeatmapDto? heatmap = await client.GetFromJsonAsync(
                $"/api/routes/{sample.Id}/heatmap", ApiJsonContext.Default.VolatilityHeatmapDto);
            Assert.NotEmpty(heatmap!.Cells);
        }
    }
}
