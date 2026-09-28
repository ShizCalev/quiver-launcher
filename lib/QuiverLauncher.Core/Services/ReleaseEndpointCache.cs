using System.Text.Json;

namespace QuiverLauncher.Core.Services;

/// <summary>Validators are meaningful only alongside the payload for the same URL and credentials.</summary>
public sealed class ReleaseEndpointCache
{
    public sealed record Entry(string Body, string? ETag, DateTimeOffset ValidatedAt);
    public sealed record RateLimitEntry(long? Limit, long Remaining, DateTimeOffset? ResetAt, DateTimeOffset ObservedAt);
    private readonly object _gate = new();
    private readonly string? _path;
    private readonly string? _rateLimitPath;
    private readonly Dictionary<string, Entry> _entries;
    private readonly Dictionary<string, RateLimitEntry> _rateLimits;

    public ReleaseEndpointCache(string? directory = null)
    {
        _path = directory == null ? null : Path.Combine(directory, "release_endpoints_v1.json");
        _rateLimitPath = directory == null ? null : Path.Combine(directory, "release_rate_limits_v1.json");
        try
        {
            _entries = _path != null && File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _entries = []; }
        try
        {
            _rateLimits = _rateLimitPath != null && File.Exists(_rateLimitPath)
                ? JsonSerializer.Deserialize<Dictionary<string, RateLimitEntry>>(File.ReadAllText(_rateLimitPath)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _rateLimits = []; }
    }

    public Entry? Get(string key) { lock (_gate) return _entries.GetValueOrDefault(key); }

    public void Set(string key, Entry entry)
    {
        lock (_gate)
        {
            _entries[key] = entry;
            if (_path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_entries));
                File.Move(_path + ".tmp", _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Debug.WriteLine("Could not persist release endpoint cache."); }
        }
    }

    public RateLimitEntry? GetRateLimit(string key) { lock (_gate) return _rateLimits.GetValueOrDefault(key); }

    public void SetRateLimit(string key, RateLimitEntry entry)
    {
        lock (_gate)
        {
            _rateLimits[key] = entry;
            PersistRateLimits();
        }
    }

    public RateLimitEntry UpdateRateLimit(string key, Func<RateLimitEntry?, RateLimitEntry> update)
    {
        lock (_gate)
        {
            var entry = update(_rateLimits.GetValueOrDefault(key));
            _rateLimits[key] = entry;
            PersistRateLimits();
            return entry;
        }
    }

    private void PersistRateLimits()
    {
        if (_rateLimitPath == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_rateLimitPath)!);
            File.WriteAllText(_rateLimitPath + ".tmp", JsonSerializer.Serialize(_rateLimits));
            File.Move(_rateLimitPath + ".tmp", _rateLimitPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Debug.WriteLine("Could not persist release rate-limit cache."); }
    }
}
