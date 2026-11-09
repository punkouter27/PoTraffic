namespace PoTraffic.Shared.DTOs.Account;

/// <summary>Body of <c>PUT /api/account/panels/{panel}</c>: switch one advanced panel on or off.</summary>
public sealed record AdvancedPanelRequest(bool On);
