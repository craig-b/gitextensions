namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the browse-repository-window settings page. The console tab
///  and default-shell terminal picker are ConEmu (Windows) chrome and stay view-side.
/// </summary>
public sealed class BrowseRepoPageModel : SettingsPageModel
{
    public BrowseRepoPageModel()
        : base("Browse repository window")
    {
        Groups =
        [
            new SettingsGroup("General",
                UseBrowseForFileHistory = new BoolSettingsEntry("Show file history in the main window",
                    () => AppSettings.UseBrowseForFileHistory.Value, value => AppSettings.UseBrowseForFileHistory.Value = value),
                UseDiffViewerForBlame = new BoolSettingsEntry("Show blame in diff viewer",
                    () => AppSettings.UseDiffViewerForBlame.Value, value => AppSettings.UseDiffViewerForBlame.Value = value),
                ShowFindInCommitFilesGitGrep = new BoolSettingsEntry("Show 'Find in commit files using git-grep'",
                    () => AppSettings.ShowFindInCommitFilesGitGrep.Value, value => AppSettings.ShowFindInCommitFilesGitGrep.Value = value),
                ShowRevisionGridTooltips = new BoolSettingsEntry("Show revision tooltips (restart required)",
                    () => AppSettings.ShowRevisionGridTooltips.Value, value => AppSettings.ShowRevisionGridTooltips.Value = value)),

            new SettingsGroup("Tabs (restart required)",
                ShowGpgInformation = new BoolSettingsEntry("Show GPG information",
                    () => AppSettings.ShowGpgInformation.Value, value => AppSettings.ShowGpgInformation.Value = value),
                ShowOutputHistoryAsTab = new BoolSettingsEntry("Show output history as tab (otherwise as panel)",
                    () => AppSettings.ShowOutputHistoryAsTab.Value, value => AppSettings.ShowOutputHistoryAsTab.Value = value),
                OutputHistoryDepth = new NumberSettingsEntry("Output history depth (0 to disable):",
                    minimum: 0, maximum: 1000, increment: 1,
                    () => Math.Clamp(AppSettings.OutputHistoryDepth.Value, 0, 1000),
                    value => AppSettings.OutputHistoryDepth.Value = value)),
        ];
    }

    public BoolSettingsEntry UseBrowseForFileHistory { get; }
    public BoolSettingsEntry UseDiffViewerForBlame { get; }
    public BoolSettingsEntry ShowFindInCommitFilesGitGrep { get; }
    public BoolSettingsEntry ShowRevisionGridTooltips { get; }

    public BoolSettingsEntry ShowGpgInformation { get; }
    public BoolSettingsEntry ShowOutputHistoryAsTab { get; }
    public NumberSettingsEntry OutputHistoryDepth { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }

    public override void Save()
    {
        // the panel-visibility default is re-derived only when the output-history
        // configuration actually changed (the running app may have toggled the panel)
        bool outputHistoryChanged =
            AppSettings.ShowOutputHistoryAsTab.Value != ShowOutputHistoryAsTab.Value
            || AppSettings.OutputHistoryDepth.Value != OutputHistoryDepth.Value;

        base.Save();

        if (outputHistoryChanged)
        {
            AppSettings.OutputHistoryPanelVisible.Value = !ShowOutputHistoryAsTab.Value && OutputHistoryDepth.Value > 0;
        }
    }
}
