using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PoTraffic.Api.Infrastructure.Storage;
using PoTraffic.Shared.DTOs.Routes;

namespace PoTraffic.Api.Features.Places;

/// <summary>
/// BFF proxy to the Google Places Autocomplete API (#9). The Google key stays server-side
/// (reuses <c>GoogleMaps:ApiKey</c>); the client only ever sees address suggestions. Failures
/// and a missing key degrade gracefully to an empty list so free-text entry still works.
/// With mock providers on (<see cref="FeatureFlags.UseMockProviders"/>) it answers with canned
/// suggestions instead, so the dropdown can be exercised locally without a key or a bill.
/// </summary>
public sealed class GooglePlacesService(
    HttpClient http,
    IConfiguration config,
    FeatureFlags flags,
    TableStorageContext db,
    ILogger<GooglePlacesService> logger)
{
    private const string AutocompleteUrl = "https://maps.googleapis.com/maps/api/place/autocomplete/json";
    private const int MinInputLength = 3;

    public async Task<IReadOnlyList<PlaceSuggestionDto>> AutocompleteAsync(string? input, CancellationToken ct)
    {
        if (input is null || input.Trim().Length < MinInputLength)
            return [];

        if (flags.UseMockProviders)
            return MockSuggestions(input.Trim());

        string? key = config["GoogleMaps:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            return [];

        string url = $"{AutocompleteUrl}?input={Uri.EscapeDataString(input.Trim())}&types=address&key={key}";
        // Counted toward the monthly spend (persisted with the next save, not per keystroke).
        SpendingCap.Record(db, ProviderCallKind.Autocomplete, DateTimeOffset.UtcNow);
        try
        {
            PlacesResponse? resp = await http.GetFromJsonAsync<PlacesResponse>(url, ct);
            if (resp?.Status is not ("OK" or "ZERO_RESULTS"))
            {
                logger.LogWarning("Places autocomplete returned status '{Status}'.", resp?.Status);
                return [];
            }
            return resp.Predictions
                .Select(p => new PlaceSuggestionDto(p.Description, p.PlaceId))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Places autocomplete request failed.");
            return [];
        }
    }

    /// <summary>The typed text completed into a few plausible cities; free, and never billed.</summary>
    private static IReadOnlyList<PlaceSuggestionDto> MockSuggestions(string input) =>
    [
        new($"{input}, Seattle, WA, USA", "mock-seattle"),
        new($"{input}, Portland, OR, USA", "mock-portland"),
        new($"{input}, San Francisco, CA, USA", "mock-san-francisco"),
    ];

    private sealed record PlacesResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("predictions")] Prediction[] Predictions);

    private sealed record Prediction(
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("place_id")] string PlaceId);
}
