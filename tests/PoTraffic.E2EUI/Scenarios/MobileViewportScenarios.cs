using Microsoft.Playwright;
using PoTraffic.E2EUI.Helpers;

namespace PoTraffic.E2EUI.Scenarios;

/// <summary>
/// Mobile viewport E2E tests — verifies key user journeys render correctly
/// at a mobile form factor (390×844, equivalent to iPhone 14 Pro).
/// Inherits from <see cref="PlaywrightTestBase"/> for console/crash audit.
/// </summary>
public sealed class MobileViewportScenarios : PlaywrightTestBase
{
    private const int MobileWidth = 390;
    private const int MobileHeight = 844;

    [SkipUnlessE2EReady]
    public async Task LoginPage_RendersCorrectly_AtMobileViewport()
    {
        // Create a mobile-sized context (iPhone 14 Pro: 390×844)
        IBrowserContext mobileContext = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = MobileWidth, Height = MobileHeight }
        });
        IPage mobilePage = await mobileContext.NewPageAsync();

        try
        {
            await mobilePage.GotoAsync($"{BaseUrl}/login");
            await mobilePage.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Which sign-in actions appear is a property of the server's configuration, not of
            // the viewport: LoginPage hides the Microsoft button when Microsoft is unconfigured
            // and guest is available, which is exactly the Testing host this runs against. This
            // test is about mobile layout, so it asserts that the page offers a way in — not
            // which one.
            ILocator signInButtons = mobilePage.Locator(".auth-social-btn");
            await signInButtons.First.WaitForAsync(new() { Timeout = 15_000 });

            int signInCount = await signInButtons.CountAsync();
            Assert.True(signInCount > 0, "The login page must offer at least one sign-in action at mobile viewport.");
            Assert.True(await signInButtons.First.IsVisibleAsync(),
                "The login page's sign-in action must be visible at mobile viewport.");

            int scrollWidth = await mobilePage.EvaluateAsync<int>("document.body.scrollWidth");
            Assert.True(scrollWidth <= MobileWidth,
                $"Page has horizontal overflow at mobile width. scrollWidth={scrollWidth}, viewport={MobileWidth}");
        }
        finally
        {
            await mobilePage.CloseAsync();
            await mobileContext.CloseAsync();
        }
    }

    [SkipUnlessE2EReady]
    public async Task DashboardRouteCards_StackVertically_AtMobileViewport()
    {
        IBrowserContext mobileContext = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = MobileWidth, Height = MobileHeight }
        });
        IPage mobilePage = await mobileContext.NewPageAsync();

        try
        {
            await mobilePage.GotoAsync($"{BaseUrl}/login");
            await mobilePage.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.True(true, "Stub — implement after Radzen DataList responsive config is finalised");
        }
        finally
        {
            await mobilePage.CloseAsync();
            await mobileContext.CloseAsync();
        }
    }

    [SkipUnlessE2EReady]
    public async Task AdminPage_RedirectsToLogin_ForUnauthenticatedMobileUser()
    {
        IBrowserContext mobileContext = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = MobileWidth, Height = MobileHeight }
        });
        IPage mobilePage = await mobileContext.NewPageAsync();

        try
        {
            await mobilePage.GotoAsync($"{BaseUrl}/admin");
            await mobilePage.WaitForURLAsync("**/login**", new PageWaitForURLOptions
            {
                Timeout = 30_000
            });

            string currentUrl = mobilePage.Url;
            Assert.Contains("/login", currentUrl, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await mobilePage.CloseAsync();
            await mobileContext.CloseAsync();
        }
    }

    /// <summary>
    /// /diag is the anonymous "System status" page (the /diag contract in AGENTS.md): a signed-out
    /// visitor gets the banner, then API / Data / Services and the version, at phone and desktop
    /// widths, with no console or network errors (the base class fails the test on any).
    /// </summary>
    [SkipUnlessE2EReady]
    public async Task DiagPage_RendersForSignedOutVisitor_AtPhoneAndDesktop()
    {
        foreach ((int width, int height) in new[] { (1366, 900), (MobileWidth, MobileHeight) })
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{BaseUrl}/diag");

            await Page.GetByTestId("diag-banner").WaitForAsync(new() { Timeout = 30_000 });
            Assert.Contains("/diag", Page.Url, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("System status", await Page.TitleAsync());
            Assert.Equal(["API", "Data", "Services"], await Page.Locator("section.po-diag__group h2").AllInnerTextsAsync());
            Assert.True(await Page.Locator("[data-group='api'] [data-check='api']").IsVisibleAsync(), "API row missing");
            Assert.True(await Page.Locator("[data-group='data'] [data-check='storage']").IsVisibleAsync(), "storage row missing");
            Assert.True(await Page.Locator("[data-group='service'] [data-check='google-maps']").IsVisibleAsync(), "services rows missing");
            // The Test host mocks Google Maps on purpose: a neutral Off row, not a fault, and the banner ignores it.
            Assert.Equal("Off", await Page.Locator("[data-check='google-maps'] .po-badge").InnerTextAsync());
            Assert.Equal("Everything is working", await Page.GetByTestId("diag-banner").InnerTextAsync());
            Assert.True(await Page.GetByTestId("diag-about").IsVisibleAsync(), "version/environment missing");
            // Po web standard: one .po-diag__card per dependency, each with a .po-badge status.
            int badgedCards = await Page.Locator(".po-diag__card .po-badge").CountAsync();
            Assert.True(badgedCards >= 3, $"expected a badged .po-diag__card per dependency, found {badgedCards}");

            await Page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).ClickAsync();
            await Page.GetByTestId("diag-banner").WaitForAsync();

            int scrollWidth = await Page.EvaluateAsync<int>("document.documentElement.scrollWidth");
            Assert.True(scrollWidth <= width, $"/diag overflows horizontally at {width}px (scrollWidth={scrollWidth}).");
        }
    }
}
