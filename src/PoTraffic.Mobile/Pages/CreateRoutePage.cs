using System.Net;
using System.Net.Http.Json;
using PoTraffic.Mobile.Services;
using PoTraffic.Shared.Constants;
using PoTraffic.Shared.DTOs.Routes;
using PoTraffic.Shared.Enums;

namespace PoTraffic.Mobile.Pages;

/// <summary>New commute: two addresses (autocomplete, or this phone's position), an optional arrive-by, and the schedule.</summary>
public sealed class CreateRoutePage : ContentPage
{
    private readonly ApiClient _api;
    private readonly Entry _origin = new() { Placeholder = "From (address or place)", MaxLength = ValidationConstants.AddressMaxLength };
    private readonly Entry _destination = new() { Placeholder = "To (address or place)", MaxLength = ValidationConstants.AddressMaxLength };
    private readonly VerticalStackLayout _originHits = new() { Spacing = 2 };
    private readonly VerticalStackLayout _destinationHits = new() { Spacing = 2 };
    private readonly Switch _hasArriveBy = new();
    private readonly TimePicker _arriveBy = new() { Time = new TimeSpan(9, 0, 0), IsEnabled = false };
    private readonly TimePicker _start = new() { Time = TimeSpan.Zero };
    private readonly TimePicker _end = new() { Time = new TimeSpan(23, 59, 0) };
    private readonly Label _error = Ui.Colored(null, Ui.Bad, 14);
    private byte _days = 0x1F; // Mon–Fri
    private int _typingVersion;

    public CreateRoutePage(ApiClient api)
    {
        _api = api;
        Title = "New commute";
        Wire(_origin, _originHits);
        Wire(_destination, _destinationHits);

        // An arrive-by target shapes the window: it ends then and starts three hours before.
        _hasArriveBy.Toggled += (_, e) => { _arriveBy.IsEnabled = e.Value; if (e.Value) { FitWindow(); } };
        _arriveBy.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TimePicker.Time) && _hasArriveBy.IsToggled) { FitWindow(); } };

        Content = new ScrollView
        {
            Content = Ui.Stack(10,
                Ui.H1("New commute"),
                Ui.Text("From"), _origin,
                Ui.Secondary("📍 Use my location", UseMyLocationAsync),
                _originHits,
                Ui.Text("To"), _destination, _destinationHits,
                Ui.H2("Arrive by"),
                Ui.Field("I need to be there by a set time", _hasArriveBy),
                _arriveBy,
                Ui.Muted("With an arrive-by time, PoTraffic answers \"when should I leave?\" and can alert you."),
                Ui.H2("When to sample"),
                Ui.Field("From", _start),
                Ui.Field("Until", _end),
                Ui.DayPicker(_days, d => _days = d),
                _error,
                Ui.Primary("Create commute", SaveAsync)),
            Padding = 16,
        };
    }

    private void FitWindow()
    {
        TimeSpan end = _arriveBy.Time ?? TimeSpan.FromHours(9);
        TimeSpan start = end - TimeSpan.FromHours(3);
        _end.Time = end;
        _start.Time = start < TimeSpan.Zero ? start + TimeSpan.FromDays(1) : start;
    }

    // Autocomplete after a short pause in typing; tapping a suggestion fills the field.
    private void Wire(Entry entry, VerticalStackLayout hits)
    {
        entry.TextChanged += async (_, e) =>
        {
            int version = ++_typingVersion;
            CancellationToken ct = CancellationToken.None;
            string q = e.NewTextValue?.Trim() ?? string.Empty;
            try
            {
                await Task.Delay(300, ct);
                if (version != _typingVersion)
                {
                    return;
                }

                hits.Clear();
                if (q.Length < 3 || !ApiClient.Online)
                {
                    return;
                }

                foreach (PlaceSuggestionDto hit in (await _api.SearchPlacesAsync(q, ct) ?? []).Take(5))
                {
                    Label row = new() { Text = hit.Description, Padding = new Thickness(4, 6), TextColor = Ui.Accent };
                    TapGestureRecognizer tap = new();
                    tap.Tapped += (_, _) => { entry.Text = hit.Description; hits.Clear(); entry.Unfocus(); };
                    row.GestureRecognizers.Add(tap);
                    hits.Add(row);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
            {
            }
        };
    }

    private async Task UseMyLocationAsync()
    {
        try
        {
            if (await Permissions.RequestAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted)
            {
                _error.Text = "Location is off for PoTraffic — type the address instead.";
                return;
            }

            Location? here = await Geolocation.Default.GetLastKnownLocationAsync() is { } last && DateTimeOffset.UtcNow - last.Timestamp < TimeSpan.FromMinutes(2)
                ? last
                : await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(10)));
            if (here is null)
            {
                _error.Text = "Couldn't get a GPS fix — type the address instead.";
                return;
            }

            Placemark? place = (await Geocoding.Default.GetPlacemarksAsync(here))?.FirstOrDefault();
            _origin.Text = place is null
                ? FormattableString.Invariant($"{here.Latitude:F6},{here.Longitude:F6}")
                : string.Join(", ", new[] { $"{place.SubThoroughfare} {place.Thoroughfare}".Trim(), place.Locality, place.AdminArea, place.PostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
            _error.Text = null;
        }
        catch (Exception ex) when (ex is FeatureNotEnabledException or FeatureNotSupportedException or PermissionException)
        {
            _error.Text = "Location services are off — type the address instead.";
        }
    }

    private async Task SaveAsync()
    {
        _error.Text = null;
        if (string.IsNullOrWhiteSpace(_origin.Text) || string.IsNullOrWhiteSpace(_destination.Text))
        {
            _error.Text = "Enter both addresses.";
            return;
        }

        if (_days == 0)
        {
            _error.Text = "Select at least one day to probe.";
            return;
        }

        if (Format.HHmm(_start.Time) == Format.HHmm(_end.Time))
        {
            _error.Text = "End time must be different from start time.";
            return;
        }

        try
        {
            using HttpResponseMessage created = await _api.CreateRouteAsync(new CreateRouteRequest(
                _origin.Text.Trim(), _destination.Text.Trim(), RouteProvider.GoogleMaps,
                Format.HHmm(_start.Time), Format.HHmm(_end.Time), _days, TimeZoneInfo.Local.Id));

            if (!created.IsSuccessStatusCode)
            {
                (string? code, string message) = await ApiClient.ReadProblemAsync(created, "Failed to create commute. Please verify your addresses.");
                _error.Text = code switch
                {
                    RouteErrorCodes.MapsKeyMissing => "Maps integration is not configured on the server.",
                    RouteErrorCodes.AddressNotFound => "We couldn't find that address. Please check and try again.",
                    RouteErrorCodes.SameCoordinates => "Origin and destination resolve to the same location. Please use different addresses.",
                    RouteErrorCodes.SpendingCapReached => "The app's monthly spending cap is reached; new commutes can't be checked until it resets.",
                    _ when (int)created.StatusCode >= 500 => "Something went wrong on our end. Please try again in a moment.",
                    _ => message,
                };
                return;
            }

            RouteDto? route = await created.Content.ReadFromJsonAsync(MobileJsonContext.Default.RouteDto);
            if (route is null)
            {
                await Shell.Current.GoToAsync("..");
                return;
            }

            if (_hasArriveBy.IsToggled)
            {
                using HttpResponseMessage _ = await _api.SetArriveByAsync(route.Id, Format.HHmm(_arriveBy.Time));
            }

            if (route.Windows.FirstOrDefault(w => w.IsActive) is { } window)
            {
                using HttpResponseMessage started = await _api.StartWindowAsync(route.Id, window.Id);
                if (started.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await Ui.Toast("Commute created, but today's monitoring quota is used up. Monitoring starts tomorrow.");
                }
            }

            await Shell.Current.GoToAsync($"../route?id={route.Id.Value}");
        }
        catch (HttpRequestException)
        {
            _error.Text = "Creating a commute needs a connection.";
        }
    }
}
