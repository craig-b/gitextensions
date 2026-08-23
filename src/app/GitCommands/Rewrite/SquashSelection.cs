using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommands.Rewrite;

/// <summary>
///  Squashing a multi-row grid selection into one commit: valid only for a contiguous
///  first-parent chain ending at HEAD, executed as a soft reset to the oldest commit's
///  parent followed by a commit carrying every message.
/// </summary>
public static class SquashSelection
{
    /// <summary>Why the selection cannot be squashed, or null when it can.</summary>
    public static string? Validate(IReadOnlyList<GitRevision> newestFirst, ObjectId? headCommit)
    {
        if (newestFirst.Count < 2)
        {
            return "Select at least two commits.";
        }

        if (headCommit is null || newestFirst[0].ObjectId != headCommit)
        {
            return "The newest selected commit must be the current HEAD - squashing mid-history needs an interactive rebase.";
        }

        for (int i = 0; i < newestFirst.Count - 1; i++)
        {
            if (newestFirst[i].FirstParentId != newestFirst[i + 1].ObjectId)
            {
                return "The selection must be a contiguous run of first-parent commits (no gaps, no merge side-branches).";
            }
        }

        return newestFirst[^1].HasParent ? null : "Cannot squash into the root commit.";
    }

    /// <summary>The soft-reset target: the parent of the oldest selected commit.</summary>
    public static ObjectId ResetTarget(IReadOnlyList<GitRevision> newestFirst)
        => newestFirst[^1].FirstParentId;

    /// <summary>Every selected message, oldest first, ready for the commit editor.</summary>
    public static string CombinedMessage(IReadOnlyList<GitRevision> newestFirst)
        => string.Join("\n\n", newestFirst.Reverse()
            .Select(revision => string.IsNullOrEmpty(revision.Body) ? revision.Subject : revision.Body));
}
