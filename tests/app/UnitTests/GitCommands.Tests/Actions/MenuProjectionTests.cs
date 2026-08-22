using GitCommands.Actions;

namespace GitCommandsTests.Actions;

public sealed class MenuProjectionTests
{
    private static IReadOnlyList<string> Flatten(IReadOnlyList<IReadOnlyList<ProjectedMenuItem>> groups)
        => [.. groups.SelectMany(group => group.Select(item => item.Action.Id))];

    [Test]
    public void Simple_mode_shows_core_tier_only()
    {
        var groups = MenuProjector.Project(GridMenuRegistry.CommitActions, MenuProfile.Simple, _ => true);

        Flatten(groups).Should().Equal(
            "commit.createBranch", "commit.cherryPick", "commit.revert", "commit.openDifftool",
            "commit.mergeIntoCurrent", "commit.checkoutBranch",
            "copy.hash", "copy.message", "copy.author", "copy.date");
        groups.Should().HaveCount(3, because: "primaries, refs, and copy survive; empty groups vanish");
    }

    [Test]
    public void Normal_mode_keeps_declaration_order_and_groups()
    {
        var groups = MenuProjector.Project(GridMenuRegistry.CommitActions, MenuProfile.Normal, _ => true);

        groups.Select(group => group[0].Action.Group).Should().Equal(
            "primary", "refs", "compare", "copy", "history-rewrite", "integrations");
    }

    [Test]
    public void Gray_policy_keeps_inapplicable_items_disabled_hide_policy_drops_them()
    {
        GridCommitMenuContext bare = new(IsBareRepository: true, HasCurrentBranch: false);

        var grayed = MenuProjector.Project(GridMenuRegistry.CommitActions, MenuProfile.Normal,
            action => GridMenuRegistry.IsApplicable(action, bare));
        grayed.SelectMany(g => g).Should().Contain(item => item.Action.Id == "commit.createBranch" && !item.Enabled);

        MenuProfile hiding = MenuProfile.Normal with { InapplicablePolicy = InapplicableItemPolicy.Hide };
        var hidden = MenuProjector.Project(GridMenuRegistry.CommitActions, hiding,
            action => GridMenuRegistry.IsApplicable(action, bare));
        Flatten(hidden).Should().NotContain("commit.createBranch");
        Flatten(hidden).Should().Contain("copy.hash", because: "copying never needs a work tree");
    }

    [Test]
    public void Custom_profile_is_an_explicit_ordered_selection()
    {
        MenuProfile custom = new(MenuProfileMode.Custom, CustomOrder: ["copy.hash", "commit.cherryPick", "unknown.id"]);

        var groups = MenuProjector.Project(GridMenuRegistry.CommitActions, custom, _ => true);

        Flatten(groups).Should().Equal("copy.hash", "commit.cherryPick");
    }

    [Test]
    public void Contextual_rows_replace_the_commit_menu_keeping_copy_and_bisect_prepends()
    {
        IReadOnlyList<ActionDescriptor> artificial = GridMenuRegistry.CommitMenuFor(new GridCommitMenuContext(IsArtificial: true));
        artificial.Select(action => action.Id).Should().Equal(
            "artificial.commit", "artificial.resetChanges",
            "copy.hash", "copy.message", "copy.author", "copy.date", "copy.refNames");

        IReadOnlyList<ActionDescriptor> stash = GridMenuRegistry.CommitMenuFor(new GridCommitMenuContext(IsStash: true));
        stash[0].Id.Should().Be("stash.apply");
        stash.Select(action => action.Id).Should().Contain("copy.hash", because: "replacements keep the copy group");

        IReadOnlyList<ActionDescriptor> bisect = GridMenuRegistry.CommitMenuFor(new GridCommitMenuContext(InBisect: true));
        bisect[0].Id.Should().Be("bisect.good");
        bisect.Select(action => action.Id).Should().Contain("commit.createBranch");

        GridMenuRegistry.CommitMenuFor(new GridCommitMenuContext())
            .Should().BeSameAs(GridMenuRegistry.CommitActions);
    }

    [Test]
    public void Multi_row_selection_replaces_the_commit_menu_with_the_range_menu()
    {
        GridMenuRegistry.CommitMenuFor(new GridCommitMenuContext(SelectedCount: 2))
            .Should().BeSameAs(GridMenuRegistry.RangeActions);

        // The range menu wins over every other contextual state.
        GridMenuRegistry.CommitMenuFor(new GridCommitMenuContext(IsStash: true, InBisect: true, SelectedCount: 3))
            .Should().BeSameAs(GridMenuRegistry.RangeActions);

        ActionDescriptor cherryPick = GridMenuRegistry.RangeActions.Single(action => action.Id == "range.cherryPick");
        GridMenuRegistry.IsApplicable(cherryPick, new GridCommitMenuContext(SelectedCount: 2)).Should().BeTrue();
        GridMenuRegistry.IsApplicable(cherryPick, new GridCommitMenuContext(SelectedCount: 2, IsBareRepository: true)).Should().BeFalse();
    }

    [Test]
    public void Ref_menu_applicability_follows_kind_and_currency()
    {
        RefMenuContext currentLocal = new(RefMenuKind.LocalBranch, IsCurrent: true);
        RefMenuContext otherLocal = new(RefMenuKind.LocalBranch);
        RefMenuContext remote = new(RefMenuKind.RemoteBranch);
        RefMenuContext tag = new(RefMenuKind.Tag);

        ActionDescriptor Get(string id) => GridMenuRegistry.RefActions.Single(action => action.Id == id);

        GridMenuRegistry.IsApplicable(Get("ref.checkout"), otherLocal).Should().BeTrue();
        GridMenuRegistry.IsApplicable(Get("ref.checkout"), currentLocal).Should().BeFalse();
        GridMenuRegistry.IsApplicable(Get("ref.checkout"), tag).Should().BeFalse();
        GridMenuRegistry.IsApplicable(Get("ref.push"), otherLocal).Should().BeTrue();
        GridMenuRegistry.IsApplicable(Get("ref.push"), remote).Should().BeFalse();
        GridMenuRegistry.IsApplicable(Get("ref.pull"), remote).Should().BeTrue();
        GridMenuRegistry.IsApplicable(Get("ref.delete"), currentLocal).Should().BeFalse();
        GridMenuRegistry.IsApplicable(Get("ref.delete"), tag).Should().BeTrue();
        GridMenuRegistry.IsApplicable(Get("ref.copyName"), currentLocal).Should().BeTrue();
        GridMenuRegistry.IsApplicable(Get("ref.pushTag"), tag).Should().BeTrue();
        GridMenuRegistry.IsApplicable(Get("ref.pushTag"), otherLocal).Should().BeFalse();
        GridMenuRegistry.IsApplicable(Get("ref.createBranchFrom"), tag).Should().BeTrue();
        GridMenuRegistry.IsApplicable(Get("ref.createBranchFrom"), currentLocal).Should().BeTrue();
    }
}
