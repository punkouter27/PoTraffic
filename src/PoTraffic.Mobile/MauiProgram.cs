using Microsoft.Extensions.Logging;
using PoTraffic.Mobile.Pages;
using PoTraffic.Mobile.Services;

namespace PoTraffic.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<MobileConfig>();
        builder.Services.AddSingleton<SecureSessionStore>();
        builder.Services.AddSingleton<OfflineRouteCache>();
        builder.Services.AddTransient<AuthDelegatingHandler>();
        builder.Services.AddSingleton(sp =>
        {
            var handler = sp.GetRequiredService<AuthDelegatingHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PoTraffic.Mobile/1.0");
            return http;
        });
        builder.Services.AddSingleton<PoTrafficMobileClient>();

        builder.Services.AddTransient<CommutePage>();
        builder.Services.AddTransient<AlertsPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
