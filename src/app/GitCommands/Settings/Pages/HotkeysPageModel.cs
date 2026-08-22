using GitCommands.Actions;

namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the hotkeys settings page: one entry per registry action,
///  projected straight from <see cref="GridMenuRegistry"/>. Blank = the action's default,
///  "none" disables. Client-only, like the menus page.
/// </summary>
public sealed class HotkeysPageModel : SettingsPageModel
{
    public HotkeysPageModel()
        : base("Hotkeys")
    {
        // Left-panel actions whose ids are not already listed under another surface
        // (submodule verbs share ids with the file menu, the additions with the ref menu).
        IEnumerable<ActionDescriptor> leftPanelActions = new[]
            {
                LeftPanelMenuRegistry.RemoteRepoActions,
                LeftPanelMenuRegistry.RemotesSectionActions,
                LeftPanelMenuRegistry.StashNodeActions,
                LeftPanelMenuRegistry.StashesSectionActions,
                LeftPanelMenuRegistry.SubmoduleNodeActions,
                LeftPanelMenuRegistry.SubmodulesSectionActions,
                LeftPanelMenuRegistry.WorktreeNodeActions,
                LeftPanelMenuRegistry.WorktreesSectionActions,
                LeftPanelMenuRegistry.BranchFolderActions,
                LeftPanelMenuRegistry.RefRangeActions,
            }
            .SelectMany(actions => actions)
            .DistinctBy(action => action.Id)
            .Where(action => FileMenuRegistry.FileActions.All(other => other.Id != action.Id)
                && GridMenuRegistry.RefActions.All(other => other.Id != action.Id));

        Groups =
        [
            new SettingsGroup("Commit menu", [.. GridMenuRegistry.CommitActions.Select(Entry)]),
            new SettingsGroup("Range menu (multi-selection)", [.. GridMenuRegistry.RangeActions.Select(Entry)]),
            new SettingsGroup("Ref menu", [.. GridMenuRegistry.RefActions.Select(Entry)]),
            new SettingsGroup("File menu", [.. FileMenuRegistry.FileActions.Select(Entry)]),
            new SettingsGroup("Left panel", [.. leftPanelActions.Select(Entry)]),
        ];
    }

    public override IReadOnlyList<SettingsGroup> Groups { get; }

    private static SettingsEntry Entry(ActionDescriptor action)
    {
        string caption = action.Hotkey is null
            ? action.Caption.TrimEnd('.')
            : $"{action.Caption.TrimEnd('.')}  (default: {action.Hotkey})";
        string key = HotkeyResolution.SettingKey(action.Id);
        return new StringSettingsEntry(
            caption,
            () => AppSettings.GetString(key, "") ?? "",
            value => AppSettings.SetString(key, value.Trim()));
    }
}
