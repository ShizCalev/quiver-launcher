using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogPlatformSummaryTests
{
    private static GameInfo App(string name, string? asset = null)
    {
        var app = new GameInfo { Name = name, Repository = $"summary-{Guid.NewGuid():N}/app", FolderName = name };
        if (asset != null) CatalogPlatformIndex.Set("github", app.Repository, null, null,
            new GitHubRelease { tag_name = "1", assets = [new() { name = asset }] });
        return app;
    }

    [Fact]
    public void Counts_separate_verified_unavailable_and_unknown_and_ignore_browsing_filters()
    {
        var windows = App("Windows", "game-Windows.zip");
        var linux = App("Linux", "game-Linux.AppImage");
        var unknown = App("Unknown");
        var source = new AppCatalogSource();
        var model = new CatalogSyncViewModel { PlatformFilters = ["Windows"] };
        model.Refresh(source, [windows], [windows, linux, unknown]);
        model.PlatformSummary.Membership.Should().Be("1 of 1 Windows app in your library");
        model.PlatformSummary.Total.Should().Be(3);
        model.PlatformSummary.Unavailable.Should().Be(1);
        model.PlatformSummary.Unverified.Should().Be(1);
        model.PlatformSummary.Complete.Should().BeFalse("unknown apps must not be marked unavailable or complete");
        var summary = model.PlatformSummary;
        model.SearchText = "does not exist";
        model.CycleTagChip("unmatched");
        model.ReviewFilter = CatalogReviewFilter.Hidden;
        model.PlatformSummary.Should().Be(summary);

        CatalogReviewEligibility.Reconcile(source, model.AllRows, devicePlatform: "Windows");
        var card = CatalogSourceListItem.FromSource(source);
        card.UsageStatsShort.Should().Be(summary.Membership);
        card.PlatformBreakdown.Should().Contain("1 unavailable on Windows").And.Contain("1 awaiting compatibility checks");
        card.HasPlatformExclusions.Should().BeTrue();
    }

    [Fact]
    public void Exclusions_reveal_unavailable_and_unknown_including_hidden_entries_without_allowing_bulk_add()
    {
        var windows = App("Windows", "game-Windows.zip");
        var linux = App("Linux", "game-Linux.AppImage");
        var unknown = App("Unknown");
        var source = new AppCatalogSource();
        var model = new CatalogSyncViewModel { PlatformFilters = ["Windows"] };
        model.Refresh(source, [], [windows, linux, unknown]);
        CatalogCompareService.HideFromReview(source, model.AllRows.Single(r => r.External == linux).ReviewKey);
        model.SearchText = "Windows";
        model.SetPlatformExclusions(true);
        model.RefreshCompatibilityLabels();
        model.GetFilteredRows().Select(r => r.External).Should().BeEquivalentTo([linux, unknown]);
        model.AllRows.Single(r => r.External == linux).CompatibilityText.Should().Contain("Windows");
        model.GetFilteredBulkAddRows().Should().BeEmpty();
        model.SetPlatformExclusions(false);
        model.GetFilteredRows().Select(r => r.External).Should().Contain(windows).And.NotContain(linux);
    }

    [Fact]
    public void All_platforms_and_no_metadata_have_explicit_labels()
    {
        var app = App("Unknown");
        var rows = CatalogCompareService.BuildCompareRows([app], [app]);
        CatalogPlatformSummary.Create(rows, []).Membership.Should().Be("1 of 1 app in your library (all platforms)");
        var summary = CatalogPlatformSummary.Create(rows, ["Windows"]);
        summary.Membership.Should().Contain("availability unverified");
        summary.Unavailable.Should().Be(0);
        summary.Complete.Should().BeFalse();
    }

    [Fact]
    public void Counts_respect_asset_filters_and_platform_changes()
    {
        var app = App("Only Windows", "game-Windows.zip");
        var rows = CatalogCompareService.BuildCompareRows([], [app]);
        CatalogPlatformSummary.Create(rows, ["Linux"]).Unavailable.Should().Be(1);
        CatalogPlatformSummary.Create(rows, ["Windows"]).Available.Should().Be(1);
        app.ReleaseAssetFilter = "nonexistent";
        CatalogPlatformSummary.Create(rows, ["Windows"]).Unavailable.Should().Be(1);
    }
}
