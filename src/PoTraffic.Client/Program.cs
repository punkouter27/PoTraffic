using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using PoTraffic.Client;
using PoTraffic.Client.Services;
using PoTraffic.Client.Services.Auth;
using PoTraffic.Client.Services.Http;
using Radzen;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

Uri apiBase = new(builder.HostEnvironment.BaseAddress);

builder.Services.AddScoped(sp =>
{
    DevFakeUserHandler fakeUserHandler = new(
        builder.HostEnvironment,
        sp.GetRequiredService<IJSRuntime>())
    {
        InnerHandler = new HttpClientHandler()
    };

    UnauthorizedRedirectHandler authRedirectHandler = new(sp.GetRequiredService<NavigationManager>())
    {
        InnerHandler = fakeUserHandler
    };

    return new HttpClient(authRedirectHandler)
    {
        BaseAddress = apiBase
    };
});

builder.Services.AddScoped<PoTrafficApiClient>();

// Authentication — cookie-backed BFF auth state provider (GET /api/auth/me)
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<CookieAuthenticationStateProvider>());
builder.Services.AddAuthorizationCore();

// Radzen component services (dialogs, tooltips, notifications, context menus)
builder.Services.AddRadzenComponents();

// Last-known-good payloads, so the dashboard paints before the first round-trip completes.
builder.Services.AddScoped<ClientCache>();

// One shared visibility/connectivity subscription driving every polling page.
builder.Services.AddScoped<PageActivityMonitor>();

// Holds deletes for a grace period so "Undo" has something to undo.
builder.Services.AddScoped<PendingDeletionService>();

// Service-worker registration, offline caching and the install prompt.
builder.Services.AddScoped<PwaService>();

// The motion setting that governs the chart draw-in and the map pins' pulse.
builder.Services.AddScoped<FxService>();

// One once-a-second heartbeat shared by every live countdown, instead of a timer each.
builder.Services.AddScoped<ClockTicker>();

// Which advanced panels (map, weather, sparklines) the signed-in user has switched on.
builder.Services.AddScoped<UiPreferences>();

WebAssemblyHost host = builder.Build();

await host.Services.GetRequiredService<UiPreferences>().LoadAsync();

await host.RunAsync();
