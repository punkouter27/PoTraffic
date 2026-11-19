using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using PoTraffic.Mobile.Services;
using AndroidApp = Android.App.Application;

namespace PoTraffic.Mobile;

/// <summary>Leave-by alarms through AlarmManager (exact when allowed, else inexact) and posted notifications.</summary>
public sealed class AndroidAlarms : IAlarmScheduler
{
    public const string ChannelId = "leave";
    private const string ExtraId = "id", ExtraTitle = "title", ExtraBody = "body", ExtraRoute = "route";

    private static Context Ctx => AndroidApp.Context;

    public async Task<bool> RequestPermissionAsync()
    {
        EnsureChannel(Ctx);
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && await Permissions.RequestAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
        {
            return false;
        }

        return true;
    }

    public void Schedule(ScheduledLeaveAlert alert)
    {
        var intent = new Intent(Ctx, typeof(LeaveAlarmReceiver))
            .PutExtra(ExtraId, alert.Id).PutExtra(ExtraTitle, alert.Title).PutExtra(ExtraBody, alert.Body).PutExtra(ExtraRoute, alert.RouteId);
        PendingIntent pending = PendingIntent.GetBroadcast(Ctx, alert.Id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        var alarms = (AlarmManager)Ctx.GetSystemService(Context.AlarmService)!;
        long at = alert.FireAt.ToUnixTimeMilliseconds();

        // Android 12+ makes exact alarms opt-in; without it the alert may land a few minutes late.
        if (OperatingSystem.IsAndroidVersionAtLeast(31) && !alarms.CanScheduleExactAlarms())
        {
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
        }
        else
        {
            alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
        }
    }

    public void Cancel(int id)
    {
        var intent = new Intent(Ctx, typeof(LeaveAlarmReceiver));
        if (PendingIntent.GetBroadcast(Ctx, id, intent, PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable) is { } pending)
        {
            ((AlarmManager)Ctx.GetSystemService(Context.AlarmService)!).Cancel(pending);
            pending.Cancel();
        }
    }

    public void ShowNow(int id, string title, string body, string routeId) => Post(Ctx, id, title, body, routeId);

    internal static void Post(Context context, int id, string title, string body, string routeId)
    {
        EnsureChannel(context);
        var open = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse($"{MobileConfig.AppScheme}://route/{routeId}"));
        open.SetPackage(context.PackageName);
        PendingIntent tap = PendingIntent.GetActivity(context, id, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

        var style = new NotificationCompat.BigTextStyle();
        style.BigText(body);
        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetSmallIcon(Resource.Mipmap.appicon);
        builder.SetContentTitle(title);
        builder.SetContentText(body);
        builder.SetStyle(style);
        builder.SetPriority(NotificationCompat.PriorityHigh);
        builder.SetCategory(NotificationCompat.CategoryReminder);
        builder.SetAutoCancel(true);
        builder.SetContentIntent(tap);
        NotificationManagerCompat.From(context)!.Notify(id, builder.Build()!);
    }

    internal static void OnAlarm(Context context, Intent intent)
        => Post(context, intent.GetIntExtra(ExtraId, 0), intent.GetStringExtra(ExtraTitle) ?? "Time to leave", intent.GetStringExtra(ExtraBody) ?? string.Empty, intent.GetStringExtra(ExtraRoute) ?? string.Empty);

    private static void EnsureChannel(Context context)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Leave-by alerts", NotificationImportance.High)
            {
                Description = "When to leave for a commute, and when PoTraffic starts or stops timing one.",
            });
        }
    }
}

[BroadcastReceiver(Exported = false)]
public sealed class LeaveAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null && intent is not null)
        {
            AndroidAlarms.OnAlarm(context, intent);
        }
    }
}

/// <summary>A reboot clears every alarm and proximity alert; this puts back the future ones.</summary>
[BroadcastReceiver(Exported = true, Enabled = true)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted)
        {
            return;
        }

        var alarms = new AndroidAlarms();
        foreach (ScheduledLeaveAlert alert in OfflineStore.LoadNow("leave-alerts", MobileJsonContext.Default.ListScheduledLeaveAlert) ?? [])
        {
            if (alert.FireAt > DateTimeOffset.Now)
            {
                alarms.Schedule(alert);
            }
        }

        CommuteFences.RestoreAll(new AndroidGeofences());
    }
}
