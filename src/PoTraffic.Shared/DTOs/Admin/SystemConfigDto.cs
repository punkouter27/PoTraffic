namespace PoTraffic.Shared.DTOs.Admin;

/// <param name="IsEditable">
/// True when <c>PUT /api/admin/configuration/{key}</c> accepts a new value for this row (cost
/// rates, limits and feature flags). The server is the authority; this saves the client from
/// keeping its own copy of the editable-key list.
/// </param>
public sealed record SystemConfigDto(
    string Key,
    string Value,
    string? Description,
    bool IsSensitive,
    bool IsEditable = false);