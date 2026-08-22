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
        Groups =
        [
            new SettingsGroup("Commit menu", [.. GridMenuRegistry.CommitActions.Select(Entry)]),
            new SettingsGroup("Range menu (multi-selection)", [.. GridMenuRegistry.RangeActions.Select(Entry)]),
            new SettingsGroup("Ref menu", [.. GridMenuRegistry.RefActions.Select(Entry)]),
            new SettingsGroup("File menu", [.. FileMenuRegistry.FileActions.Select(Entry)]),
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
