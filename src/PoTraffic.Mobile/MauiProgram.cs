using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
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
            .UseMauiMaps()
            .ConfigureLifecycleEvents(Lifecycle)
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
        builder.Services.AddSingleton<EntraAuthService>();
        builder.Services.AddSingleton<OfflineStore>();
        builder.Services.AddSingleton(new CookieContainer());
        builder.Services.AddSingleton<CookieJar>();
        builder.Services.AddTransient<AuthDelegatingHandler>();
        builder.Services.AddSingleton(sp =>
        {
            var handler = sp.GetRequiredService<AuthDelegatingHandler>();
            handler.InnerHandler = new HttpClientHandler { CookieContainer = sp.GetRequiredService<CookieContainer>(), UseCookies = true };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PoTraffic.Mobile/1.0");
            return http;
        });
        builder.Services.AddSingleton<ApiClient>();
        builder.Services.AddSingleton<Session>();
        builder.Services.AddSingleton<LeaveAlerts>();
        builder.Services.AddSingleton<CommuteFences>();
#if ANDROID
        builder.Services.AddSingleton<IAlarmScheduler, AndroidAlarms>();
        builder.Services.AddSingleton<IGeofenceRegistrar, AndroidGeofences>();
#elif IOS
        builder.Services.AddSingleton<IAlarmScheduler, IosAlarms>();
        builder.Services.AddSingleton<IGeofenceRegistrar, IosGeofences>();
#endif

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<CreateRoutePage>();
        builder.Services.AddTransient<RouteDetailPage>();
        builder.Services.AddTransient<AlertsPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AdminPage>();
        builder.Services.AddTransient<DiagPage>();

        MauiApp app = builder.Build();

        // A Debug guest session survives restarts and background wake-ups.
        app.Services.GetRequiredService<CookieJar>().RestoreAsync().GetAwaiter().GetResult();
        return app;
    }

    // Notification taps arrive as potraffic://route/{id} intents (Android) or through the
    // notification delegate (iOS). iOS also starts listening for region crossings at launch,
    // because a crossing can relaunch the app in the background.
    private static void Lifecycle(ILifecycleBuilder events)
    {
#if ANDROID
        events.AddAndroid(android => android
            .OnCreate((activity, _) => Forward(activity.Intent))
            .OnNewIntent((_, intent) => Forward(intent)));

        static void Forward(Android.Content.Intent? intent)
        {
            if (intent?.Action == Android.Content.Intent.ActionView && intent.DataString is { } data && Uri.TryCreate(data, UriKind.Absolute, out var uri))
            {
                Application.Current?.SendOnAppLinkRequestReceived(uri);
            }
        }
#elif IOS
        events.AddiOS(ios => ios.FinishedLaunching((_, _) =>
        {
            UserNotifications.UNUserNotificationCenter.Current.Delegate = new IosAlarms.Delegate();
            IosGeofences.Listen();
            return true;
        }));
#endif
    }
}
