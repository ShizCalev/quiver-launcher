using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class PlatformAvailabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-platform-history-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new();
    private readonly PlatformAvailabilityService _service;
    public PlatformAvailabilityTests() { _service = new(_root); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private static GameInfo App() => new() { Name = "Game", FolderName = "Game", Repository = "availability/" + Guid.NewGuid().ToString("N") };
    private void Set(GameInfo app, string tag, params string[] assets) => CatalogPlatformIndex.Set("github", app.Repository, null,
        app.GetReleaseApiToken(_settings), new() { tag_name = tag, assets = assets.Select(name => new GitHubAsset { name = name }).ToArray() });

    [Fact]
    public void Latest_availability_ignores_pin_and_keeps_unknown_separate()
    {
        var app = App(); app.PreferredVersion = "v1";
        CatalogPlatformIndex.Set("github", app.Repository, "v1", null, new() { tag_name = "v1", assets = [new() { name = "game-Windows.zip" }] });
        _service.GetAvailability(app, _settings, "Linux").Should().Be(PlatformAvailability.Unknown);
        Set(app, "v2", "game-Linux.AppImage");
        _service.GetAvailability(app, _settings, "Linux").Should().Be(PlatformAvailability.BuildAvailable);
        _service.GetAvailability(app, _settings, "Windows").Should().Be(PlatformAvailability.NoBuildFound);
        app.PreferredVersion.Should().Be("v1");
        app.Tags.Should().BeEmpty();
        app.ReleaseAssetFilter = "nonexistent";
        _service.GetAvailability(app, _settings, "Linux").Should().Be(PlatformAvailability.Unknown);
        _service.GetAvailability(new GameInfo { Name = "Manual" }, _settings, "Linux").Should().Be(PlatformAvailability.Unknown);
    }

    [Fact]
    public void History_baselines_deduplicates_persists_and_reviews_without_reannouncing()
    {
        var app = App();
        Set(app, "v1", "game-Windows.zip"); _service.Observe([app], _settings);
        _service.Pending(app, _settings).Should().BeEmpty();
        Set(app, "v2", "game-Windows.zip", "game-Linux.AppImage"); _service.Observe([app, app], _settings);
        _service.Observe([app], _settings);
        var addition = _service.Pending(app, _settings).Should().ContainSingle().Subject;
        addition.Platform.Should().Be("Linux"); addition.ReleaseTag.Should().Be("v2");
        var restarted = new PlatformAvailabilityService(_root);
        restarted.Pending(app, _settings).Should().Equal(addition);
        restarted.MarkReviewed([app], _settings);
        var reviewed = new PlatformAvailabilityService(_root);
        reviewed.Observe([app], _settings);
        reviewed.Pending(app, _settings).Should().BeEmpty();
        Set(app, "v3", "game-Windows.zip", "game-Linux.AppImage"); reviewed.Observe([app], _settings);
        reviewed.Pending(app, _settings).Should().BeEmpty();
    }

    [Fact]
    public void Same_release_asset_additions_count_but_lost_support_and_unknown_checks_do_not()
    {
        var app = App();
        Set(app, "v1", "game-Windows.zip"); _service.Observe([app], _settings);
        Set(app, "v1", "game-Windows.zip", "game-Linux.AppImage"); _service.Observe([app], _settings);
        _service.Pending(app, _settings).Should().ContainSingle();
        Set(app, "v2", "mystery.bin"); _service.Observe([app], _settings);
        _service.Pending(app, _settings).Should().ContainSingle();
        _service.GetAvailability(app, _settings, "Linux").Should().Be(PlatformAvailability.BuildAvailable);
        Set(app, "v3", "game-Windows.zip"); _service.Observe([app], _settings);
        _service.Pending(app, _settings).Should().BeEmpty();
    }

    [Fact]
    public void New_asset_and_credential_contexts_establish_baselines_without_leaking_tokens()
    {
        var app = App();
        Set(app, "v1", "game-Windows.zip"); _service.Observe([app], _settings);
        app.ReleaseAssetFilter = "game";
        Set(app, "v2", "game-Windows.zip", "game-Linux.AppImage"); _service.Observe([app], _settings);
        _service.Pending(app, _settings).Should().BeEmpty();
        _settings.GitHubApiToken = "private-test-token-never-persist";
        Set(app, "v3", "game-Windows.zip", "game-Linux.AppImage", "game-Android.apk"); _service.Observe([app], _settings);
        _service.Pending(app, _settings).Should().BeEmpty();
        File.ReadAllText(Path.Combine(_root, "platform-availability-history-v1.json")).Should().NotContain(_settings.GitHubApiToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Failed_refresh_preserves_verified_state(HttpStatusCode status)
    {
        var app = App(); Set(app, "v1", "game-Windows.zip"); _service.Observe([app], _settings);
        using var client = new HttpClient(new Handler(_ => new(status) { Content = new StringContent("{}") }));
        await _service.RefreshAsync(client, [app], _settings, TestContext.Current.CancellationToken, force: true);
        _service.GetAvailability(app, _settings, "Linux").Should().Be(PlatformAvailability.NoBuildFound);
        _service.Pending(app, _settings).Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_fetches_latest_for_pinned_app_and_reuses_fresh_metadata()
    {
        var app = App(); app.PreferredVersion = "v1";
        var calls = new List<string>();
        using var client = new HttpClient(new Handler(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"tag_name":"v2","assets":[{"name":"game-Linux.AppImage","browser_download_url":"https://example.com/game-Linux.AppImage"}]}""") };
        }));
        await _service.RefreshAsync(client, [app, app], _settings, TestContext.Current.CancellationToken);
        await _service.RefreshAsync(client, [app], _settings, TestContext.Current.CancellationToken);
        calls.Should().ContainSingle().Which.Should().EndWith("/latest");
        _service.GetAvailability(app, _settings, "Linux").Should().Be(PlatformAvailability.BuildAvailable);
        app.PreferredVersion.Should().Be("v1");
    }

    [Fact]
    public void Obsolete_evidence_is_unknown_and_policy_changes_rebaseline()
    {
        var app = App(); Set(app, "v1", "game-Windows.zip"); _service.Observe([app], _settings);
        var cache = Path.Combine(_root, "metadata"); Directory.CreateDirectory(cache);
        void Load(int revision, DateTimeOffset validated)
        {
            var entries = new Dictionary<string, CatalogPlatformEntry>
            {
                [CatalogPlatformIndex.Key("github", app.Repository!)] = new("v2", ["game-Linux.AppImage"], validated, revision)
            };
            File.WriteAllText(Path.Combine(cache, "catalog_platform_index_v1.json"), JsonSerializer.Serialize(entries));
            CatalogPlatformIndex.Initialize(cache);
        }
        Load(1, DateTimeOffset.UtcNow);
        new PlatformAvailabilityService(Path.Combine(_root, "empty")).GetAvailability(app, _settings, "Linux").Should().Be(PlatformAvailability.Unknown);
        _service.Observe([app], _settings); _service.Pending(app, _settings).Should().BeEmpty();
        Load(3, DateTimeOffset.UtcNow.AddMinutes(1));
        _service.Observe([app], _settings); _service.Pending(app, _settings).Should().BeEmpty();
        _service.GetAvailability(app, _settings, "Linux").Should().Be(PlatformAvailability.BuildAvailable);
        // An older response cannot replace the newer baseline or produce an addition.
        Set(app, "old", "game-Windows.zip"); _service.Observe([app], _settings);
        _service.GetAvailability(app, _settings, "Linux").Should().Be(PlatformAvailability.BuildAvailable);
        _service.Pending(app, _settings).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_keeps_completed_observations_durable()
    {
        var first = App(); var second = App();
        Set(first, "v1", "game-Windows.zip"); _service.Observe([first], _settings);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains(second.Repository!))
            { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"tag_name":"v2","assets":[{"name":"game-Linux.AppImage","browser_download_url":"https://example.com/game-Linux.AppImage"}]}""") };
        }));
        Func<Task> refresh = () => _service.RefreshAsync(client, [first, second], _settings, cancellation.Token, force: true);
        await refresh.Should().ThrowAsync<OperationCanceledException>();
        new PlatformAvailabilityService(_root).Pending(first, _settings).Should().ContainSingle().Which.Platform.Should().Be("Linux");
    }

    [Fact]
    public void Catalog_view_respects_OS_filter_hidden_and_reviews_only_visible_changes()
    {
        var first = App(); var second = App(); second.Name = "Other";
        var source = new AppCatalogSource { CachedListVersion = "2", AcknowledgedListVersion = "1" };
        var model = new CatalogSyncViewModel { Availability = _service, SettingsModel = new(new Store(_settings)), PlatformFilters = ["Windows"] };
        foreach (var app in new[] { first, second }) Set(app, "v1", "game-Mac.zip");
        _service.Observe([first, second], _settings);
        foreach (var app in new[] { first, second }) Set(app, "v2", "game-Mac.zip", "game-Linux.AppImage");
        _service.Observe([first, second], _settings);
        model.Refresh(source, [first], [first, second]);
        model.ReviewFilter = CatalogReviewFilter.NewPlatformSupport;
        model.EffectivePlatformFilters.Should().Equal("Windows");
        model.GetFilteredRows().Should().BeEmpty();
        model.NewPlatformSupportCount.Should().Be(0);
        model.PlatformFilters = ["Linux"];
        model.GetFilteredRows().Should().HaveCount(2);
        model.NewPlatformSupportCount.Should().Be(2);
        CatalogCompareService.HideFromReview(source, model.AllRows.Single(r => r.External == second).ReviewKey);
        model.GetFilteredRows().Should().ContainSingle();
        model.RefreshNewPlatformSupport();
        model.AllRows.Single(r => r.External == first).NewPlatformSupportText.Should().Be("Recently available for Linux");
        model.MarkPlatformSupportReviewed(model.GetFilteredRows().ToArray());
        _service.Pending(first, _settings).Should().BeEmpty();
        _service.Pending(second, _settings).Should().ContainSingle();
        source.AcknowledgedListVersion.Should().Be("1");
        model.ShowNewPlatformSupportFilter.Should().BeTrue();
        model.ReviewFilter = CatalogReviewFilter.All;
        model.EffectivePlatformFilters.Should().Equal("Linux");
        model.ShowNewPlatformSupportFilter.Should().BeFalse();
    }

    [Fact]
    public async Task Platform_rules_combine_with_tags_and_refresh_without_collection_reset()
    {
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        QuiverLauncherPaths.OverrideUserDataRoot = _root;
        try
        {
            var filter = new TagDisplayFilter { Platform = "Linux", Availability = PlatformAvailability.NoBuildFound, Tags = ["favorite"] };
            _settings.TagDisplayFilters.Add(filter); _settings.ActiveTagDisplayFilterId = filter.Id;
            var app = App(); app.Tags = ["favorite"];
            var other = App(); other.Tags = ["unrelated"];
            using var manager = new GameManager(new Store(_settings));
            Set(app, "v1", "game-Windows.zip"); Set(other, "v1", "game-Windows.zip");
            await manager.CatalogService.SaveLocalAppsAsync([app, other]);
            await manager.ReloadLibraryFromDiskAsync(allowNetwork: false);
            manager.Games.Should().ContainSingle().Which.Repository.Should().Be(app.Repository);
            var changes = new List<NotifyCollectionChangedAction>();
            manager.Games.CollectionChanged += (_, e) => changes.Add(e.Action);
            Set(app, "v2", "game-Linux.AppImage");
            manager.RefreshPlatformFilteredGames();
            manager.Games.Should().BeEmpty();
            changes.Should().NotContain(NotifyCollectionChangedAction.Reset);
        }
        finally { QuiverLauncherPaths.OverrideUserDataRoot = previousRoot; }
    }

    [Fact]
    public void Reviewing_android_leaves_other_platform_notices_pending()
    {
        var app = App(); Set(app, "v1", "game-Mac.zip"); _service.Observe([app], _settings);
        Set(app, "v2", "game-Mac.zip", "game-Linux.AppImage", "game-Android.apk"); _service.Observe([app], _settings);
        var model = new CatalogSyncViewModel { Availability = _service, SettingsModel = new(new Store(_settings)), PlatformFilters = ["Android"] };
        model.Refresh(new(), [], [app]); model.ReviewFilter = CatalogReviewFilter.NewPlatformSupport;
        model.RefreshNewPlatformSupport();
        model.AllRows[0].NewPlatformSupportText.Should().Be("Recently available for Android");
        model.MarkPlatformSupportReviewed(model.GetFilteredRows().ToArray());
        model.NewPlatformSupportCount.Should().Be(0);
        _service.Pending(app, _settings).Should().ContainSingle().Which.Platform.Should().Be("Linux");
        model.PlatformFilters = [];
        model.NewPlatformSupportCount.Should().Be(1);
        model.MarkPlatformSupportReviewed(model.GetFilteredRows().ToArray());
        _service.Pending(app, _settings).Should().BeEmpty();
    }

    private sealed class Store(AppSettings settings) : ISettingsStore
    {
        public AppSettings Current => settings;
        public AppSettings Load() => settings;
        public void Save(AppSettings value) { }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
}
