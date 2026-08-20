namespace GitCommands.UserRepositoryHistory;

/// <summary>
///  One repository entry in a recent/favourite menu. The number carries the menu's accelerator
///  rule; how it is rendered (WinForms <c>&amp;1</c>, Avalonia <c>_1</c>, or not at all) is the
///  view's business — <see cref="RecentRepositoryMenu.AcceleratorText"/> is the classic style.
/// </summary>
/// <param name="Number">1-based position used for the accelerator; continues across the pinned/recent blocks, restarts per favourite category.</param>
/// <param name="Tooltip">The full path, only when it differs from the caption; otherwise <see langword="null"/> (no tooltip).</param>
/// <param name="BranchName">The cached current branch, or <see langword="null"/> when not cached.</param>
public sealed record RepoMenuEntry(Repository Repo, int Number, string Caption, string? Tooltip, string? BranchName, bool IsPinned);

/// <summary>The recent-repositories menu: a pinned block, then a recent block.</summary>
public sealed record RecentRepositoriesMenuModel(IReadOnlyList<RepoMenuEntry> Pinned, IReadOnlyList<RepoMenuEntry> Recent)
{
    /// <summary>A separator sits between the blocks only when both are non-empty.</summary>
    public bool ShowSeparator => Pinned.Count > 0 && Recent.Count > 0;
}

/// <summary>
///  One favourite category submenu. <paramref name="Category"/> is <see langword="null"/> for
///  legacy entries without one — the view supplies its own localized caption for that group
///  (historically this rendered as a submenu with no title at all).
/// </summary>
public sealed record FavouriteCategoryGroup(string? Category, IReadOnlyList<RepoMenuEntry> Entries);

/// <summary>
///  Builds the recent- and favourite-repositories menus the way the WinForms menus always have:
///  split via <see cref="RecentRepoSplitter"/>, number the entries, tooltip only when the caption
///  hides the path, branch name from the cache when known.
/// </summary>
public static class RecentRepositoryMenu
{
    /// <summary>The classic accelerator: 1..9 get <c>&amp;N</c>, 10 gets <c>1&amp;0</c>, later entries none.</summary>
    public static string AcceleratorText(int number) => number switch { < 10 => $"&{number}", 10 => "1&0", _ => $"{number}" };

    public static RecentRepositoriesMenuModel BuildRecent(
        IList<Repository> recentHistory,
        RecentRepoSplitterOptions options,
        Func<string, string?> branchNameLookup)
    {
        List<RecentRepoInfo> pinnedRepos = [];
        List<RecentRepoInfo> allRecentRepos = [];
        new RecentRepoSplitter(options).SplitRecentRepos(recentHistory, pinnedRepos, allRecentRepos);

        int number = 0;
        List<RepoMenuEntry> pinned = [];
        foreach (RecentRepoInfo repo in pinnedRepos)
        {
            pinned.Add(CreateEntry(repo, ++number, branchNameLookup, markPinned: true));
        }

        List<RepoMenuEntry> recent = [];
        foreach (RecentRepoInfo repo in allRecentRepos)
        {
            recent.Add(CreateEntry(repo, ++number, branchNameLookup, markPinned: true));
        }

        return new(pinned, recent);
    }

    public static IReadOnlyList<FavouriteCategoryGroup> BuildFavourites(
        IList<Repository> favouriteHistory,
        RecentRepoSplitterOptions options,
        Func<string, string?> branchNameLookup)
    {
        List<RecentRepoInfo> pinnedRepos = [];
        List<RecentRepoInfo> allRecentRepos = [];
        new RecentRepoSplitter(options).SplitRecentRepos(favouriteHistory, pinnedRepos, allRecentRepos);

        List<FavouriteCategoryGroup> groups = [];

        // Union is set-union on reference identity: it dedupes entries present in both lists when
        // top repositories are not hidden from the recent list.
        foreach (IGrouping<string?, RecentRepoInfo> group in pinnedRepos.Union(allRecentRepos).GroupBy(r => r.Repo.Category).OrderBy(g => g.Key))
        {
            int number = 0;
            List<RepoMenuEntry> entries = [];
            foreach (RecentRepoInfo repo in group)
            {
                // The favourites menu never shows pin icons - anchoring is a recent-menu affordance.
                entries.Add(CreateEntry(repo, ++number, branchNameLookup, markPinned: false));
            }

            groups.Add(new(group.Key, entries));
        }

        return groups;
    }

    private static RepoMenuEntry CreateEntry(RecentRepoInfo info, int number, Func<string, string?> branchNameLookup, bool markPinned)
    {
        string caption = info.Caption ?? info.Repo.Path;
        return new(
            info.Repo,
            number,
            caption,
            Tooltip: info.Repo.Path != caption ? info.Repo.Path : null,
            BranchName: branchNameLookup(info.Repo.Path),
            IsPinned: markPinned && info.Anchored);
    }
}

/// <summary>
///  The in-menu repository filter: blank shows everything, otherwise a case-insensitive substring
///  match on the item's text. Which items are exempt (the filter box itself, fixed commands,
///  separators) is the view's concern.
/// </summary>
public static class MenuFilter
{
    public static bool IsVisible(string? itemText, string? filterText)
        => string.IsNullOrWhiteSpace(filterText)
            || itemText?.Contains(filterText, StringComparison.CurrentCultureIgnoreCase) is true;
}
