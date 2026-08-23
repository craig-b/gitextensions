using System.Buffers;
using GitExtensions.Extensibility.Git;

namespace GitCommands.LeftPanel;

/// <summary>A worktree row of the left panel: the worktree, whether it is checked out here, and its display path.</summary>
public sealed class WorktreeTreeNode
{
    public required GitWorktree Worktree { get; init; }

    public bool IsCurrent { get; init; }

    public required string DisplayPath { get; init; }
}

/// <summary>
///  Builds the worktree list the left panel shows. Git always lists the
///  main worktree first; relative paths are computed against the main worktree's parent so
///  linked worktrees in a sibling directory read "repo.worktrees/feature-a" rather than flat
///  names, and the common directory prefix is stripped from the non-main entries.
/// </summary>
public static class WorktreeTreeBuilder
{
    private static readonly SearchValues<char> _directorySeparatorChars = SearchValues.Create(
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);

    private static readonly SearchValues<char> _wordBoundaryChars = SearchValues.Create(
        ['_', '-', '.', ' ']);

    public static IReadOnlyList<WorktreeTreeNode> Build(IReadOnlyList<GitWorktree> worktrees, string workingDir)
    {
        string currentWorkingDir = workingDir.TrimEnd(Path.DirectorySeparatorChar);

        string mainWorktreePath = worktrees.Count > 0
            ? worktrees[0].Path.TrimEnd(Path.DirectorySeparatorChar)
            : currentWorkingDir;
        string parentDir = Path.GetDirectoryName(mainWorktreePath) ?? mainWorktreePath;

        List<(GitWorktree Worktree, bool IsCurrent, string RelativePath)> worktreeInfos = [];

        foreach (GitWorktree worktree in worktrees)
        {
            bool isCurrent = string.Equals(
                worktree.Path.TrimEnd(Path.DirectorySeparatorChar),
                currentWorkingDir,
                StringComparison.OrdinalIgnoreCase);

            string relativePath;
            try
            {
                relativePath = Path.GetRelativePath(parentDir, worktree.Path);
            }
            catch
            {
                relativePath = worktree.Path;
            }

            worktreeInfos.Add((worktree, isCurrent, relativePath));
        }

        // Strip the common directory prefix from non-main worktrees to reduce visual noise.
        // The first worktree is always the main worktree and is excluded from prefix calculation.
        string commonPrefix = GetCommonPrefix(
            worktreeInfos.Skip(1).Select(worktreeInfo => worktreeInfo.RelativePath));

        List<WorktreeTreeNode> nodes = [];
        for (int i = 0; i < worktreeInfos.Count; i++)
        {
            (GitWorktree worktree, bool isCurrent, string relativePath) = worktreeInfos[i];

            string displayPath = i > 0
                && commonPrefix.Length > 0
                && relativePath.StartsWith(commonPrefix, StringComparison.OrdinalIgnoreCase)
                ? relativePath[commonPrefix.Length..]
                : relativePath;

            nodes.Add(new WorktreeTreeNode { Worktree = worktree, IsCurrent = isCurrent, DisplayPath = displayPath });
        }

        return nodes;
    }

    /// <summary>
    ///  Finds the longest common prefix among the given relative paths, snapping to a word boundary.
    /// </summary>
    /// <remarks>
    ///  Word boundaries are directory separators and the characters <c>_</c>, <c>-</c>, <c>.</c>, and space.
    ///  This ensures names like "apricot" and "apple" are not truncated to "pricot" and "pple".
    /// </remarks>
    /// <returns>
    ///  The common prefix including its trailing delimiter, or an empty string if there is no common prefix.
    /// </returns>
    internal static string GetCommonPrefix(IEnumerable<string> paths)
    {
        ReadOnlySpan<char> prefix = default;
        bool hasDirectorySeparator = false;
        int count = 0;

        foreach (string path in paths)
        {
            ReadOnlySpan<char> span = path.AsSpan();
            count++;

            if (count == 1)
            {
                prefix = span;
                hasDirectorySeparator = span.ContainsAny(_directorySeparatorChars);
                continue;
            }

            hasDirectorySeparator = hasDirectorySeparator || span.ContainsAny(_directorySeparatorChars);

            // Find the longest character-for-character match.
            int limit = Math.Min(prefix.Length, span.Length);
            int matchLength = 0;
            for (int i = 0; i < limit; i++)
            {
                if (char.ToUpperInvariant(prefix[i]) != char.ToUpperInvariant(span[i]))
                {
                    break;
                }

                matchLength = i + 1;
            }

            prefix = prefix[..matchLength];
        }

        if (count < 2 || prefix.Length == 0)
        {
            return "";
        }

        // Snap to the last directory separator in the common prefix.
        int dirSepIndex = prefix.LastIndexOfAny(_directorySeparatorChars);
        if (dirSepIndex >= 0)
        {
            return prefix[..(dirSepIndex + 1)].ToString();
        }

        // Fall back to word-boundary characters only when none of the paths contain
        // directory separators. This handles the case where all worktrees are flat
        // siblings in the same directory (e.g. "repo_dev", "repo_test").
        if (hasDirectorySeparator)
        {
            return "";
        }

        int boundaryIndex = prefix.LastIndexOfAny(_wordBoundaryChars);
        if (boundaryIndex < 0)
        {
            return "";
        }

        return prefix[..(boundaryIndex + 1)].ToString();
    }
}
