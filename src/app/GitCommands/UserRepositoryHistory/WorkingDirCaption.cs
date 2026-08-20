namespace GitCommands.UserRepositoryHistory;

/// <summary>
///  The caption shown on the working-directory toolbar button: the repository's shortened menu
///  caption when it appears in the recent history, the raw path otherwise, both through
///  <see cref="PathUtil.GetDisplayPath"/>. Promoting the repository in the MRU is the host's
///  explicit step on a repository switch — computing a caption never writes history.
/// </summary>
public static class WorkingDirCaption
{
    public static string Compute(string workingDir, IList<Repository> recentHistory, RecentRepoSplitterOptions options)
    {
        // Same list for both outputs: "give me everything", pinned or not.
        List<RecentRepoInfo> allRepos = [];
        new RecentRepoSplitter(options).SplitRecentRepos(recentHistory, allRepos, allRepos);

        RecentRepoInfo? current = allRepos.Find(r => r.Repo.Path.Equals(workingDir, StringComparison.InvariantCultureIgnoreCase));
        return PathUtil.GetDisplayPath(current?.Caption ?? workingDir);
    }
}
