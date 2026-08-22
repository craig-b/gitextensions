using GitCommands.LeftPanel;

namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the "Left panel" settings page: the six section visibility
///  toggles (shared with the WinForms panel's header buttons) and the section order that
///  replaced the context menu's Move Up/Down rows. Client-only, like the hotkeys page.
/// </summary>
public sealed class LeftPanelPageModel : SettingsPageModel
{
    public LeftPanelPageModel()
        : base("Left panel")
    {
        Groups =
        [
            new SettingsGroup("Sections",
            [
                new BoolSettingsEntry("Show branches", () => AppSettings.RepoObjectsTreeShowBranches, value => AppSettings.RepoObjectsTreeShowBranches = value),
                new BoolSettingsEntry("Show remotes", () => AppSettings.RepoObjectsTreeShowRemotes, value => AppSettings.RepoObjectsTreeShowRemotes = value),
                new BoolSettingsEntry("Show tags", () => AppSettings.RepoObjectsTreeShowTags, value => AppSettings.RepoObjectsTreeShowTags = value),
                new BoolSettingsEntry("Show stashes", () => AppSettings.RepoObjectsTreeShowStashes, value => AppSettings.RepoObjectsTreeShowStashes = value),
                new BoolSettingsEntry("Show worktrees", () => AppSettings.RepoObjectsTreeShowWorktrees, value => AppSettings.RepoObjectsTreeShowWorktrees = value),
                new BoolSettingsEntry("Show submodules", () => AppSettings.RepoObjectsTreeShowSubmodules, value => AppSettings.RepoObjectsTreeShowSubmodules = value),
            ]),
            new SettingsGroup("Order",
            [
                new StringSettingsEntry(
                    "Section order, top to bottom (comma-separated names; missing sections keep their default place)",
                    () => AppSettings.LeftPanelSectionOrder,
                    value => AppSettings.LeftPanelSectionOrder = value.Trim()),
            ]),
        ];
    }

    public override IReadOnlyList<SettingsGroup> Groups { get; }

    /// <summary>The ordered, visible sections the client sidebar should build.</summary>
    public static IReadOnlyList<LeftPanelSection> VisibleSectionsInOrder()
        => [.. LeftPanelSectionOrder.Parse(AppSettings.LeftPanelSectionOrder).Where(IsVisible)];

    private static bool IsVisible(LeftPanelSection section)
        => section switch
        {
            LeftPanelSection.Branches => AppSettings.RepoObjectsTreeShowBranches,
            LeftPanelSection.Remotes => AppSettings.RepoObjectsTreeShowRemotes,
            LeftPanelSection.Tags => AppSettings.RepoObjectsTreeShowTags,
            LeftPanelSection.Stashes => AppSettings.RepoObjectsTreeShowStashes,
            LeftPanelSection.Worktrees => AppSettings.RepoObjectsTreeShowWorktrees,
            _ => AppSettings.RepoObjectsTreeShowSubmodules,
        };
}
