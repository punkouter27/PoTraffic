using FluentAssertions;

namespace PoTraffic.Unit.Features.Auth;

/// <summary>The dev-only fake sign-in URL (GET /api/auth/guest-login?returnUrl=) only returns to a local path.</summary>
public sealed class GuestSignInUrlTests
{
    [Theory]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("/routes/create?x=1", "/routes/create?x=1")]
    [InlineData("/", "/")]
    [InlineData(null, "/dashboard")]
    [InlineData("", "/dashboard")]
    [InlineData("https://evil.example", "/dashboard")]
    [InlineData("//evil.example", "/dashboard")]
    [InlineData(@"/\evil.example", "/dashboard")]
    [InlineData("dashboard", "/dashboard")]
    public void Return_url_is_a_local_path_or_the_dashboard(string? returnUrl, string expected) =>
        GuestAuthExtensions.SafeReturnUrl(returnUrl).Should().Be(expected);
}
