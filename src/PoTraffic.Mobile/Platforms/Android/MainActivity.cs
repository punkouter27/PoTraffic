using Android.App;
using Android.Content;
using Android.Content.PM;
using PoTraffic.Mobile.Services;

namespace PoTraffic.Mobile;

// potraffic://route/{id}: what a leave-by or geofence notification opens (also an adb test hook).
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = MobileConfig.AppScheme,
    DataHost = "route")]
public class MainActivity : MauiAppCompatActivity
{
}
