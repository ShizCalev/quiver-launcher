namespace QuiverLauncher.Services;

public interface IGameDownloadDialogs
{
    Task<bool> ConfirmWindowsInstallerAsync(string appName) =>
        throw new InvalidOperationException("MSI installation requires the Windows desktop interface. Open Quiver and run the installer there.");
    Task<string?> PickWindowsExecutableAsync(string appName, string? previousPath) =>
        throw new InvalidOperationException("Open Quiver's Windows desktop interface and select the installed executable.");
    Task<bool> ConfirmDownloadWithoutRunnerAsync();
    Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(
        string gamePath,
        LinuxWindowsRunnerConfig? existing = null,
        bool isInstall = true);
    Task ShowRateLimitExceededAsync();
    Task ShowGitLabRateLimitExceededAsync();
    Task ShowErrorAsync(string message, string title);
}

public sealed class AvaloniaGameDownloadDialogs : IGameDownloadDialogs
{
    public Task<bool> ConfirmWindowsInstallerAsync(string appName) => GameDialogService.ConfirmWindowsInstallerAsync(appName);
    public Task<string?> PickWindowsExecutableAsync(string appName, string? previousPath) => GameDialogService.PickWindowsExecutableAsync(appName, previousPath);
    public static AvaloniaGameDownloadDialogs Instance { get; } = new();

    public Task<bool> ConfirmDownloadWithoutRunnerAsync() =>
        GameDialogService.ShowWineNotFoundWarningAsync();

    public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(
        string gamePath,
        LinuxWindowsRunnerConfig? existing = null,
        bool isInstall = true) =>
        GameDialogService.ShowLinuxWindowsRunnerDialogAsync(gamePath, existing, isInstall);

    public Task ShowRateLimitExceededAsync() =>
        GameDialogService.ShowRateLimitErrorAsync();

    public Task ShowGitLabRateLimitExceededAsync() =>
        GameDialogService.ShowGitLabRateLimitErrorAsync();

    public Task ShowErrorAsync(string message, string title) =>
        GameDialogService.ShowMessageBoxAsync(message, title);
}

public sealed class HeadlessGameDownloadDialogs : IGameDownloadDialogs
{
    public static HeadlessGameDownloadDialogs Instance { get; } = new();

    public Task<bool> ConfirmDownloadWithoutRunnerAsync() => Task.FromResult(true);

    public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(
        string gamePath,
        LinuxWindowsRunnerConfig? existing = null,
        bool isInstall = true)
    {
        var kind = existing?.Kind ?? WindowsRunnerService.GetPreferredDefaultKind();
        return Task.FromResult<LinuxWindowsRunnerConfig?>(new LinuxWindowsRunnerConfig
        {
            Kind = kind,
            PrefixPath = existing?.PrefixPath ?? WindowsRunnerService.GetDefaultPrefixPathForKind(kind, gamePath),
            ProtonPath = existing?.ProtonPath ?? WindowsRunnerService.ListDetectedProtonInstallations().FirstOrDefault()?.ProtonExecutable,
            CustomLaunchCommand = existing?.CustomLaunchCommand,
        });
    }

    public Task ShowRateLimitExceededAsync() => Task.CompletedTask;

    public Task ShowGitLabRateLimitExceededAsync() => Task.CompletedTask;

    public Task ShowErrorAsync(string message, string title)
    {
        Console.Error.WriteLine($"{title}: {message}");
        return Task.CompletedTask;
    }
}
