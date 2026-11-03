// filepath: tests/PoTraffic.E2ETests/Api/ApiSkipUnlessReadyAttribute.cs
using System;
using System.Net.Http;
using System.Threading;
using Xunit;

namespace PoTraffic.E2ETests.Api;

/// <summary>
/// xunit <see cref="FactAttribute"/> that auto-skips when the live PoTraffic.API
/// (any environment) is not reachable. Lets CI pass on machines without a
/// running instance while still letting a local dev drive the suite.
///
/// Probes <c>E2E_BASE_URL</c> when it is set — the same host <see cref="ApiSessionFactory"/>
/// will talk to — and otherwise the standard Testing (5150) and Development (5000) ports, so the
/// scenario file does not need to know which profile is running.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ApiSkipUnlessReadyAttribute : FactAttribute
{
    private static string[] CandidatePorts =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") is { Length: > 0 } explicitUrl
            ? [explicitUrl]
            : [ApiSessionFactory.DefaultBaseUrl, ApiSessionFactory.DevelopmentBaseUrl];

    public override string? Skip => GetReachableBaseUrl() is null
        ? $"PoTraffic.API /health/json unreachable on {string.Join(", ", CandidatePorts)}"
        : null;

    /// <summary>Returns the first base URL whose /health/json responds 200, or null.</summary>
    public static string? GetReachableBaseUrl()
    {
        foreach (string candidate in CandidatePorts)
        {
            try
            {
                using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                using var resp = probe.GetAsync(new Uri(new Uri(candidate), "/health/json"))
                                      .GetAwaiter().GetResult();
                if (resp.IsSuccessStatusCode) return candidate;
            }
            catch
            {
                // try the next candidate
            }
        }
        return null;
    }
}