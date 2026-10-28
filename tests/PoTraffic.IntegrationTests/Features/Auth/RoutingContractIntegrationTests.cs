using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PoTraffic.IntegrationTests.Features.Auth;

public sealed class RoutingContractIntegrationTests : BaseIntegrationTest
{
    // Every route a signed-out caller may reach. A new public route fails this test until it is
    // added here on purpose. Static files and the Blazor host are public by design and are
    // identified by their metadata, not listed. This is the Testing host, so the guest login and
    // the /e2e helpers are on it; Production maps neither.
    private static readonly string[] AnonymousAllowList =
    [
        "/api/auth/external/{provider}/callback",
        "/api/auth/external/{provider}/start",
        "/api/auth/guest-login",
        "/api/auth/logout",
        "/api/auth/me",
        "/api/auth/providers",
        "/api/diag/client-error",
        "/api/system/features",
        "/api/{**rest}",
        "/e2e/dev-login",
        "/e2e/execute-poll",
        "/e2e/seed-admin",
        "/e2e/seed-route",
        "/health/connections",
        "/health/json",
        "/health/live",
        "/health/ready",
        "/{*path:nonfile}",
    ];

    [SkipUnlessAzuriteAvailable]
    public void Anonymous_surface_is_exactly_the_allow_list()
    {
        IEnumerable<string> anonymous = GetServices().GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Where(e => !IsHostOrStaticAsset(e))
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Distinct()
            .Order(StringComparer.Ordinal);

        anonymous.Should().Equal(AnonymousAllowList);
    }

    [SkipUnlessAzuriteAvailable]
    public void Every_other_endpoint_requires_a_signed_in_user()
    {
        AuthorizationPolicy? policy = GetServices().GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;

        policy.Should().NotBeNull();
        policy!.Requirements.Should().Contain(r => r is DenyAnonymousAuthorizationRequirement);
    }

    [SkipUnlessAzuriteAvailable]
    public async Task Liveness_carries_the_commit_the_deploy_checks_for()
    {
        HttpResponseMessage response = await CreateClient().GetAsync("/health/live");

        response.Headers.Contains("X-Commit").Should().BeTrue();
    }

    private static bool IsHostOrStaticAsset(RouteEndpoint endpoint) =>
        endpoint.RoutePattern.RawText!.TrimStart('/').StartsWith("_framework", StringComparison.Ordinal)
        || endpoint.Metadata.Any(m => m.GetType().Name is "StaticAssetDescriptor" or "ComponentTypeMetadata" or "RootComponentMetadata");
}
