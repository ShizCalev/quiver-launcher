using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Services;

/// <summary>Library membership across the entire catalog, independent of browsing filters.</summary>
public sealed record CatalogPlatformSummary(string Platform, bool AllPlatforms, int Total, int InLibrary,
    int Available, int AvailableInLibrary, int Unavailable, int Unverified)
{
    public string Membership => AllPlatforms
        ? $"{InLibrary} of {Total} {(Total == 1 ? "app" : "apps")} in your library (all platforms)"
        : Available == 0 && Unverified > 0 ? $"{Platform} availability unverified · {InLibrary} apps in your library"
        : Available == 0 ? $"No {Platform} apps available"
        : $"{AvailableInLibrary} of {Available} {Platform} {(Available == 1 ? "app" : "apps")} in your library";
    public bool Complete => Available > 0 && AvailableInLibrary == Available && Unverified == 0;
    public int Excluded => Unavailable + Unverified;
    public string Breakdown => $"{Total} {(Total == 1 ? "app" : "apps")} across all platforms" +
        (Unavailable > 0 ? $" · {Unavailable} unavailable on {Platform}" : "") +
        (Unverified > 0 ? $" · {Unverified} awaiting compatibility checks" : "");

    public static CatalogPlatformSummary Create(IReadOnlyList<CatalogSyncRowItem> rows,
        IReadOnlyList<string> platforms, AppSettings? settings = null)
    {
        var all = CatalogPlatformSupport.IsAll(platforms);
        var label = all ? "all platforms" : string.Join(" / ", platforms.Select(p => p == "Mac" ? "macOS" : p));
        var available = 0; var inLibrary = 0; var unavailable = 0; var unverified = 0;
        foreach (var row in rows)
        {
            var app = row.External ?? row.Local;
            if (app == null || !CatalogPlatformIndex.TryGet(app.EffectiveRepositorySource, app.Repository,
                    app.PreferredVersion, settings == null ? app.GetReleaseApiToken() : app.GetReleaseApiToken(settings), out var metadata))
            { unverified++; continue; }
            if (all || CatalogPlatformSupport.Matches(CatalogPlatformSupport.FromMetadata(metadata!, app.ReleaseAssetFilter),
                    CatalogPlatformSupport.ParseFilters(platforms)))
            { available++; if (row.Local != null) inLibrary++; }
            else unavailable++;
        }
        return new(label, all, rows.Count, rows.Count(r => r.Local != null), available, inLibrary, unavailable, unverified);
    }
}
