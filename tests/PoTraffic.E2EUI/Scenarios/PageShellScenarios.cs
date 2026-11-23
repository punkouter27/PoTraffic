using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PoTraffic.E2EUI.Helpers;

namespace PoTraffic.E2EUI.Scenarios;

/// <summary>
/// The Po page shell (AGENTS.md, Page shell) at desktop and phone widths: title top-left, the
/// signed-in email top-right, exactly (through a real test sign-in: the dev-only fake sign-in URL),
/// the vYYMMDD stamp as a <footer> bottom-right on desktop and after the page content on phones,
/// nothing in the header or the stamp overlapping content, an h1 on every page, the sign-in page
/// reduced to the title and its one Microsoft button, no navigation for a signed-out visitor, and
/// Radzen's CSS loaded once, before the app's own.
/// </summary>
public sealed class PageShellScenarios : PlaywrightTestBase
{
    private static readonly (int Width, int Height)[] Sizes = [(1366, 800), (390, 844)];
    private static readonly string[] SignedInRoutes = ["/dashboard", "/routes/create", "/diag", "/settings"];
    private static readonly string[] SignedOutRoutes = ["/login", "/diag", "/settings", "/no-such-page"];

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
        string signedInEmail = await SignInAsGuestAsync();

        foreach ((int width, int height) in Sizes)
        {
            await Page.SetViewportSizeAsync(width, height);
            foreach (string route in SignedInRoutes)
            {
                await Page.GotoAsync($"{BaseUrl}{route}");
                ILocator email = Page.Locator(".po-header .po-header__actions .po-header__email");
                await email.WaitForAsync(new() { Timeout = 30_000 });
                await Page.WaitForTimeoutAsync(500);

                Assert.Equal(signedInEmail, (await email.InnerTextAsync()).Trim());
                await AssertOneH1Async(route);
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
    public async Task SignedOut_HasNoNavigation_AndTheSignInPageIsTitlePlusOneMicrosoftButton()
    {
        foreach ((int width, int height) in Sizes)
        {
            await Page.SetViewportSizeAsync(width, height);

            foreach (string route in SignedOutRoutes)
            {
                await Page.GotoAsync($"{BaseUrl}{route}");
                await Page.Locator("main h1").WaitForAsync(new() { Timeout = 30_000 });
                await Page.WaitForTimeoutAsync(500);
                // Navigation is for signed-in users only: no bottom nav, no <nav> at all, no email.
                Assert.Equal(0, await Page.Locator(".pt-bottom-nav").CountAsync());
                Assert.Equal(0, await Page.Locator("nav").CountAsync());
                Assert.Equal(0, await Page.Locator(".po-header__email").CountAsync());
                await AssertOneH1Async(route);
                await AssertStampAsync(width, route);
            }

            await Page.GotoAsync($"{BaseUrl}/login");
            ILocator buttons = Page.Locator("main button");
            await buttons.First.WaitForAsync(new() { Timeout = 30_000 });
            await Page.WaitForTimeoutAsync(500);
            Assert.Equal("PoTraffic", (await Page.Locator("main h1").InnerTextAsync()).Trim());
            Assert.Equal(0, await Page.Locator(".po-header__actions").CountAsync());
            Assert.Equal(0, await Page.Locator("main p").CountAsync());
            // Exactly one button, the Microsoft one, in every environment (no guest button: the
            // browser tests sign in through the dev-only fake sign-in URL instead).
            string[] names = [.. (await buttons.AllInnerTextsAsync()).Select(t => t.Trim())];
            Assert.Equal(["Sign in with Microsoft"], names);
        }
    }

    /// <summary>
    /// Radzen's theme loads once, first: the first stylesheet in &lt;head&gt; is css/vendor.css, which
    /// imports Radzen's default.css (into the vendor layer) before any app CSS; no other copy
    /// (no &lt;RadzenTheme&gt;, no second link). A pt-plain Radzen button still wears the app look.
    /// </summary>
    [SkipUnlessE2EReady]
    public async Task Radzen_Css_Loads_Once_Before_The_App_Css()
    {
        await Page.GotoAsync($"{BaseUrl}/login");
        await Page.Locator("main h1").WaitForAsync(new() { Timeout = 30_000 });

        string[] links = await Page.EvaluateAsync<string[]>(
            "() => [...document.head.querySelectorAll('link[rel=stylesheet]')].map(l => l.getAttribute('href'))");
        Assert.Equal("css/vendor.css", links[0]);
        int app = Array.FindIndex(links, l => l is "css/po-tokens.css" or "css/app.css" or "PoTraffic.Client.bundle.scp.css");
        Assert.True(app > 0, string.Join(", ", links));
        Assert.DoesNotContain(links, l => l.Contains("Radzen.Blazor", StringComparison.Ordinal));

        string[] radzen = await Page.EvaluateAsync<string[]>("""
            () => {
                const found = [];
                const walk = (sheet, path) => {
                    let rules = [];
                    try { rules = [...sheet.cssRules]; } catch { return; }
                    for (const r of rules) if (r.styleSheet) {
                        if ((r.href || '').includes('Radzen.Blazor/css/')) found.push(path + ' > ' + r.href);
                        walk(r.styleSheet, path + ' > ' + r.href);
                    }
                };
                for (const s of document.styleSheets) {
                    if ((s.href || '').includes('Radzen.Blazor/css/')) found.push(s.href);
                    walk(s, s.href || 'inline');
                }
                return found;
            }
            """);
        string only = Assert.Single(radzen);
        Assert.Contains("/css/vendor.css > ../_content/Radzen.Blazor/css/default.css", only);
    }

    private async Task AssertOneH1Async(string route)
    {
        Assert.True(await Page.Locator("h1").CountAsync() == 1, $"{route} must have exactly one h1");
        Assert.False(string.IsNullOrWhiteSpace(await Page.Locator("h1").InnerTextAsync()), $"{route} h1 is empty");
    }

    private async Task AssertStampAsync(int width, string route)
    {
        ILocator stamp = Page.Locator(".po-version");
        Assert.Matches(new Regex(@"^v\d{6}$"), (await stamp.InnerTextAsync()).Trim());
        // A <footer>, not a hidden span: no text is left outside a landmark (axe "region").
        Assert.Equal("FOOTER", await stamp.EvaluateAsync<string>("e => e.tagName"));
        Assert.Null(await stamp.GetAttributeAsync("aria-hidden"));
        string position = await stamp.EvaluateAsync<string>("e => getComputedStyle(e).position");
        Assert.Equal(width <= 768 ? "static" : "fixed", position);

        string[] overlaps = await Page.EvaluateAsync<string[]>(OverlapScript);
        Assert.True(overlaps.Length == 0, $"chrome covers content at {width}px on {route}: {string.Join("; ", overlaps)}");
        int scrollWidth = await Page.EvaluateAsync<int>("document.documentElement.scrollWidth");
        Assert.True(scrollWidth <= width, $"{route} scrolls sideways at {width}px (scrollWidth={scrollWidth})");
    }
}
