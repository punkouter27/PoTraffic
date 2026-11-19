using PoTraffic.Mobile.Pages;

namespace PoTraffic.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("login", typeof(LoginPage));
        Routing.RegisterRoute("create", typeof(CreateRoutePage));
        Routing.RegisterRoute("route", typeof(RouteDetailPage));
        Routing.RegisterRoute("admin", typeof(AdminPage));
        Routing.RegisterRoute("diag", typeof(DiagPage));
    }
}
