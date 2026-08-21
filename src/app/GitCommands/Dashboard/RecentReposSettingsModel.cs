using GitCommands.UserRepositoryHistory;

namespace GitCommands.Dashboard;

/// <summary>
///  The recent-repositories settings dialog's seven values as a snapshot: load once, edit freely,
///  persist only on OK — the preview renders from the unsaved snapshot through the very splitter
///  the real menus use.
/// </summary>
public sealed record RecentReposSettingsSnapshot(
    ShorteningRecentRepoPathStrategy ShorteningStrategy,
    bool HideTopRepositoriesFromRecentList,
    bool SortTopRepos,
    bool SortRecentRepos,
    int RecentReposComboMinWidth,
    int MaxTopRepositories,
    int RecentRepositoriesHistorySize)
{
    public static RecentReposSettingsSnapshot Load()
        => new(
            AppSettings.ShorteningRecentRepoPathStrategy,
            AppSettings.HideTopRepositoriesFromRecentList.Value,
            AppSettings.SortTopRepos,
            AppSettings.SortRecentRepos,
            AppSettings.RecentReposComboMinWidth,
            AppSettings.MaxTopRepositories,
            AppSettings.RecentRepositoriesHistorySize);

    public void Save()
    {
        AppSettings.ShorteningRecentRepoPathStrategy = ShorteningStrategy;
        AppSettings.HideTopRepositoriesFromRecentList.Value = HideTopRepositoriesFromRecentList;
        AppSettings.SortTopRepos = SortTopRepos;
        AppSettings.SortRecentRepos = SortRecentRepos;
        AppSettings.RecentReposComboMinWidth = RecentReposComboMinWidth;
        AppSettings.MaxTopRepositories = MaxTopRepositories;
        AppSettings.RecentRepositoriesHistorySize = RecentRepositoriesHistorySize;
    }

    public RecentRepoSplitterOptions ToSplitterOptions(Func<string?, int>? measureCaptionWidth = null)
        => new(
            MaxTopRepositories,
            HideTopRepositoriesFromRecentList,
            ShorteningStrategy,
            SortTopRepos,
            SortRecentRepos,
            RecentReposComboMinWidth,
            measureCaptionWidth);
}

/// <summary>
///  The combo-width spinner's hysteresis around the minimum: shrinking below the minimum snaps
///  to 0 (= auto-size), growing from below snaps up to the minimum.
/// </summary>
public static class ComboWidthRule
{
    public const int MinComboWidthAllowed = 30;

    public static int Snap(int previousValue, int newValue)
    {
        if (newValue == previousValue || newValue >= MinComboWidthAllowed)
        {
            return newValue;
        }

        return newValue < previousValue ? 0 : MinComboWidthAllowed;
    }
}

/// <summary>Which anchor commands apply to a repository, given its current anchor.</summary>
public static class AnchorCommandAvailability
{
    public static (bool CanAnchorTop, bool CanAnchorRecent, bool CanRemoveAnchor) For(Repository.RepositoryAnchor anchor)
        => (anchor is not Repository.RepositoryAnchor.AnchoredInTop,
            anchor is not Repository.RepositoryAnchor.AnchoredInRecent,
            anchor is not Repository.RepositoryAnchor.None);
}
