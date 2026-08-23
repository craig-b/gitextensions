using GitExtensions.Extensibility.Git;
using Microsoft;

namespace GitCommands.Commit;

/// <summary>
///  Reconciles the commit dialog's unstaged list after a stage or unstage operation, so the
///  dialog can update in place instead of rescanning the whole status (extracted verbatim from
///  FormCommit.Stage/Unstage). Both methods mutate the flags of the
///  <see cref="GitItemStatus"/> instances they are given - the caller's items are the ones that
///  end up in the returned list, exactly as the inline code always did.
/// </summary>
public static class StagedListReconciler
{
    /// <summary>
    ///  After files were successfully staged: removes them from the unstaged list, except dirty
    ///  submodules, which stay unstaged with a refreshed status.
    /// </summary>
    /// <param name="currentUnstagedFiles">The unstaged list as currently displayed.</param>
    /// <param name="stagedFiles">The files that were just staged.</param>
    /// <param name="module">Supplies submodule status for the entries that stay.</param>
    /// <returns>The new unstaged list.</returns>
    public static List<GitItemStatus> ReconcileAfterStage(
        IReadOnlyList<GitItemStatus> currentUnstagedFiles,
        IReadOnlyList<GitItemStatus> stagedFiles,
        IGitModule module)
    {
        List<GitItemStatus> unstagedFiles = [.. currentUnstagedFiles];

        HashSet<string?> names = [];
        foreach (GitItemStatus item in stagedFiles)
        {
            names.Add(item.Name);
            names.Add(item.OldName);
        }

        HashSet<GitItemStatus> unstagedItems = [];

        foreach (GitItemStatus item in unstagedFiles)
        {
            if (names.Contains(item.Name))
            {
                unstagedItems.Add(item);
            }
        }

        // Dirty submodules need to be kept in unstaged, update the status
        unstagedFiles.RemoveAll(
            item =>
            {
                if ((!item.IsSubmodule || !item.IsDirty) && unstagedItems.Contains(item))
                {
                    return true;
                }

                module.GetSubmoduleCurrentStatus([item]);
                return false;
            });

        return unstagedFiles;
    }

    /// <summary>
    ///  After files were unstaged: every file that actually left the (re-read) staged list is
    ///  moved into the unstaged list - updating an existing entry's flags where one exists,
    ///  synthesizing a delete + untracked-new pair for renames, and marking the rest as
    ///  work-tree changes.
    /// </summary>
    /// <param name="unstagedItems">The files that were just unstaged.</param>
    /// <param name="currentStagedFiles">The staged list as re-read from git after the operation.</param>
    /// <param name="currentUnstagedFiles">The unstaged list as currently displayed.</param>
    /// <param name="module">Supplies submodule status for updated entries.</param>
    /// <returns>The new unstaged list.</returns>
    public static List<GitItemStatus> ReconcileAfterUnstage(
        IReadOnlyList<GitItemStatus> unstagedItems,
        IReadOnlyList<GitItemStatus> currentStagedFiles,
        IReadOnlyList<GitItemStatus> currentUnstagedFiles,
        IGitModule module)
    {
        List<GitItemStatus> stagedFiles = [.. currentStagedFiles];
        List<GitItemStatus> unstagedFiles = [.. currentUnstagedFiles];

        foreach (GitItemStatus item in unstagedItems)
        {
            GitItemStatus item1 = item;
            if (stagedFiles.Exists(i => i.Name == item1.Name))
            {
                continue;
            }

            item.IsTracked = !item.IsNew || item.IsChanged || item.IsDeleted;
            int index = unstagedFiles.FindIndex(i => i.Name == item.Name);

            if (index >= 0)
            {
                unstagedFiles[index].IsNew = item.IsNew;
                unstagedFiles[index].IsDeleted = item.IsDeleted;
                unstagedFiles[index].IsTracked = item.IsTracked;
                unstagedFiles[index].IsChanged = item.IsChanged;

                // if this is a submodule, update the status, may be dirty
                module.GetSubmoduleCurrentStatus([unstagedFiles[index]]);

                continue;
            }

            if (item.IsRenamed)
            {
                Validates.NotNull(item.OldName);

                GitItemStatus clone = new(item.OldName)
                {
                    IsDeleted = true,
                    IsTracked = true,
                    Staged = StagedStatus.WorkTree
                };
                unstagedFiles.Add(clone);

                item.IsRenamed = false;
                item.IsNew = true;
                item.IsTracked = false;
                item.OldName = string.Empty;
            }

            item.Staged = StagedStatus.WorkTree;
            unstagedFiles.Add(item);
        }

        return unstagedFiles;
    }
}
