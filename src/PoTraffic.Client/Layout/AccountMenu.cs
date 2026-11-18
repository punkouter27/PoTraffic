using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PoTraffic.Client.Services.Auth;
using PoTraffic.Shared.Constants;

namespace PoTraffic.Client.Layout;

/// <summary>
/// What "Account" means, once. The Po header (PoHeader), the Settings page and the phone's
/// bottom sheet (BottomNav) all read the items, the guest-label rule and sign-out from here.
/// </summary>
public static class AccountMenu
{
    public sealed record Link(string Href, string Icon, string Label);

    /// <param name="Email">The raw sign-in address — what the Po header shows.</param>
    /// <param name="Label">The friendly label (guest addresses read GUEST12345678).</param>
    public sealed record Identity(bool IsAuthenticated, bool IsAdmin, bool IsGuest, string Email, string Label, string Initials);

    public static IReadOnlyList<Link> Links(bool isAdmin)
    {
        List<Link> links =
        [
            new("/settings", "settings", "Settings"),
        ];
        if (isAdmin)
            links.Add(new("/admin", "admin_panel_settings", "Admin"));
        return links;
    }

    public static Identity Describe(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return new Identity(false, false, false, string.Empty, string.Empty, "?");

        bool isAdmin = HasRole(user, "Administrator");
        bool isGuest = HasRole(user, "Guest");

        string email = user.FindFirst("email")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.Identity?.Name
            ?? "User";

        // Guest accounts are minted as guest12345678@potraffic.dev; the raw address is noise,
        // so they render as GUEST12345678. The address shape is GuestAccountConstants'.
        return isGuest && GuestAccountConstants.IsGuestEmail(email)
            ? new Identity(true, isAdmin, true, email, GuestAccountConstants.ToDisplayLabel(email), "G")
            : new Identity(true, isAdmin, false, email, email, email[0].ToString().ToUpperInvariant());
    }

    public static async Task SignOutAsync(AuthenticationStateProvider auth, NavigationManager nav)
    {
        if (auth is CookieAuthenticationStateProvider cookie)
            await cookie.LogoutAsync();
        nav.NavigateTo("/login", forceLoad: true);
    }

    private static bool HasRole(ClaimsPrincipal user, string role) =>
        user.IsInRole(role) ||
        user.Claims.Any(c => c.Type is "role" or ClaimTypes.Role && c.Value == role);
}
