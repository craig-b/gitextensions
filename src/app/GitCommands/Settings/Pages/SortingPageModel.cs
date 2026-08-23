using GitCommands.Utils;
using GitUIPluginInterfaces;
using ResourceManager;

namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the sorting settings page. The choice indices are the enum
///  values (the enums are 0-based and sequential, as the WinForms combos assume), and
///  saving reinitializes the cached translated strings that embed sort captions.
/// </summary>
public sealed class SortingPageModel : SettingsPageModel
{
    public SortingPageModel()
        : base("Sorting")
    {
        Groups =
        [
            new SettingsGroup("Sorting",
                RevisionsSortBy = new ChoiceSettingsEntry("Sort revisions by", DescriptionsOf<RevisionSortOrder>(),
                    () => (int)AppSettings.RevisionSortOrder.Value,
                    index =>
                    {
                        AppSettings.RevisionSortOrder.Value = (RevisionSortOrder)index;
                        AppSettings.RevisionSortOrder.Save();
                    }),
                BranchesSortBy = new ChoiceSettingsEntry("Sort branches by", DescriptionsOf<GitRefsSortBy>(),
                    () => (int)AppSettings.RefsSortBy, index => AppSettings.RefsSortBy = (GitRefsSortBy)index),
                BranchesOrder = new ChoiceSettingsEntry("Order branches", DescriptionsOf<GitRefsSortOrder>(),
                    () => (int)AppSettings.RefsSortOrder, index => AppSettings.RefsSortOrder = (GitRefsSortOrder)index),
                PrioritizedBranchNames = new StringSettingsEntry("Prioritized branches",
                    () => AppSettings.PrioritizedBranchNames, value => AppSettings.PrioritizedBranchNames = value),
                PrioritizedRemoteNames = new StringSettingsEntry("Prioritized remotes",
                    () => AppSettings.PrioritizedRemoteNames, value => AppSettings.PrioritizedRemoteNames = value)),
        ];
    }

    public ChoiceSettingsEntry RevisionsSortBy { get; }
    public ChoiceSettingsEntry BranchesSortBy { get; }
    public ChoiceSettingsEntry BranchesOrder { get; }
    public StringSettingsEntry PrioritizedBranchNames { get; }
    public StringSettingsEntry PrioritizedRemoteNames { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }

    public override void Save()
    {
        base.Save();

        // sort captions are embedded in cached translated strings
        TranslatedStrings.Reinitialize();
    }

    private static string[] DescriptionsOf<T>() where T : Enum
        => [.. EnumHelper.GetValues<T>().Select(value => value.GetDescription())];
}
