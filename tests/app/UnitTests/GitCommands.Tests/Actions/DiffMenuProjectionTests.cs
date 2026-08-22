using GitCommands.Actions;

namespace GitCommandsTests.Actions;

public sealed class DiffMenuProjectionTests
{
    private static IReadOnlyList<string> Ids(IReadOnlyList<ActionDescriptor> actions)
        => [.. actions.Select(action => action.Id)];

    [Test]
    public void Patch_group_swaps_structurally_on_the_line_target()
    {
        DiffMenuContext patching = new(IsPatchView: true, SupportsLinePatching: true);

        Ids(DiffMenuRegistry.DiffMenuFor(patching with { Target = DiffLineTarget.WorkTree }))
            .Should().StartWith(["diff.stageLines", "diff.resetLines"]);
        Ids(DiffMenuRegistry.DiffMenuFor(patching with { Target = DiffLineTarget.Index }))
            .Should().StartWith(["diff.unstageLines", "diff.resetLines"]);

        // Committed diffs get the honest captions - never the lying "Stage".
        IReadOnlyList<ActionDescriptor> committed = DiffMenuRegistry.DiffMenuFor(patching with { Target = DiffLineTarget.Committed });
        Ids(committed).Should().StartWith(["diff.applyLines", "diff.revertLines"]);
        Ids(committed).Should().NotContain("diff.stageLines");

        Ids(DiffMenuRegistry.DiffMenuFor(patching with { SupportsLinePatching = false }))
            .Should().NotContain(id => id.StartsWith("diff.stageLines") || id == "diff.applyLines");
    }

    [Test]
    public void Copy_variants_exist_only_in_patch_view_and_commit_row_only_in_commit_window()
    {
        Ids(DiffMenuRegistry.DiffMenuFor(new DiffMenuContext(IsPatchView: false)))
            .Should().Equal("diff.copy");

        Ids(DiffMenuRegistry.DiffMenuFor(new DiffMenuContext(IsPatchView: true)))
            .Should().Equal("diff.copy", "diff.copyPatch", "diff.copyNewVersion", "diff.copyOldVersion");

        Ids(DiffMenuRegistry.DiffMenuFor(new DiffMenuContext(IsPatchView: true, IsCommitWindow: true)))
            .Should().Contain("diff.addToCommitMessage");
    }

    [Test]
    public void Copy_transforms_require_a_selection_but_patch_verbs_work_off_the_caret()
    {
        ActionDescriptor Get(string id) => DiffMenuRegistry.DiffActions.Single(action => action.Id == id);

        DiffMenuContext noSelection = new(HasSelection: false);
        foreach (string id in new[] { "diff.copy", "diff.copyPatch", "diff.copyNewVersion", "diff.copyOldVersion", "diff.addToCommitMessage" })
        {
            DiffMenuRegistry.IsApplicable(Get(id), noSelection).Should().BeFalse();
            DiffMenuRegistry.IsApplicable(Get(id), noSelection with { HasSelection = true }).Should().BeTrue();
        }

        DiffMenuRegistry.IsApplicable(Get("diff.stageLines"), noSelection).Should().BeTrue();
    }

    [Test]
    public void Simple_mode_stays_small()
    {
        DiffMenuContext worktree = new(HasSelection: true, IsPatchView: true, SupportsLinePatching: true, Target: DiffLineTarget.WorkTree);
        MenuProjector.Project(DiffMenuRegistry.DiffMenuFor(worktree), MenuProfile.Simple, action => DiffMenuRegistry.IsApplicable(action, worktree))
            .SelectMany(group => group).Select(item => item.Action.Id)
            .Should().Equal("diff.stageLines", "diff.resetLines", "diff.copy");
    }

    [Test]
    public void Pointer_rule_evictions_are_not_in_the_registry()
    {
        // View toggles, Find, Go to line, next/previous change are pane-global - the pointer
        // rule keeps them out of the context menu (view bar / palette / hotkeys instead).
        IEnumerable<string> captions = DiffMenuRegistry.DiffActions
            .Concat(DiffMenuRegistry.BlameGutterActions)
            .Select(action => action.Caption.ToLowerInvariant());

        captions.Should().NotContain(caption =>
            caption.Contains("whitespace") || caption.Contains("entire file") || caption.Contains("context")
            || caption.Contains("syntax") || caption.Contains("find") || caption.Contains("go to line")
            || caption.Contains("treat") || caption.Contains("encoding") || caption.Contains("word wrap")
            || caption.Contains("scroll") || caption.Contains("appearance"));
    }
}
