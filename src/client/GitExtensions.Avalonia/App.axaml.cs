using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace GitExtensions.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Verification hook: force a theme variant regardless of the system setting.
        RequestedThemeVariant = Environment.GetEnvironmentVariable("GE_SPIKE_THEME")?.ToLowerInvariant() switch
        {
            "dark" => global::Avalonia.Styling.ThemeVariant.Dark,
            "light" => global::Avalonia.Styling.ThemeVariant.Light,
            _ => RequestedThemeVariant,
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(ResolveStartupRepository(desktop.Args));
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    ///  An explicit argument wins; otherwise the WinForms startup rule via StartupWorkingDir:
    ///  the remembered repository when the setting asks for it (pruning it when it went stale),
    ///  falling back to the current directory.
    /// </summary>
    private static string ResolveStartupRepository(string[]? args)
    {
        if (args is [{ Length: > 0 } path, ..])
        {
            return path;
        }

        GitCommands.Open.StartupWorkingDir startup = GitCommands.Open.StartupWorkingDir.Resolve(
            GitCommands.AppSettings.StartWithRecentWorkingDir,
            GitCommands.AppSettings.RecentWorkingDir,
            GitCommands.GitModule.IsValidGitWorkingDir);

        if (startup.StalePathToPrune is string stalePath)
        {
            // The remembered repository no longer exists: forget it instead of re-checking it on every launch.
            GitCommands.AppSettings.RecentWorkingDir = string.Empty;
            GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.RemoveRecentAsync(stalePath).GetAwaiter().GetResult();
        }

        return startup.WorkingDir ?? Environment.CurrentDirectory;
    }
}
