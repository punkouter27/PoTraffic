using System.Net.Http.Json;
using System.Text.Json;

namespace PoTraffic.Client.Services.Http;

/// <summary>
/// Extracts structured error codes and user-facing RFC 7807 / RFC 9457 ProblemDetails
/// messages from failed API responses without reflection-based deserialization.
/// </summary>
public static class ProblemDetailsParser
{
    public sealed record ParsedError(string Message, string? ErrorCode = null);

    public static async Task<ParsedError> ParseAsync(HttpResponseMessage response, string fallbackMessage)
    {
        try
        {
            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not ("application/json" or "application/problem+json"))
                return new ParsedError(fallbackMessage);

            JsonElement root = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.JsonElement);
            string? code = root.TryGetProperty("error", out JsonElement errProp) ? errProp.GetString() : null;

            if (root.TryGetProperty("errors", out JsonElement errorsProp) && errorsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in errorsProp.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0)
                    {
                        string? first = prop.Value[0].GetString();
                        if (!string.IsNullOrWhiteSpace(first))
                            return new ParsedError(first, code);
                    }
                }
            }

            if (root.TryGetProperty("detail", out JsonElement detailProp) && !string.IsNullOrWhiteSpace(detailProp.GetString()))
                return new ParsedError(detailProp.GetString()!, code);

            if (root.TryGetProperty("title", out JsonElement titleProp) && !string.IsNullOrWhiteSpace(titleProp.GetString()))
                return new ParsedError(titleProp.GetString()!, code);

            return new ParsedError(fallbackMessage, code);
        }
        catch
        {
            return new ParsedError(fallbackMessage);
        }
    }
}
