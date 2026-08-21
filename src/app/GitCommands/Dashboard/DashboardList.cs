using GitCommands.UserRepositoryHistory;

namespace GitCommands.Dashboard;

/// <summary>One repository tile: the shortened caption plus where it belongs.</summary>
public sealed record DashboardRepositoryItem(Repository Repo, string Caption, bool IsFavourite);

/// <summary>
///  One dashboard group: the fixed recent group (<see cref="IsRecent"/>, view-localized caption)
///  or a favourite category. Items are placed at build time — there is no header lookup to miss.
/// </summary>
public sealed record DashboardRepositoryGroup(string? Category, bool IsRecent, IReadOnlyList<DashboardRepositoryItem> Items);

/// <summary>
///  The dashboard's list shaping: filter, split-and-merge, and grouping. The comparer for
///  category identity is <see cref="StringComparer.CurrentCulture"/> throughout — grouping,
///  ordering, and name validation agree (historically validation compared ordinally).
/// </summary>
public static class DashboardList
{
    public static StringComparer CategoryComparer => StringComparer.CurrentCulture;

    /// <summary>
    ///  The search filter. Matches the path or the category ("Search repositories" historically
    ///  searched the path only, despite its placeholder).
    /// </summary>
    public static IList<Repository> Filter(IList<Repository> repositories, string pattern)
    {
        if (pattern.Length == 0)
        {
            return repositories;
        }

        return [.. repositories.Where(repo =>
            repo.Path.Contains(pattern, StringComparison.CurrentCultureIgnoreCase)
            || repo.Category?.Contains(pattern, StringComparison.CurrentCultureIgnoreCase) is true)];
    }

    /// <summary>
    ///  Split via <see cref="RecentRepoSplitter"/> and merge the top block back in front of the
    ///  rest (reference-identity union, so a repo appearing in both blocks is listed once).
    /// </summary>
    public static IReadOnlyList<RecentRepoInfo> SplitAndMerge(IList<Repository> repositories, RecentRepoSplitterOptions options)
    {
        List<RecentRepoInfo> topRepos = [];
        List<RecentRepoInfo> recentRepos = [];
        new RecentRepoSplitter(options).SplitRecentRepos(repositories, topRepos, recentRepos);
        return [.. topRepos.Union(recentRepos)];
    }

    /// <summary>The distinct favourite category headers over both lists, ordered.</summary>
    public static IReadOnlyList<string> CategoryHeaders(IEnumerable<RecentRepoInfo> recentRepositories, IEnumerable<RecentRepoInfo> favouriteRepositories)
        => [.. recentRepositories.Concat(favouriteRepositories)
            .Select(repo => repo.Repo.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Cast<string>()
            .Distinct(CategoryComparer)
            .OrderBy(category => category, CategoryComparer)];

    /// <summary>
    ///  The rendered structure: the recent group first, then one group per favourite category.
    ///  Favourites whose category matches no header (blank) land nowhere — matching the historical
    ///  favourite-membership rule that a favourite IS a repository with a category.
    /// </summary>
    public static IReadOnlyList<DashboardRepositoryGroup> Groups(
        IReadOnlyList<RecentRepoInfo> recentRepositories,
        IReadOnlyList<RecentRepoInfo> favouriteRepositories)
    {
        List<DashboardRepositoryGroup> groups =
        [
            new(Category: null, IsRecent: true,
                [.. recentRepositories.Select(repo => new DashboardRepositoryItem(repo.Repo, repo.Caption ?? repo.Repo.Path, IsFavourite: false))]),
        ];

        foreach (string category in CategoryHeaders(recentRepositories, favouriteRepositories))
        {
            groups.Add(new(
                category,
                IsRecent: false,
                [.. favouriteRepositories
                    .Where(repo => CategoryComparer.Equals(repo.Repo.Category, category))
                    .Select(repo => new DashboardRepositoryItem(repo.Repo, repo.Caption ?? repo.Repo.Path, IsFavourite: true))]));
        }

        return groups;
    }
}
