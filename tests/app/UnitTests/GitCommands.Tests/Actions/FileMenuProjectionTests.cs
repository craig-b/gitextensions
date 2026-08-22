using GitCommands.Actions;

namespace GitCommandsTests.Actions;

public sealed class FileMenuProjectionTests
{
    private static IReadOnlyList<string> Flatten(IReadOnlyList<IReadOnlyList<ProjectedMenuItem>> groups)
        => [.. groups.SelectMany(group => group.Select(item => item.Action.Id))];

    [Test]
    public void Simple_mode_shows_core_tier_only()
    {
        var groups = MenuProjector.Project(FileMenuRegistry.FileActions, MenuProfile.Simple,
            action => FileMenuRegistry.IsApplicable(action, new FileMenuContext(AnyWorkTree: true, IsArtificialRevision: true)));

        Flatten(groups).Should().Equal(
            "file.stage", "file.unstage", "file.resetChanges",
            "file.openDifftool", "file.history", "file.blame",
            "file.copyPath", "file.copyRelativePath",
            "submodule.open", "submodule.update", "submodule.reset", "submodule.stash", "submodule.commit");
    }

    [Test]
    public void Conflicted_selection_prepends_the_resolve_group()
    {
        IReadOnlyList<ActionDescriptor> menu = FileMenuRegistry.FileMenuFor(new FileMenuContext(AnyConflicted: true));
        menu[0].Id.Should().Be("conflict.ours");
        menu.Select(action => action.Id).Should().Contain("file.history");

        FileMenuRegistry.FileMenuFor(new FileMenuContext())
            .Should().BeSameAs(FileMenuRegistry.FileActions);
    }

    [Test]
    public void Applicability_follows_surface_and_selection()
    {
        ActionDescriptor Get(string id) => FileMenuRegistry.FileActions.Single(action => action.Id == id);

        FileMenuContext revisionDiff = new(AnyTracked: true, IsDiffGridSurface: true);
        FileMenuContext unstagedList = new(AnyWorkTree: true, IsArtificialRevision: true);
        FileMenuContext stagedList = new(AnyIndex: true, IsArtificialRevision: true);
        FileMenuContext submodule = new(AnyWorkTree: true, AnySubmodule: true, IsArtificialRevision: true);

        FileMenuRegistry.IsApplicable(Get("file.stage"), unstagedList).Should().BeTrue();
        FileMenuRegistry.IsApplicable(Get("file.stage"), stagedList).Should().BeFalse();
        FileMenuRegistry.IsApplicable(Get("file.unstage"), stagedList).Should().BeTrue();
        FileMenuRegistry.IsApplicable(Get("file.stage"), revisionDiff).Should().BeFalse();

        FileMenuRegistry.IsApplicable(Get("file.filterInGrid"), revisionDiff).Should().BeTrue();
        FileMenuRegistry.IsApplicable(Get("file.filterInGrid"), unstagedList).Should().BeFalse();

        // Save-as/open-temp read a committed blob: never on the artificial (worktree/index) lists.
        FileMenuRegistry.IsApplicable(Get("file.saveAs"), revisionDiff).Should().BeTrue();
        FileMenuRegistry.IsApplicable(Get("file.saveAs"), unstagedList).Should().BeFalse();

        // Delete acts on the working tree: artificial revision with the files on disk.
        FileMenuRegistry.IsApplicable(Get("file.delete"), unstagedList with { SelectionOnDisk = true }).Should().BeTrue();
        FileMenuRegistry.IsApplicable(Get("file.delete"), revisionDiff with { SelectionOnDisk = true }).Should().BeFalse();

        FileMenuRegistry.IsApplicable(Get("submodule.update"), submodule).Should().BeTrue();
        FileMenuRegistry.IsApplicable(Get("submodule.update"), unstagedList).Should().BeFalse();
        FileMenuRegistry.IsApplicable(Get("file.blame"), submodule).Should().BeFalse();

        FileMenuRegistry.IsApplicable(Get("file.blame"), revisionDiff with { SelectedCount = 2 }).Should().BeFalse();
        FileMenuRegistry.IsApplicable(Get("file.history"), revisionDiff with { SelectedCount = 2 }).Should().BeTrue();
    }

    [Test]
    public void Reviewed_evictions_are_not_in_the_registry()
    {
        // Sort-and-group-by, the Show-'Find...' toggle, tree chrome, and Windows-only items
        // (Visual Studio, WSL/Cygwin copy variants) were evicted in review - they must not
        // reappear under new ids.
        IEnumerable<string> captions = FileMenuRegistry.FileActions.Select(action => action.Caption.ToLowerInvariant());
        captions.Should().NotContain(caption =>
            caption.Contains("sort") || caption.Contains("expand") || caption.Contains("collapse")
            || caption.Contains("visual studio") || caption.Contains("wsl") || caption.Contains("cygwin"));
    }
}
