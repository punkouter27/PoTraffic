using Foundation;
using PoTraffic.Mobile.Services;
using UserNotifications;

namespace PoTraffic.Mobile;

/// <summary>Leave-by alerts as Time Sensitive local notifications (they break through Focus).</summary>
public sealed class IosAlarms : IAlarmScheduler
{
    private const string RouteKey = "route";

    private static UNUserNotificationCenter Center => UNUserNotificationCenter.Current;

    public async Task<bool> RequestPermissionAsync()
    {
        (bool granted, _) = await Center.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.TimeSensitive);
        return granted;
    }

    public void Schedule(ScheduledLeaveAlert alert)
    {
        DateTime local = alert.FireAt.LocalDateTime;
        var when = new NSDateComponents { Year = local.Year, Month = local.Month, Day = local.Day, Hour = local.Hour, Minute = local.Minute };
        Add(alert.Id, alert.Title, alert.Body, alert.RouteId, UNCalendarNotificationTrigger.CreateTrigger(when, repeats: false));
    }

    public void Cancel(int id) => Center.RemovePendingNotificationRequests([Identifier(id)]);

    public void ShowNow(int id, string title, string body, string routeId) => Add(id, title, body, routeId, trigger: null);

    private static void Add(int id, string title, string body, string routeId, UNNotificationTrigger? trigger)
    {
        var content = new UNMutableNotificationContent
        {
            Title = title,
            Body = body,
            Sound = UNNotificationSound.Default,
            InterruptionLevel = UNNotificationInterruptionLevel.TimeSensitive,
            UserInfo = NSDictionary.FromObjectAndKey(new NSString(routeId), new NSString(RouteKey)),
        };
        Center.AddNotificationRequest(UNNotificationRequest.FromIdentifier(Identifier(id), content, trigger), _ => { });
    }

    private static string Identifier(int id) => $"leave-{id}";

    /// <summary>Shows alerts while the app is open, and opens the route when one is tapped.</summary>
    public sealed class Delegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler)
            => completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound | UNNotificationPresentationOptions.List);

        public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
        {
            if (response.Notification.Request.Content.UserInfo[RouteKey] is NSString route)
            {
                Microsoft.Maui.Controls.Application.Current?.SendOnAppLinkRequestReceived(new Uri($"{MobileConfig.AppScheme}://route/{route}"));
            }

            completionHandler();
        }
    }
}
