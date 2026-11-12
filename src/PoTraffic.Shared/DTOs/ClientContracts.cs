using PoTraffic.Shared.Ids;

namespace PoTraffic.Shared.DTOs;

public sealed record ProvidersResponse(List<string> Providers, bool GuestEnabled);

public sealed record FeatureFlags(bool UseMockProviders, bool EnableWeather);

public sealed record CheckNowResponse(int DurationSeconds, int DistanceMetres);

public sealed record StopSessionRequest(SessionId SessionId);

public sealed record SaveWindowRequest(
    string StartTime,
    string EndTime,
    byte DaysOfWeekMask,
    string? TimeZoneId,
    WindowId? ReplacesWindowId = null);

public sealed record SetTimeZoneRequest(string TimeZoneId);

public sealed record PushKeyResponse(string PublicKey);

public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth);

public sealed record PushUnsubscribeRequest(string Endpoint);

public sealed record SetArriveByRequest(string? ArriveBy);

public sealed record SetRouteNameRequest(string? Name);

public sealed record SetRoutePausedRequest(bool Paused);

public sealed record SetRouteAddressesRequest(string OriginAddress, string DestinationAddress);

public sealed record CreateSampleRouteRequest(int UtcOffsetMinutes);
