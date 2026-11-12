using Microsoft.Playwright;
using PoTraffic.E2EUI.Helpers;

namespace PoTraffic.E2EUI;

/// <summary>
/// Template Method pattern — base class for all E2EUI Playwright tests.
/// Fails the test run automatically on any browser console.error, unhandled
/// exception (#blazor-error-ui), page crash, or failed static asset / API request.
/// </summary>
public abstract class PlaywrightTestBase : IAsyncLifetime
{
    private readonly List<string> _browserErrors = [];

    protected IPlaywright Playwright { get; private set; } = null!;
    protected IBrowser Browser { get; private set; } = null!;
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;

    protected static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5150";

    /// <summary>
    /// Opt-in allowlist for negative tests that intentionally trigger an expected HTTP error status.
    /// </summary>
    protected HashSet<int> AllowedErrorStatuses { get; } = [];

    /// <summary>
    /// Opt-in flag for negative tests that intentionally provoke a browser console error.
    /// </summary>
    protected bool SuppressBrowserErrorAssertions { get; set; }

    public async Task InitializeAsync()
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        BrowserTypeLaunchOptions launchOptions = BrowserLaunch.ResolveLaunchOptions();
        bool isHeaded = launchOptions.Headless == false;
        if (isHeaded && string.IsNullOrEmpty(launchOptions.ExecutablePath))
            launchOptions.ExecutablePath = FindCachedChromiumExecutable();

        Browser = await Playwright.Chromium.LaunchAsync(launchOptions);

        Context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            RecordVideoDir = "TestResults/e2e-video"
        });

        Page = await Context.NewPageAsync();

        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
                _browserErrors.Add($"[console.error] {msg.Text}");
        };
        Page.PageError += (_, err) => _browserErrors.Add($"[pageerror] {err}");
        Page.Crash += (_, page) => _browserErrors.Add($"[pagecrash] {page.Url}");
        Page.RequestFailed += (_, req) =>
        {
            // Ignore aborted requests caused by immediate navigation/unload
            if (!string.Equals(req.Failure, "net::ERR_ABORTED", StringComparison.OrdinalIgnoreCase))
                _browserErrors.Add($"[requestfailed] {req.Method} {req.Url} ({req.Failure})");
        };
        Page.Response += (_, resp) =>
        {
            if (resp.Status >= 400 && !AllowedErrorStatuses.Contains(resp.Status))
            {
                // Ignore browser favicon/source-map probes if absent, fail on all app/static/API errors
                if (!resp.Url.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                    _browserErrors.Add($"[http {resp.Status}] {resp.Request.Method} {resp.Url}");
            }
        };
    }

    public async Task DisposeAsync()
    {
        bool blazorErrorVisible = false;
        try
        {
            if (Page is not null && !Page.IsClosed)
            {
                ILocator errorUi = Page.Locator("#blazor-error-ui");
                if (await errorUi.CountAsync() > 0 && await errorUi.IsVisibleAsync())
                    blazorErrorVisible = true;
            }
        }
        catch { /* page already closed */ }

        if (Page is not null) await Page.CloseAsync();
        if (Context is not null) await Context.CloseAsync();
        if (Browser is not null) await Browser.CloseAsync();
        Playwright.Dispose();

        if (!KeepVideoOnSuccess && Page?.Video is not null)
        {
            try
            {
                string? videoPath = await Page.Video.PathAsync();
                if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                    File.Delete(videoPath);
            }
            catch { }
        }

        if (!SuppressBrowserErrorAssertions)
        {
            if (blazorErrorVisible)
                _browserErrors.Add("[#blazor-error-ui] Unhandled Blazor exception banner was visible.");

            if (_browserErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "E2EUI detected browser console/network/Blazor errors:\n - " +
                    string.Join("\n - ", _browserErrors));
            }
        }
    }

    protected bool KeepVideoOnSuccess { get; set; }

    protected async Task AuthenticateViaDevLoginAsync(
        string email,
        string role = "Administrator",
        string destination = "/dashboard",
        IPage? page = null)
    {
        IPage targetPage = page ?? Page;
        await targetPage.GotoAsync($"{BaseUrl}/login");
        await targetPage.EvaluateAsync(
            @"async ([email, role]) => {
                const resp = await fetch('/e2e/dev-login', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ email, role })
                });
                if (!resp.ok) throw new Error('dev-login failed: ' + resp.status);
            }",
            new[] { email, role });
        await targetPage.GotoAsync($"{BaseUrl}{destination}");
    }

    private static string? FindCachedChromiumExecutable()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] roots =
        [
            Path.Combine(localData, "ms-playwright"),
            Path.Combine(homeDir, ".cache", "ms-playwright")
        ];

        foreach (string root in roots.Where(Directory.Exists))
        {
            string? executable = Directory
                .EnumerateFiles(root, OperatingSystem.IsWindows() ? "chrome.exe" : "chrome", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(executable))
                return executable;
        }

        return null;
    }
}
