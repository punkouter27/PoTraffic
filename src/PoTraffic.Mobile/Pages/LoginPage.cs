using PoTraffic.Mobile.Services;

namespace PoTraffic.Mobile.Pages;

/// <summary>Sign in: Microsoft (Entra, PKCE in the system browser) or, in Debug builds, the API's guest session.</summary>
public sealed class LoginPage : ContentPage
{
    private readonly ApiClient _api;
    private readonly Session _session;
    private readonly EntraAuthService _entra;
    private readonly MobileConfig _config;
    private readonly CookieJar _cookies;
    private readonly Label _status = Ui.Muted(null);

    public LoginPage(ApiClient api, Session session, EntraAuthService entra, MobileConfig config, CookieJar cookies)
    {
        _api = api;
        _session = session;
        _entra = entra;
        _config = config;
        _cookies = cookies;
        Title = "Sign in";
        Shell.SetTabBarIsVisible(this, false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        VerticalStackLayout body = Ui.Stack(14,
            Ui.H1("PoTraffic"),
            Ui.Text("Know when to leave. PoTraffic times your commutes on a schedule and learns how long they really take."),
            Ui.Primary("Continue with Microsoft", MicrosoftAsync));

        try
        {
            if (MobileConfig.GuestAllowed && await _api.GetProvidersAsync() is { GuestEnabled: true })
            {
                body.Add(Ui.Secondary("Continue as Guest (dev)", GuestAsync));
            }
        }
        catch (HttpRequestException)
        {
            _status.Text = $"Can't reach {_config.BaseUrl}. Check the server address on the Settings tab.";
        }

        body.Add(_status);
        Content = new ScrollView { Content = body, Padding = 20 };
    }

    private async Task MicrosoftAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.EntraClientId))
        {
            _status.Text = "Microsoft sign-in needs the app's Entra client id — set it under Settings → Server.";
            return;
        }

        try
        {
            _cookies.Clear();
            await _entra.SignInWithPkceAsync();
            await FinishAsync();
        }
        catch (TaskCanceledException)
        {
            _status.Text = "Sign-in was cancelled.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Sign-in failed: {ex.Message}";
        }
    }

    private async Task GuestAsync()
    {
        try
        {
            using HttpResponseMessage resp = await _api.GuestLoginAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _status.Text = $"Guest sign-in refused ({(int)resp.StatusCode}).";
                return;
            }

            await _cookies.SaveAsync();
            await FinishAsync();
        }
        catch (HttpRequestException ex)
        {
            _status.Text = $"Can't reach the server: {ex.Message}";
        }
    }

    private async Task FinishAsync()
    {
        if (await _session.RefreshAsync())
        {
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            _status.Text = "Signed in, but the server didn't accept the session.";
        }
    }
}
