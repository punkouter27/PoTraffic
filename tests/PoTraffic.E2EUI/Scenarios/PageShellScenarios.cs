using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PoTraffic.E2EUI.Helpers;

namespace PoTraffic.E2EUI.Scenarios;

/// <summary>
/// The Po page shell (AGENTS.md, Page shell) at desktop and phone widths: title top-left, the
/// signed-in email top-right (through a real test sign-in: the guest button), the vYYMMDD stamp
/// bottom-right on desktop and after the page content on phones, nothing in the header or the
/// stamp overlapping content, the sign-in page reduced to the title and its sign-in button, and
/// no bottom nav for a signed-out visitor.
/// </summary>
public sealed class PageShellScenarios : PlaywrightTestBase
{
    private static readonly (int Width, int Height)[] Sizes = [(1366, 800), (390, 844)];
    private static readonly string[] SignInButtons = ["Sign in with Microsoft", "Continue as Guest"];
    private static readonly string[] SignedInRoutes = ["/dashboard", "/diag", "/settings"];

    /// <summary>Text-bearing leaf elements in main that the header or the stamp covers, at every scroll position.</summary>
    private const string OverlapScript = """
        () => {
            const chrome = [document.querySelector('header.po-header'), document.querySelector('.po-version'), document.querySelector('.pt-bottom-nav')].filter(Boolean);
            const hits = [];
            const leaves = [...document.querySelectorAll('main *')].filter(e => !chrome.some(c => c.contains(e) || e.contains(c))
                && e.children.length === 0 && (e.innerText || '').trim() && e.getClientRects().length);
            const check = () => {
                for (const c of chrome) {
                    if (scrollY > 0 && c.tagName === 'HEADER') continue; // the sticky header scrolls content under it by design
                    if (getComputedStyle(c).position === 'static') continue; // in the flow: cannot cover anything
                    // The bottom nav slides over content while scrolling; the last row must clear it at the end.
                    if (c.classList.contains('pt-bottom-nav') && Math.ceil(scrollY + innerHeight) < document.documentElement.scrollHeight - 1) continue;
                    const a = c.getBoundingClientRect();
                    for (const e of leaves) {
                        const b = e.getBoundingClientRect();
                        if (b.width && b.height && a.left < b.right - 1 && b.left < a.right - 1 && a.top < b.bottom - 1 && b.top < a.bottom - 1)
                            hits.push(c.className + ' x ' + e.tagName + " '" + e.innerText.trim().slice(0, 30) + "'");
                    }
                }
            };
            const h = document.documentElement.scrollHeight;
            for (let y = 0; y <= h; y += 200) { window.scrollTo(0, y); check(); }
            window.scrollTo(0, h); check(); window.scrollTo(0, 0);
            return [...new Set(hits)];
        }
        """;

    [SkipUnlessE2EReady]
    public async Task SignedIn_Header_ShowsTitleLeft_EmailRight_AndStampNeverCoversContent()
    {
        await Page.GotoAsync($"{BaseUrl}/login");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue as Guest" }).ClickAsync();
        // The guest session is in place once the header shows its email (a client-side navigation, no load event).
        await Page.Locator(".po-header__email").WaitForAsync(new() { Timeout = 30_000 });

        foreach ((int width, int height) in Sizes)
        {
            await Page.SetViewportSizeAsync(width, height);
            foreach (string route in SignedInRoutes)
            {
                await Page.GotoAsync($"{BaseUrl}{route}");
                ILocator email = Page.Locator(".po-header .po-header__actions .po-header__email");
                await email.WaitForAsync(new() { Timeout = 30_000 });
                await Page.WaitForTimeoutAsync(500);

                Assert.Contains("@", await email.InnerTextAsync());
                LocatorBoundingBoxResult title = (await Page.Locator(".po-header__title").BoundingBoxAsync())!;
                LocatorBoundingBoxResult emailBox = (await email.BoundingBoxAsync())!;
                Assert.Equal("PoTraffic", (await Page.Locator(".po-header__title").InnerTextAsync()).Trim());
                Assert.True(title.X < 40 && title.Y < 56, $"title not top-left at {width}px on {route}");
                Assert.True(emailBox.X > title.X + title.Width && emailBox.Y < 56, $"email not top-right at {width}px on {route}");

                await AssertStampAsync(width, route);
                if (width < 641)
                    Assert.Equal(1, await Page.Locator(".pt-bottom-nav").CountAsync());
            }
        }
    }

    [SkipUnlessE2EReady]
    public async Task SignedOut_HasNoBottomNav_AndTheSignInPageIsTitlePlusButton()
    {
        foreach ((int width, int height) in Sizes)
        {
            await Page.SetViewportSizeAsync(width, height);

            await Page.GotoAsync($"{BaseUrl}/diag");
            await Page.GetByTestId("diag-banner").WaitForAsync(new() { Timeout = 30_000 });
            Assert.Equal(0, await Page.Locator(".pt-bottom-nav").CountAsync());
            await AssertStampAsync(width, "/diag");

            await Page.GotoAsync($"{BaseUrl}/login");
            ILocator buttons = Page.Locator("main button");
            await buttons.First.WaitForAsync(new() { Timeout = 30_000 });
            await Page.WaitForTimeoutAsync(500);
            Assert.Equal("PoTraffic", (await Page.Locator("main h1").InnerTextAsync()).Trim());
            Assert.Equal(0, await Page.Locator(".po-header__actions").CountAsync());
            Assert.Equal(0, await Page.Locator("main p").CountAsync());
            // One sign-in button. The guest button exists only on Development/Test hosts, where it
            // replaces the Microsoft one unless Microsoft sign-in is configured too.
            string[] names = [.. (await buttons.AllInnerTextsAsync()).Select(t => t.Trim())];
            Assert.NotEmpty(names);
            Assert.All(names, n => Assert.Contains(n, SignInButtons));
            Assert.True(names.Count(n => n != "Continue as Guest") <= 1, string.Join(", ", names));
            await AssertStampAsync(width, "/login");
        }
    }

    private async Task AssertStampAsync(int width, string route)
    {
        ILocator stamp = Page.Locator(".po-version");
        Assert.Matches(new Regex(@"^v\d{6}$"), (await stamp.InnerTextAsync()).Trim());
        string position = await stamp.EvaluateAsync<string>("e => getComputedStyle(e).position");
        Assert.Equal(width <= 768 ? "static" : "fixed", position);

        string[] overlaps = await Page.EvaluateAsync<string[]>(OverlapScript);
        Assert.True(overlaps.Length == 0, $"chrome covers content at {width}px on {route}: {string.Join("; ", overlaps)}");
        int scrollWidth = await Page.EvaluateAsync<int>("document.documentElement.scrollWidth");
        Assert.True(scrollWidth <= width, $"{route} scrolls sideways at {width}px (scrollWidth={scrollWidth})");
    }
}
