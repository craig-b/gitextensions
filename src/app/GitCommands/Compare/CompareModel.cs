using GitExtensions.Extensibility.Git;

namespace GitCommands.Compare;

/// <summary>The compare dialog's decisions (FormDiff binds them).</summary>
public static class CompareRevisions
{
    /// <summary>
    ///  The merge base of the pair, with artificial revisions mapped to the current head.
    ///  Null when either side is unresolvable or both map to the same commit - the
    ///  compare-to-merge-base option disables then.
    /// </summary>
    public static ObjectId? ResolveMergeBase(ObjectId firstId, ObjectId secondId, Func<ObjectId> getCurrentHead, Func<ObjectId, ObjectId, ObjectId> getMergeBase)
    {
        ObjectId firstMergeId = firstId.IsArtificial ? getCurrentHead() : firstId;
        ObjectId secondMergeId = secondId.IsArtificial ? getCurrentHead() : secondId;
        if (firstMergeId.IsZero || secondMergeId.IsZero || firstMergeId == secondMergeId)
        {
            return null;
        }

        ObjectId mergeBase = getMergeBase(firstMergeId, secondMergeId);
        return mergeBase.IsZero ? null : mergeBase;
    }

    /// <summary>The BASE side of the diff: the merge base when that option is on, else the first commit.</summary>
    public static ObjectId ResolveBase(ObjectId firstId, ObjectId? mergeBase, bool compareToMergeBase)
        => compareToMergeBase && mergeBase is ObjectId resolvedBase ? resolvedBase : firstId;

    /// <summary>
    ///  git-for-windows fails "difftool --dir-diff -R" from the working directory, so
    ///  directory diffs are disabled when the BASE side is the work tree.
    /// </summary>
    public static bool DirDiffAllowed(ObjectId firstId) => firstId != ObjectId.WorkTreeId;

    /// <summary>Comparing the working directory to itself is refused.</summary>
    public static bool CanCompareToWorkingDirectory(ObjectId firstId) => firstId != ObjectId.WorkTreeId;
}
