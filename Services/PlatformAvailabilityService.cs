using System.Text.Json;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public enum PlatformAvailability { BuildAvailable, NoBuildFound, Unknown }
public sealed record PlatformSupportAddition(string Platform, string ReleaseTag, DateTimeOffset DetectedAt);
public sealed record PlatformAvailabilitySnapshot(CatalogPlatformFlags Platforms, string ReleaseTag, DateTimeOffset ValidatedAt, int SelectionRevision);

/// <summary>Latest-release browsing evidence, independent of installation pins and editable tags.</summary>
public sealed class PlatformAvailabilityService
{
    private const int PolicyRevision = 1;
    private sealed record Observation(PlatformAvailabilitySnapshot Snapshot, List<PlatformSupportAddition> Pending);
    private sealed record History(int Revision, Dictionary<string, Observation> Entries);
    private readonly Dictionary<string, Observation> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly string _path;
    private readonly TimeProvider _clock;
    public event Action? Changed;

    public PlatformAvailabilityService(string directory, TimeProvider? clock = null)
    {
        _path = Path.Combine(directory, "platform-availability-history-v1.json");
        _clock = clock ?? TimeProvider.System;
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<History>(File.ReadAllText(_path)) is { } history &&
                history.Revision == PolicyRevision && history.Entries != null)
                foreach (var entry in history.Entries)
                    if (entry.Value?.Snapshot != null && entry.Value.Pending != null) _entries[entry.Key] = entry.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private static string Key(GameInfo app, AppSettings settings) => JsonSerializer.Serialize(new[]
    {
        CatalogPlatformIndex.Key(app.EffectiveRepositorySource, app.Repository ?? "", null, app.GetReleaseApiToken(settings)),
        RepositorySourceHelper.NormalizeReleaseAssetFilter(app.ReleaseAssetFilter) ?? ""
    });

    public static PlatformAvailabilitySnapshot? Resolve(GameInfo app, AppSettings settings)
    {
        if (app.IsManuallyManaged || !CatalogPlatformIndex.TryGet(app.EffectiveRepositorySource, app.Repository,
            null, app.GetReleaseApiToken(settings), out var metadata) || metadata!.SelectionRevision < 2 ||
            metadata.ValidatedAt == DateTimeOffset.MinValue || string.IsNullOrWhiteSpace(metadata.ReleaseTag)) return null;
        var flags = CatalogPlatformSupport.FromMetadata(metadata, app.ReleaseAssetFilter);
        return flags == CatalogPlatformFlags.None ? null : new(flags, metadata.ReleaseTag, metadata.ValidatedAt, metadata.SelectionRevision);
    }

    public PlatformAvailability GetAvailability(GameInfo app, AppSettings settings, string platform)
    {
        var snapshot = Resolve(app, settings);
        lock (_gate)
            if (_entries.TryGetValue(Key(app, settings), out var previous) &&
                (snapshot == null || previous.Snapshot.ValidatedAt > snapshot.ValidatedAt)) snapshot = previous.Snapshot;
        if (snapshot == null) return PlatformAvailability.Unknown;
        return CatalogPlatformSupport.Matches(snapshot.Platforms, CatalogPlatformSupport.ParseFilters([platform]))
            ? PlatformAvailability.BuildAvailable : PlatformAvailability.NoBuildFound;
    }

    public bool Matches(GameInfo app, AppSettings settings, TagDisplayFilter filter) =>
        string.IsNullOrWhiteSpace(filter.Platform) || GetAvailability(app, settings, filter.Platform) == filter.Availability;

    public IReadOnlyList<PlatformSupportAddition> Pending(GameInfo app, AppSettings settings)
    {
        lock (_gate) return _entries.TryGetValue(Key(app, settings), out var entry) ? entry.Pending.ToArray() : [];
    }

    public void Observe(IEnumerable<GameInfo> apps, AppSettings settings)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var app in apps)
            {
                var next = Resolve(app, settings);
                if (next == null) continue;
                var key = Key(app, settings);
                _entries.TryGetValue(key, out var previous);
                if (previous != null && next.ValidatedAt <= previous.Snapshot.ValidatedAt) continue;
                if (previous?.Snapshot.SelectionRevision != next.SelectionRevision) previous = null;
                var pending = previous?.Pending.Where(p => CatalogPlatformSupport.Matches(next.Platforms,
                    CatalogPlatformSupport.ParseFilters([p.Platform]))).ToList() ?? [];
                if (previous != null)
                    foreach (var platform in CatalogPlatformSupport.KnownPlatforms)
                    {
                        var flag = CatalogPlatformSupport.ParseFilters([platform]);
                        if (CatalogPlatformSupport.Matches(next.Platforms, flag) &&
                            !CatalogPlatformSupport.Matches(previous.Snapshot.Platforms, flag))
                            pending.Add(new(platform, next.ReleaseTag, _clock.GetUtcNow()));
                    }
                _entries[key] = new(next, pending);
                changed = true;
            }
            if (changed) Flush();
        }
        if (changed) Changed?.Invoke();
    }

    public void MarkReviewed(IEnumerable<GameInfo> apps, AppSettings settings, IReadOnlyList<string>? platforms = null)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var app in apps)
                if (_entries.TryGetValue(Key(app, settings), out var entry) && entry.Pending.Count > 0)
                    changed |= entry.Pending.RemoveAll(change => platforms == null || CatalogPlatformSupport.IsAll(platforms) ||
                        CatalogPlatformSupport.IsSelected(platforms, change.Platform)) > 0;
            if (changed) Flush();
        }
        if (changed) Changed?.Invoke();
    }

    private void Flush()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(new History(PolicyRevision, _entries)));
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Debug.WriteLine($"Platform history could not be saved: {ex.GetType().Name}"); }
    }

    public async Task RefreshAsync(HttpClient client, IEnumerable<GameInfo> apps, AppSettings settings,
        CancellationToken cancellationToken, bool allowNetwork = true, bool force = false)
    {
        var list = apps.ToArray();
        Observe(list, settings);
        if (!allowNetwork) return;
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var paused = new HashSet<string>();
            foreach (var app in list.Where(a => !a.IsManuallyManaged && !string.IsNullOrWhiteSpace(a.Repository))
                .DistinctBy(a => CatalogPlatformIndex.Key(a.EffectiveRepositorySource, a.Repository!, null, a.GetReleaseApiToken(settings))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var token = app.GetReleaseApiToken(settings);
                var context = app.EffectiveRepositorySource + ReleaseRequestCoordinator.CredentialKey(token);
                if (paused.Contains(context) || !force && CatalogPlatformIndex.IsFresh(app.EffectiveRepositorySource, app.Repository!, null, token)) continue;
                try
                {
                    var release = await CatalogReleaseSelection.FetchSelectedAsync(client, app.EffectiveRepositorySource,
                        app.Repository!, null, token, cancellationToken).ConfigureAwait(false);
                    // Empty/failed responses cannot erase verified availability or create support changes.
                    if (release != null)
                    {
                        CatalogPlatformIndex.Set(app.EffectiveRepositorySource, app.Repository!, null, token, release);
                        Observe(list, settings);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (ReleaseFetchException ex)
                {
                    if (ex.Result.IsRateLimited || ex.StatusCode == System.Net.HttpStatusCode.Unauthorized) paused.Add(context);
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
            }
            Observe(list, settings);
        }
        finally { CatalogPlatformIndex.Flush(); _refreshGate.Release(); }
    }
}
