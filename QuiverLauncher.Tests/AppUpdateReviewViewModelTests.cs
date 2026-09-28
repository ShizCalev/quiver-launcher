using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class AppUpdateReviewViewModelTests
{
    private sealed class Actions : IAppUpdateReviewActions
    {
        public bool IsReviewOpen { get; set; } = true;
        public int CheckCalls { get; private set; }
        public IReadOnlyList<GameInfo> GetReviewRows() => [];
        public IReadOnlyList<GameInfo> GetPendingUpdates() => [];
        public Task CheckForUpdatesAsync() { CheckCalls++; return Task.CompletedTask; }
        public Task UpdateAsync(GameInfo game, bool automaticSelection) => Task.CompletedTask;
        public Task SkipAsync(GameInfo game) => Task.CompletedTask;
        public void ShowVersions(GameInfo game) { }
        public void UpdatesChanged() { }
        public void ReturnToLibrary() => IsReviewOpen = false;
    }

    [AvaloniaFact]
    public void Empty_review_binds_header_and_disables_bulk_actions()
    {
        var view = new AppUpdateReviewView();
        view.Model.Configure(new Actions());
        view.Model.Refresh();
        Dispatcher.UIThread.RunJobs();
        view.FindControl<TextBlock>("AppUpdatesReviewHeaderText")!.Text.Should().Be("No app updates pending.");
        view.FindControl<Button>("AppUpdatesUpdateAllButton")!.IsEnabled.Should().BeFalse();
        view.FindControl<TextBlock>("AppUpdatesReviewEmptyText")!.IsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Bulk_skip_returns_to_library_and_resets_busy_state()
    {
        var actions = new Actions();
        var model = new AppUpdateReviewViewModel();
        model.Configure(actions);
        await model.SkipAllAsync();
        actions.IsReviewOpen.Should().BeFalse();
        model.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Recheck_is_explicit_and_does_not_require_leaving_the_review()
    {
        var actions = new Actions();
        var model = new AppUpdateReviewViewModel();
        model.Configure(actions);

        await model.CheckForUpdatesAsync();

        actions.CheckCalls.Should().Be(1);
        actions.IsReviewOpen.Should().BeTrue();
    }
}
