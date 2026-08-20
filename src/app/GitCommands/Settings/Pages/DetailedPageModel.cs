using GitExtensions.Extensibility.Settings;

namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the detailed settings page. The revision-graph toggles are
///  application-global; the push/merge-window settings live at the host-selected
///  distributed level (the WinForms page keeps its existing control bindings for those,
///  including the "no value set" placeholders — this model serves the client).
/// </summary>
public sealed class DetailedPageModel : SettingsPageModel
{
    public DetailedPageModel(Func<SettingsSource> currentSettings)
        : base("Detailed")
    {
        Groups =
        [
            new SettingsGroup("Revision graph",
                MergeGraphLanesHavingCommonParent = new BoolSettingsEntry("Merge graph lanes having common parent",
                    () => AppSettings.MergeGraphLanesHavingCommonParent.Value, value => AppSettings.MergeGraphLanesHavingCommonParent.Value = value),
                RenderGraphWithDiagonals = new BoolSettingsEntry("Render graph with diagonals",
                    () => AppSettings.RenderGraphWithDiagonals.Value, value => AppSettings.RenderGraphWithDiagonals.Value = value),
                StraightenGraphDiagonals = new BoolSettingsEntry("Straighten graph diagonals",
                    () => AppSettings.StraightenGraphDiagonals.Value, value => AppSettings.StraightenGraphDiagonals.Value = value)),

            new SettingsGroup("Push window",
                GetRemoteBranchesDirectlyFromRemote = new TriStateSettingsEntry("Get remote branches directly from the remote",
                    () => DetailedSettings.GetRemoteBranchesDirectlyFromRemote[currentSettings()],
                    value => DetailedSettings.GetRemoteBranchesDirectlyFromRemote[currentSettings()] = value)),

            new SettingsGroup("Merge window",
                AddMergeLogMessages = new TriStateSettingsEntry("Add log messages",
                    () => DetailedSettings.AddMergeLogMessages[currentSettings()],
                    value => DetailedSettings.AddMergeLogMessages[currentSettings()] = value),
                MergeLogMessagesCount = new NumberSettingsEntry("Number of log messages",
                    minimum: 0, maximum: 100, increment: 1,
                    () => DetailedSettings.MergeLogMessagesCount.ValueOrDefault(currentSettings()),
                    value => DetailedSettings.MergeLogMessagesCount[currentSettings()] = value)),
        ];
    }

    public BoolSettingsEntry MergeGraphLanesHavingCommonParent { get; }
    public BoolSettingsEntry RenderGraphWithDiagonals { get; }
    public BoolSettingsEntry StraightenGraphDiagonals { get; }

    public TriStateSettingsEntry GetRemoteBranchesDirectlyFromRemote { get; }

    public TriStateSettingsEntry AddMergeLogMessages { get; }
    public NumberSettingsEntry MergeLogMessagesCount { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
