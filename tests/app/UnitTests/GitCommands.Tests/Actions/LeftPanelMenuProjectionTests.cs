using GitCommands.Actions;

namespace GitCommandsTests.Actions;

public sealed class LeftPanelMenuProjectionTests
{
    private static IReadOnlyList<string> Ids(IReadOnlyList<ActionDescriptor> actions)
        => [.. actions.Select(action => action.Id)];

    [Test]
    public void Remote_repo_menu_swaps_state_group_structurally()
    {
        Ids(LeftPanelMenuRegistry.RemoteRepoMenuFor(new LeftPanelRemoteContext(RemoteEnabled: true, HasHttpUrl: true)))
            .Should().Equal("remote.fetch", "remote.fetchPrune", "remote.deactivate", "remote.openUrl", "remote.manage");

        Ids(LeftPanelMenuRegistry.RemoteRepoMenuFor(new LeftPanelRemoteContext(RemoteEnabled: false, HasHttpUrl: false)))
            .Should().Equal("remote.activate", "remote.activateFetch", "remote.manage");
    }

    [Test]
    public void Submodule_menu_is_structural_for_current_and_bare()
    {
        Ids(LeftPanelMenuRegistry.SubmoduleMenuFor(new LeftPanelSubmoduleContext()))
            .Should().Equal("submodule.switchTo", "submodule.open", "submodule.update", "submodule.reset", "submodule.stash", "submodule.commit");

        Ids(LeftPanelMenuRegistry.SubmoduleMenuFor(new LeftPanelSubmoduleContext(IsCurrent: true)))
            .Should().NotContain("submodule.switchTo");

        Ids(LeftPanelMenuRegistry.SubmoduleMenuFor(new LeftPanelSubmoduleContext(IsBareRepository: true)))
            .Should().Equal("submodule.switchTo", "submodule.open", "submodule.update");
    }

    [Test]
    public void Worktree_rules_follow_the_winforms_gray_spots()
    {
        ActionDescriptor Get(string id) => LeftPanelMenuRegistry.WorktreeNodeActions.Single(action => action.Id == id);

        LeftPanelWorktreeContext plain = new();
        LeftPanelMenuRegistry.IsApplicable(Get("worktree.open"), plain).Should().BeTrue();
        LeftPanelMenuRegistry.IsApplicable(Get("worktree.open"), plain with { IsCurrent = true }).Should().BeFalse();
        LeftPanelMenuRegistry.IsApplicable(Get("worktree.delete"), plain with { IsDeleted = true }).Should().BeFalse();
        LeftPanelMenuRegistry.IsApplicable(Get("worktree.showInFolder"), plain with { DirectoryExists = false }).Should().BeFalse();

        // Copy path stays live even for the current or deleted worktree.
        LeftPanelMenuRegistry.IsApplicable(Get("worktree.copyPath"), plain with { IsCurrent = true, IsDeleted = true }).Should().BeTrue();
    }

    [Test]
    public void Range_menu_gates_compare_on_exactly_two()
    {
        ActionDescriptor compare = LeftPanelMenuRegistry.RefRangeActions.Single(action => action.Id == "refs.compareSelected");
        LeftPanelMenuRegistry.IsApplicable(compare, new LeftPanelRangeContext(SelectedRefCount: 2)).Should().BeTrue();
        LeftPanelMenuRegistry.IsApplicable(compare, new LeftPanelRangeContext(SelectedRefCount: 3)).Should().BeFalse();

        ActionDescriptor operate = LeftPanelMenuRegistry.RefRangeActions.Single(action => action.Id == "refs.operateOn");
        LeftPanelMenuRegistry.IsApplicable(operate, new LeftPanelRangeContext(SelectedRefCount: 5)).Should().BeTrue();
    }

    [Test]
    public void Stash_verbs_vanish_in_bare_repositories()
    {
        ActionDescriptor apply = LeftPanelMenuRegistry.StashNodeActions.Single(action => action.Id == "stash.apply");
        LeftPanelMenuRegistry.IsApplicable(apply, new LeftPanelStashContext()).Should().BeTrue();
        LeftPanelMenuRegistry.IsApplicable(apply, new LeftPanelStashContext(IsBareRepository: true)).Should().BeFalse();
    }

    [Test]
    public void Ref_menu_additions_are_remote_branch_only_and_panel_aware()
    {
        ActionDescriptor Get(string id) => GridMenuRegistry.RefActions.Single(action => action.Id == id);

        RefMenuContext remoteBranch = new(RefMenuKind.RemoteBranch, FromLeftPanel: true);
        RefMenuContext localBranch = new(RefMenuKind.LocalBranch, FromLeftPanel: true);

        foreach (string id in new[] { "ref.fetch", "ref.fetchCheckout", "ref.fetchMerge", "ref.fetchRebase", "ref.fetchCreateBranch" })
        {
            GridMenuRegistry.IsApplicable(Get(id), remoteBranch).Should().BeTrue();
            GridMenuRegistry.IsApplicable(Get(id), localBranch).Should().BeFalse();
        }

        // "Select in left panel" is pointless when the menu already opened from the left panel.
        GridMenuRegistry.IsApplicable(Get("ref.selectInLeftPanel"), localBranch).Should().BeFalse();
        GridMenuRegistry.IsApplicable(Get("ref.selectInLeftPanel"), new RefMenuContext(RefMenuKind.LocalBranch)).Should().BeTrue();

        // The combos render as one named submenu, keeping them out of the flat menu body.
        GridMenuRegistry.RefSubmenuGroups.Should().ContainKey("fetch-combo");
    }

    [Test]
    public void Reviewed_evictions_are_not_in_the_registry()
    {
        // Sort-by/sort-order (a setting), expand/collapse (tree affordance), move up/down
        // (the Left panel settings page), and the commit-field copy submenu were evicted in
        // review - they must not reappear under new ids.
        IEnumerable<string> captions =
        [
            .. LeftPanelMenuRegistry.RemoteRepoActions.Select(action => action.Caption),
            .. LeftPanelMenuRegistry.RemotesSectionActions.Select(action => action.Caption),
            .. LeftPanelMenuRegistry.StashNodeActions.Select(action => action.Caption),
            .. LeftPanelMenuRegistry.StashesSectionActions.Select(action => action.Caption),
            .. LeftPanelMenuRegistry.SubmoduleNodeActions.Select(action => action.Caption),
            .. LeftPanelMenuRegistry.SubmodulesSectionActions.Select(action => action.Caption),
            .. LeftPanelMenuRegistry.WorktreeNodeActions.Select(action => action.Caption),
            .. LeftPanelMenuRegistry.WorktreesSectionActions.Select(action => action.Caption),
            .. LeftPanelMenuRegistry.BranchFolderActions.Select(action => action.Caption),
            .. LeftPanelMenuRegistry.RefRangeActions.Select(action => action.Caption),
        ];

        captions.Select(caption => caption.ToLowerInvariant()).Should().NotContain(caption =>
            caption.Contains("sort") || caption.Contains("expand") || caption.Contains("collapse")
            || caption.Contains("move up") || caption.Contains("move down")
            || caption.Contains("copy message") || caption.Contains("copy author"));
    }

    [Test]
    public void Simple_mode_node_menus_stay_small()
    {
        MenuProjector.Project(LeftPanelMenuRegistry.WorktreeNodeActions, MenuProfile.Simple, _ => true)
            .SelectMany(group => group).Should().HaveCount(3);

        MenuProjector.Project(LeftPanelMenuRegistry.StashNodeActions, MenuProfile.Simple, _ => true)
            .SelectMany(group => group).Should().HaveCount(4);

        MenuProjector.Project(LeftPanelMenuRegistry.SubmodulesSectionActions, MenuProfile.Simple, _ => true)
            .SelectMany(group => group).Should().HaveCount(2);
    }
}
