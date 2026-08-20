using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommands.Browse;

/// <summary>The repo-state inputs the browse window's enablement rules read.</summary>
public readonly record struct RepoUiState(bool HasWorkingDir, bool IsValidWorkingDir, bool IsBare, bool IsDashboardVisible);

/// <summary>What the browse chrome allows for a repo state.</summary>
public readonly record struct RepoUiCapabilities(
    bool ValidBrowseDir,
    bool CanLevelUp,
    bool CanCommit,
    bool CanShowDetailTabs,
    bool CanShowStashCount)
{
    public static RepoUiCapabilities For(RepoUiState state)
    {
        bool validBrowseDir = !state.IsDashboardVisible && state.IsValidWorkingDir;
        return new RepoUiCapabilities(
            ValidBrowseDir: validBrowseDir,
            CanLevelUp: state.HasWorkingDir && !state.IsBare,
            CanCommit: validBrowseDir && !state.IsBare,
            CanShowDetailTabs: validBrowseDir,
            CanShowStashCount: !state.IsBare);
    }
}

/// <summary>The Commands-menu enablement for the grid selection (FormBrowse's dropdown-opening rules).</summary>
public readonly record struct RevisionCommandAvailability(
    bool SingleNormalCommit,
    bool CanBranchCheckoutMergeCherryPickBisect,
    bool CanRebase,
    bool CanTagOrArchive,
    bool CanRepositoryCommands)
{
    public static RevisionCommandAvailability For(bool isBare, IReadOnlyList<GitRevision> selectedRevisions)
    {
        bool singleNormalCommit = selectedRevisions.Count == 1 && !selectedRevisions[0].IsArtificial;
        return new RevisionCommandAvailability(
            SingleNormalCommit: singleNormalCommit,
            CanBranchCheckoutMergeCherryPickBisect: singleNormalCommit && !isBare,
            CanRebase: selectedRevisions.Count is 1 or 2 && selectedRevisions.All(revision => !revision.IsArtificial) && !isBare,
            CanTagOrArchive: singleNormalCommit,
            CanRepositoryCommands: !isBare);
    }
}
