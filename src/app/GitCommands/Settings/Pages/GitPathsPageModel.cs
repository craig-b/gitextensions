namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the git-paths settings page. The path validation/solve flow,
///  browse dialogs, and the HOME-changing dialog stay view-side;
///  <see cref="GetEffectiveConfigEnvironment"/> serves the environment summary.
/// </summary>
public sealed class GitPathsPageModel : SettingsPageModel
{
    public GitPathsPageModel()
        : base("Paths")
    {
        Groups =
        [
            new SettingsGroup("Paths",
                GitCommand = new StringSettingsEntry("Command used to run git (git.cmd or git.exe)",
                    () => AppSettings.GitCommandValue, value => AppSettings.GitCommandValue = value),
                LinuxToolsDir = new StringSettingsEntry("Path to linux tools (sh). Leave empty when it is in the path.",
                    () => AppSettings.LinuxToolsDir, value => AppSettings.LinuxToolsDir = value)),
        ];
    }

    public StringSettingsEntry GitCommand { get; }
    public StringSettingsEntry LinuxToolsDir { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }

    /// <summary>
    ///  The environment variable git's global config resolves through:
    ///  GIT_CONFIG_GLOBAL when set, otherwise HOME.
    /// </summary>
    public static (string Name, string? Value, bool ConfigEnvIsSet) GetEffectiveConfigEnvironment()
    {
        EnvironmentConfiguration.SetEnvironmentVariables();
        string? configValue = EnvironmentConfiguration.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        return configValue is not null
            ? ("GIT_CONFIG_GLOBAL", configValue, true)
            : ("HOME", EnvironmentConfiguration.GetHomeDir(), false);
    }
}
