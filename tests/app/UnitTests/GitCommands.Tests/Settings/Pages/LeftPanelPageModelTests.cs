using GitCommands;
using GitCommands.LeftPanel;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class LeftPanelPageModelTests : SettingsPageModelTestBase
{
    protected override (string Name, Func<object?> Get, Action<object?> Set)[] Storage { get; } =
    [
        ("RepoObjectsTree.ShowBranches", () => AppSettings.RepoObjectsTreeShowBranches, value => AppSettings.RepoObjectsTreeShowBranches = (bool)value!),
        ("RepoObjectsTree.ShowRemotes", () => AppSettings.RepoObjectsTreeShowRemotes, value => AppSettings.RepoObjectsTreeShowRemotes = (bool)value!),
        ("RepoObjectsTree.ShowTags", () => AppSettings.RepoObjectsTreeShowTags, value => AppSettings.RepoObjectsTreeShowTags = (bool)value!),
        ("RepoObjectsTree.ShowStashes", () => AppSettings.RepoObjectsTreeShowStashes, value => AppSettings.RepoObjectsTreeShowStashes = (bool)value!),
        ("RepoObjectsTree.ShowWorktrees", () => AppSettings.RepoObjectsTreeShowWorktrees, value => AppSettings.RepoObjectsTreeShowWorktrees = (bool)value!),
        ("RepoObjectsTree.ShowSubmodules", () => AppSettings.RepoObjectsTreeShowSubmodules, value => AppSettings.RepoObjectsTreeShowSubmodules = (bool)value!),
        ("LeftPanelSectionOrder", () => AppSettings.LeftPanelSectionOrder, value => AppSettings.LeftPanelSectionOrder = (string)value!),
    ];

    protected override SettingsPageModel CreateModel() => new LeftPanelPageModel();

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        LeftPanelPageModel model = new();

        model.Title.Should().Be("Left panel");
        model.Groups.Select(group => group.Caption).Should().Equal("Sections", "Order");
        model.Entries.Should().HaveCount(Storage.Length);
    }

    [Test]
    public void Visible_sections_honor_order_and_visibility()
    {
        string savedOrder = AppSettings.LeftPanelSectionOrder;
        bool savedTags = AppSettings.RepoObjectsTreeShowTags;
        try
        {
            AppSettings.LeftPanelSectionOrder = "Stashes, Branches";
            AppSettings.RepoObjectsTreeShowTags = false;

            LeftPanelPageModel.VisibleSectionsInOrder().Should().Equal(
                LeftPanelSection.Stashes,
                LeftPanelSection.Branches,
                LeftPanelSection.Remotes,
                LeftPanelSection.Worktrees,
                LeftPanelSection.Submodules);
        }
        finally
        {
            AppSettings.LeftPanelSectionOrder = savedOrder;
            AppSettings.RepoObjectsTreeShowTags = savedTags;
        }
    }
}
