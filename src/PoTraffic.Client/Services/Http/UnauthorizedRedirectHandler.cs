using System.Net;
using Microsoft.AspNetCore.Components;

namespace PoTraffic.Client.Services.Http;

/// <summary>
/// Redirects to /login when the cookie session has expired mid-use (e.g. a 401
/// on a polling page). /api/auth requests are exempt so the auth-state probe on
/// the login page itself cannot loop.
/// </summary>
public sealed class UnauthorizedRedirectHandler(NavigationManager nav) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized
            && request.RequestUri?.AbsolutePath.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase) is false
            && !string.Equals(request.RequestUri?.AbsolutePath, "/api/account/panels", StringComparison.OrdinalIgnoreCase))
        {
            nav.NavigateTo("/login");
        }

        return response;
    }
}
