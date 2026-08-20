using GitCommands.Actions;

namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the menus settings page: the context-menu profile and the
///  inapplicable-item policy. Client-only for now - the WinForms menus are unchanged by design.
/// </summary>
public sealed class MenusPageModel : SettingsPageModel
{
    private static readonly MenuProfileMode[] _profileModes =
        [MenuProfileMode.Simple, MenuProfileMode.Normal, MenuProfileMode.Custom];

    private static readonly InapplicableItemPolicy[] _policies =
        [InapplicableItemPolicy.Gray, InapplicableItemPolicy.Hide];

    public MenusPageModel()
        : base("Menus")
    {
        Groups =
        [
            new SettingsGroup("Context menus",
                Profile = new ChoiceSettingsEntry("Menu profile",
                    ["Simple (core actions only)", "Normal (everything, grouped)", "Custom (your selection and order)"],
                    () => Array.IndexOf(_profileModes, AppSettings.MenuProfileMode),
                    index => AppSettings.MenuProfileMode = _profileModes[index]),
                InapplicableItems = new ChoiceSettingsEntry("Items that do not apply to the selection",
                    ["Show grayed (stable positions)", "Hide (shorter menus)"],
                    () => Array.IndexOf(_policies, AppSettings.MenuInapplicableItemPolicy),
                    index => AppSettings.MenuInapplicableItemPolicy = _policies[index])),
        ];
    }

    public ChoiceSettingsEntry Profile { get; }

    public ChoiceSettingsEntry InapplicableItems { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
