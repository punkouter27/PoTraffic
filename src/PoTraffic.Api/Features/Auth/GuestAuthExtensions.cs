using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using PoTraffic.Api.Infrastructure.Storage;
using PoTraffic.Shared.DTOs.Auth;

namespace PoTraffic.Api.Features.Auth;

/// <summary>
/// GUEST login bypass of OAuth on Development/Test hosts only (no button on /login: POST for API
/// clients and tests, GET ?returnUrl= as the fake sign-in URL for browsers and browser tests).
/// Each call creates a unique GUEST user (e.g. guest46344343@potraffic.dev) so
/// that parallel test runs or manual sessions do not collide in the database.
/// The session is issued as the standard BFF cookie.
///
/// (Rule 6 — GUEST Mode: "GUEST12345678 LOGGED IN" in the nav bar.)
/// (Rule 13 — Microsoft OAuth is the only sign-in path in Production.)
/// </summary>
public static class GuestAuthExtensions
{
    // The GUEST address shape lives in PoTraffic.Shared.Constants.GuestAccountConstants —
    // the client detects GUEST sessions from the same values and cannot reference this project.

    public static IEndpointRouteBuilder MapGuestEndpoints(this IEndpointRouteBuilder app, IWebHostEnvironment environment)
    {
        // Rule 4.4 — the fake/guest auth path must never exist in Production.
        if (environment.IsProduction())
            throw new InvalidOperationException(
                "GUEST login must not be registered in Production — Microsoft OAuth is the only sign-in path.");

        // GUEST login bypass: Development (manual bypass button) + Testing (automated tests).
        app.MapPost("/api/auth/guest-login", async (
            HttpContext httpContext,
            GuestLoginCommandHandler handler,
            IWebHostEnvironment env) =>
        {
            if (!env.IsEnvironment("Test") && !env.IsDevelopment())
            {
                return Results.NotFound();
            }

            GuestLoginResult result = await handler.Handle(new GuestLoginCommand(), CancellationToken.None);
            if (!result.IsSuccess || result.User is null)
                return Results.BadRequest(new { error = result.ErrorCode });

            User user = result.User;
            await CookieSignIn.SignInAsync(httpContext, user);
            return Results.Ok(new AuthMeResponse(user.Id, user.Email, user.Role, user.AuthProvider));
        })
        .WithTags("Auth")
        .WithName("GuestLogin")
        .ExcludeFromDescription() // Hide from OpenAPI
        .AllowAnonymous();

        // The dev-only fake sign-in URL: /login shows ONE "Sign in with Microsoft" button in every
        // environment, so a person or a browser test on a Development/Test host opens
        // /api/auth/guest-login?returnUrl=/dashboard instead of clicking a guest button. Same
        // session as the POST above, then a redirect to a local path only (no open redirect).
        app.MapGet("/api/auth/guest-login", async (
            HttpContext httpContext,
            GuestLoginCommandHandler handler,
            IWebHostEnvironment env,
            string? returnUrl) =>
        {
            if (!env.IsEnvironment("Test") && !env.IsDevelopment())
            {
                return Results.NotFound();
            }

            GuestLoginResult result = await handler.Handle(new GuestLoginCommand(), CancellationToken.None);
            if (!result.IsSuccess || result.User is null)
                return Results.BadRequest(new { error = result.ErrorCode });

            await CookieSignIn.SignInAsync(httpContext, result.User);
            return Results.LocalRedirect(SafeReturnUrl(returnUrl));
        })
        .WithTags("Auth")
        .WithName("GuestLoginRedirect")
        .ExcludeFromDescription()
        .AllowAnonymous();

        return app;
    }

    /// <summary>A local path such as <c>/dashboard</c>; anything else (absolute, <c>//host</c>, <c>/\host</c>) becomes <c>/dashboard</c>.</summary>
    internal static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } url && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
            ? url
            : "/dashboard";
}

public sealed record GuestLoginCommand;

public sealed record GuestLoginResult(
    bool IsSuccess,
    User? User,
    string? ErrorCode);

public sealed class GuestLoginCommandHandler(
    TableStorageContext db,
    ILogger<GuestLoginCommandHandler> logger)
{
    public async Task<GuestLoginResult> Handle(GuestLoginCommand command, CancellationToken ct)
    {
        // Each GUEST session gets a brand-new unique account so parallel runs never collide.
        // Suffix is 8 random digits (00000000–99999999), giving 100M distinct GUEST identities.
        int min = (int)Math.Pow(10, GuestAccountConstants.SuffixLength - 1);
        int max = (int)Math.Pow(10, GuestAccountConstants.SuffixLength);
        string guestEmail = GuestAccountConstants.EmailFor(Random.Shared.Next(min, max));

        // Ensure no accidental collision — retry once if needed
        if (db.Users.Any(u => u.Email == guestEmail))
            guestEmail = GuestAccountConstants.EmailFor(Random.Shared.Next(min, max));

        User guestUser = new()
        {
            Id = UserId.New(),
            Email = guestEmail,
            Locale = "en-US",
            Role = "Guest", // Special role for GUEST test users
            AuthProvider = "guest",
            IsEmailVerified = true,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };

        db.Add(guestUser);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("GUEST session created: {Email}", guestEmail);

        return new GuestLoginResult(true, guestUser, null);
    }
}
