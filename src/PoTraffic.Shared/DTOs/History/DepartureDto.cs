namespace PoTraffic.Shared.DTOs.History;

/// <summary>
/// The answer to "when should I leave" for one route and weekday
/// (<c>GET /api/routes/{routeId}/departure</c>).
/// </summary>
/// <param name="Best">The quickest slot of the day, or null before any sample exists.</param>
/// <param name="Plan">The leave-by plan, or null when the route has no arrive-by target.</param>
public sealed record DepartureDto(OptimalDepartureDto? Best, DeparturePlanDto? Plan);
