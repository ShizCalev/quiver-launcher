using System.Text.Json;

namespace QuiverLauncher.Core.Services;

/// <summary>Validators are meaningful only alongside the payload for the same URL and credentials.</summary>
public sealed class ReleaseEndpointCache
{
    private const long MaxCachedBodyBytes = 16 * 1024 * 1024;
    public sealed record Entry(string Body, string? ETag, DateTimeOffset ValidatedAt);
    public sealed record RateLimitEntry(long? Limit, long Remaining, DateTimeOffset? ResetAt, DateTimeOffset ObservedAt);
    private readonly object _gate = new();
    private readonly string? _path;
    private readonly string? _rateLimitPath;
    private readonly Dictionary<string, Entry> _entries;
    private readonly Dictionary<string, RateLimitEntry> _rateLimits;
    private bool _entriesDirty;

    public ReleaseEndpointCache(string? directory = null, bool persistEntries = true)
    {
        _path = directory == null || !persistEntries ? null : Path.Combine(directory, "release_endpoints_v1.json");
        _rateLimitPath = directory == null ? null : Path.Combine(directory, "release_rate_limits_v1.json");
        try
        {
            _entries = _path != null && File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _entries = []; }
        if (TrimEntries()) _entriesDirty = true;
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
            TrimEntries();
            _entriesDirty = true;
        }
    }

    private bool TrimEntries()
    {
        var total = _entries.Values.Sum(entry => (long)entry.Body.Length);
        if (total <= MaxCachedBodyBytes) return false;
        foreach (var key in _entries.OrderBy(entry => entry.Value.ValidatedAt).Select(entry => entry.Key).ToArray())
        {
            if (total <= MaxCachedBodyBytes) break;
            if (_entries.Remove(key, out var removed)) total -= removed.Body.Length;
        }
        return true;
    }

    public void FlushEntries()
    {
        lock (_gate)
        {
            if (!_entriesDirty || _path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_entries));
                File.Move(_path + ".tmp", _path, overwrite: true);
                _entriesDirty = false;
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
