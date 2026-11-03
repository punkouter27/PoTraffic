using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.Enums;
using Xunit;

namespace PoTraffic.E2ETests.Api;

/// <summary>Routes the API scenarios create for themselves, so no scenario depends on another's data.</summary>
internal static class ApiTestData
{
    /// <summary>
    /// Creates a route with addresses unique to this call: the Testing host geocodes through the
    /// deterministic mock provider, so distinct text means distinct coordinates.
    /// </summary>
    public static async Task<RouteDto> CreateRouteAsync(HttpClient client)
    {
        string suffix = Guid.NewGuid().ToString("N");
        var request = new CreateRouteRequest(
            OriginAddress: $"100 Origin Street #{suffix}",
            DestinationAddress: $"200 Destination Avenue #{suffix}",
            Provider: RouteProvider.GoogleMaps,
            StartTime: "07:00",
            EndTime: "09:00",
            DaysOfWeekMask: 0x1F);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/routes", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"POST /api/routes: {(int)response.StatusCode}\n{await response.Content.ReadAsStringAsync()}");

        RouteDto? route = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.RouteDto);
        Assert.NotNull(route);
        return route!;
    }
}
