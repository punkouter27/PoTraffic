using Microsoft.Playwright;
using PoTraffic.E2EUI.Helpers;

namespace PoTraffic.E2EUI;

public sealed class ScreenshotRunner : PlaywrightTestBase
{
    [SkipUnlessE2EReady]
    public async Task CaptureScreenshots_WhenConfigured()
    {
        string? folder = Environment.GetEnvironmentVariable("SHOTS");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        Directory.CreateDirectory(folder);

        using HttpClient apiHttp = new() { BaseAddress = new Uri(BaseUrl) };
        TestingApiClient api = new(apiHttp);
        string email = await api.SeedAdminAsync();

        // Anonymous login page (desktop)
        await Page.GotoAsync($"{BaseUrl}/login");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(folder, "login.png"), FullPage = true });

        // Authenticated desktop routes
        await AuthenticateViaDevLoginAsync(email, role: "Administrator", destination: "/dashboard");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        (string Name, string Path)[] routes =
        [
            ("dashboard", "/dashboard"),
            ("routes-create", "/routes/create"),
            ("admin", "/admin"),
            ("health", "/health"),
        ];

        foreach ((string name, string path) in routes)
        {
            await Page.GotoAsync($"{BaseUrl}{path}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Page.WaitForTimeoutAsync(500);
            await Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(folder, $"{name}.png"), FullPage = true });
        }

        // Mobile viewport dashboard
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync($"{BaseUrl}/dashboard");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(500);
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(folder, "dashboard-phone.png"), FullPage = true });
    }
}
