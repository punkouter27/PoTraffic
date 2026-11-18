using System.Text.Json;
using PoTraffic.Shared.DTOs.Alerts;
using PoTraffic.Shared.DTOs.Routes;

namespace PoTraffic.Mobile.Services;

public sealed class OfflineRouteCache : IDisposable
{
    private readonly string _routesPath = Path.Combine(FileSystem.AppDataDirectory, "potraffic-routes.json");
    private readonly string _alertsPath = Path.Combine(FileSystem.AppDataDirectory, "potraffic-alerts.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public async Task SaveRoutesAsync(IReadOnlyList<RouteDto> routes, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var temp = _routesPath + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(routes), ct).ConfigureAwait(false);
            File.Move(temp, _routesPath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<RouteDto>> LoadRoutesAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_routesPath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<RouteDto>>(await File.ReadAllTextAsync(_routesPath, ct).ConfigureAwait(false)) ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAlertsAsync(IReadOnlyList<AlertDto> alerts, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var temp = _alertsPath + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(alerts), ct).ConfigureAwait(false);
            File.Move(temp, _alertsPath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AlertDto>> LoadAlertsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_alertsPath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<AlertDto>>(await File.ReadAllTextAsync(_alertsPath, ct).ConfigureAwait(false)) ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }
}
