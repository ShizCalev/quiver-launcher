using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class PlatformAvailabilityViewTests
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, EnableGamepadInput = true };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }

    [AvaloniaTheory]
    [InlineData(360, 480)]
    [InlineData(1000, 800)]
    public async Task Filter_editor_platform_controls_bind_and_scroll_into_view(int width, int height)
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-platform-editor-" + Guid.NewGuid().ToString("N"));
        var previous = QuiverLauncherPaths.OverrideUserDataRoot;
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        var store = new Store();
        var main = new MainView(new() { SettingsStore = store, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        await using var session = new LauncherSession();
        var view = new DisplayFilterEditorView();
        view.Configure(new(store), session, main, (_, _) => Task.CompletedTask, () => { });
        var window = new Window { Content = view, Width = width, Height = height };
        try
        {
            window.Show(); view.Open(null); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var platform = view.FindControl<ComboBox>("DisplayFilterPlatformComboBox")!;
            var availability = view.FindControl<ComboBox>("DisplayFilterAvailabilityComboBox")!;
            availability.IsVisible.Should().BeFalse();
            platform.SelectedIndex = 2;
            availability.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            view.Model.PlatformIndex.Should().Be(2); view.Model.AvailabilityIndex.Should().Be(1);
            view.Navigation.CollectControls().Should().Contain(platform).And.Contain(availability);
            var save = view.FindControl<Button>("SaveDisplayFilterButton")!;
            save.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Viewport.Height.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(height);
            if (height == 480) scroll.Offset.Y.Should().BeGreaterThan(0);
            var position = save.TranslatePoint(default, scroll)!.Value;
            position.Y.Should().BeGreaterThanOrEqualTo(0);
            (position.Y + save.Bounds.Height).Should().BeLessThanOrEqualTo(scroll.Bounds.Height + 1);
        }
        finally
        {
            window.Close(); await main.ShutdownAsync();
            QuiverLauncherPaths.OverrideUserDataRoot = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 480)]
    [InlineData(true, 1100)]
    public async Task New_support_view_preserves_selected_app_and_restores_platform_filter(bool gamepad, int width)
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-platform-view-" + Guid.NewGuid().ToString("N"));
        var previous = QuiverLauncherPaths.OverrideUserDataRoot;
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        var store = new Store();
        store.Current.CatalogPlatformFilters = ["Linux"]; store.Current.CatalogPlatformFilterChosen = true;
        var main = new MainView(new() { SettingsStore = store, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = main, Width = width, Height = 800 };
        try
        {
            window.Show(); main.Shell.Mode = MainViewMode.AppCatalog; main.Shell.CatalogSubView = AppCatalogSubView.Review;
            typeof(MainView).GetMethod("UpdateMainViewUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            var catalog = main.FindControl<CatalogReviewView>("CatalogReviewPanel")!;
            var history = catalog.Model.Availability!;
            var first = new GameInfo { Name = "B game", FolderName = "b", Repository = "view/" + Guid.NewGuid().ToString("N") };
            var second = new GameInfo { Name = "A game", FolderName = "a", Repository = "view/" + Guid.NewGuid().ToString("N") };
            void Set(GameInfo app, bool linux) => CatalogPlatformIndex.Set("github", app.Repository!, null, null,
                new() { tag_name = linux ? "v2" : "v1", assets = linux ? [new() { name = "game-Linux.AppImage" }] : [new() { name = "game-Mac.zip" }] });
            Set(first, false); Set(second, false); history.Observe([first, second], store.Current);
            Set(first, true); history.Observe([first], store.Current);
            catalog.Model.Refresh(new() { CachedListVersion = "1" }, [first], [first, second]);
            catalog.Model.PlatformFilters = ["Linux"];
            catalog.SetCatalogReviewUseGridView(true);
            catalog.CatalogReviewFilter_Click(catalog.FindControl<Button>("CatalogFilterNewPlatformSupportButton"), new RoutedEventArgs());
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            catalog.Model.Rows.Should().ContainSingle();
            catalog.FindControl<Button>("CatalogReviewPlatformButton")!.Content.Should().Be("Platform: Linux ▾");
            var selected = catalog.Model.Rows[0];
            var search = catalog.FindControl<TextBox>("CatalogSearchTextBox")!;
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            if (gamepad) catalog.Navigation.ApplyCatalogReviewRowSelection(0); else search.Focus();
            var scrolls = catalog.GetVisualDescendants().OfType<ScrollViewer>().Select(s => (s, s.Offset)).ToArray();
            Set(second, true); history.Observe([second], store.Current);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            catalog.Model.Rows.Should().HaveCount(2);
            if (gamepad) selected.IsGamepadFocused.Should().BeTrue(); else search.IsFocused.Should().BeTrue();
            foreach (var (scroll, offset) in scrolls) scroll.Offset.Should().Be(offset);
            var actions = catalog.Navigation.CollectCatalogReviewRowActionControls(selected).OfType<Button>().ToList();
            actions.Select(b => b.Content?.ToString()).Should().Equal("Details", "Remove", "Dismiss");
            var positions = actions.Select(b => b.TranslatePoint(default, window)!.Value.Y).ToArray();
            (positions.Max() - positions.Min()).Should().BeLessThan(1, "all actions belong to one horizontal row");
            if (gamepad)
            {
                catalog.Navigation.ApplyCatalogReviewRowActionSelection(0);
                catalog.Navigation.HandleCatalogReviewRowActionsNavigation(QuiverLauncher.Services.NavigationDirection.Right);
                catalog.Navigation.HandleCatalogReviewRowActionsNavigation(QuiverLauncher.Services.NavigationDirection.Right);
                actions[2].Classes.Should().Contain("gamepad-focused");
                selected.IsGamepadFocused.Should().BeTrue();
            }
            catalog.Navigation.CollectCatalogReviewFilterChipControls().Should().Contain(catalog.FindControl<Button>("CatalogFilterNewPlatformSupportButton")!);
            catalog.CatalogReviewFilter_Click(catalog.FindControl<Button>("CatalogFilterAllButton"), new RoutedEventArgs());
            catalog.Model.EffectivePlatformFilters.Should().Equal("Linux");
        }
        finally
        {
            window.Close(); await main.ShutdownAsync(); GamepadFocusChrome.SetKeyboardNavigationActive(false);
            QuiverLauncherPaths.OverrideUserDataRoot = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
