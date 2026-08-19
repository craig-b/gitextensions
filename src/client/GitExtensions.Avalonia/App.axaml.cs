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
            string repositoryPath = desktop.Args is [{ Length: > 0 } path, ..]
                ? path
                : Environment.CurrentDirectory;

            desktop.MainWindow = new MainWindow(repositoryPath);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
