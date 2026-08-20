using GitExtensions.Extensibility.Settings;

namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the advanced git-config page: tri-state toggles over boolean
///  git settings at whatever level the host's settings-source callback points to
///  (effective/local/global/system). Indeterminate shows an unrecognized or unset value
///  and saves as unset.
/// </summary>
public sealed class GitConfigAdvancedPageModel : SettingsPageModel
{
    private static readonly (string Key, string Caption)[] _gitSettings =
    [
        ("pull.rebase", "Rebase local branch when pulling (instead of merge)"),
        ("fetch.prune", "Prune remote branches during fetch"),
        ("merge.autostash", "Automatically stash before doing a merge"),
        ("rebase.autostash", "Automatically stash before doing a rebase"),
        ("rebase.autosquash", "Automatically squash commits when doing an interactive rebase"),
        ("rebase.updaterefs", "Rebase also dependent branches"),
        ("rerere.enabled", "Reuse recorded resolution of conflicted merges"),
        ("rerere.autoupdate", "Automatically apply recorded resolution of conflicted merges"),
    ];

    public GitConfigAdvancedPageModel(Func<SettingsSource> currentSettings)
        : base("Advanced")
    {
        SettingsEntry[] entries = [.. _gitSettings.Select(CreateEntry)];
        Groups = [new SettingsGroup("Advanced", entries)];

        TriStateSettingsEntry CreateEntry((string Key, string Caption) setting)
            => new($"{setting.Caption} [{setting.Key}]",
                () => currentSettings().GetValue(setting.Key) switch
                {
                    "true" or "yes" or "on" or "1" => true,
                    "false" or "no" or "off" or "0" or "" => false,
                    _ => null,
                },
                value => currentSettings().SetValue(setting.Key, value switch
                {
                    true => "true",
                    false => "false",
                    null => null,
                }));
    }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
