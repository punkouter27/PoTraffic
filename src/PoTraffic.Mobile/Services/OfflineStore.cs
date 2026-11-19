using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PoTraffic.Mobile.Services;

/// <summary>
/// Small JSON documents in the app's private folder: the dashboard snapshot, alerts, and the
/// leave-alert / geofence registrations. Written via temp file + rename, so a crash never leaves
/// half a file; an unreadable file reads as absent.
/// </summary>
public sealed class OfflineStore : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    private static string PathFor(string key) => Path.Combine(FileSystem.AppDataDirectory, $"potraffic-{key}.json");

    public async Task SaveAsync<T>(string key, T value, JsonTypeInfo<T> type)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string path = PathFor(key), temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(value, type)).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T?> LoadAsync<T>(string key, JsonTypeInfo<T> type)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string path = PathFor(key);
            if (!File.Exists(path))
            {
                return default;
            }

            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, type).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return default;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Sync read for broadcast receivers that run before any page exists.</summary>
    public static T? LoadNow<T>(string key, JsonTypeInfo<T> type)
    {
        try
        {
            string path = PathFor(key);
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllBytes(path), type) : default;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return default;
        }
    }

    public static void Clear()
    {
        foreach (string file in Directory.EnumerateFiles(FileSystem.AppDataDirectory, "potraffic-*.json"))
        {
            File.Delete(file);
        }
    }
}
