using GitCommands;
using GitCommands.Dashboard;
using GitCommands.UserRepositoryHistory;
using ResourceManager;

namespace GitUI.CommandsDialogs.BrowseDialog.DashboardControl;

public interface IUserRepositoriesListController
{
    Task AssignCategoryAsync(Repository repository, string? category);
    Task RenameCategoryAsync(IEnumerable<Repository> repositories, string? originalName, string? newName);
    Task ClearRecentAsync(IEnumerable<Repository> repositories);
    string GetCurrentBranchName(string path);
    bool IsValidGitWorkingDir(string path);
    (IReadOnlyList<RecentRepoInfo> recentRepositories, IReadOnlyList<RecentRepoInfo> favouriteRepositories) PreRenderRepositories(string filter);
    bool RemoveInvalidRepository(string path);
    void ClearCache();
}

public sealed class UserRepositoriesListController : IUserRepositoriesListController
{
    private readonly ILocalRepositoryManager _localRepositoryManager;
    private readonly IInvalidRepositoryRemover _invalidRepositoryRemover;
    private readonly IRepositoryCurrentBranchNameCache _branchNameCache;

    // Holds the raw, unfiltered list of repositories.
    // This is done to allow fast filtering of all known repos.
    private IList<Repository>? _allRecentRepositories;
    private IList<Repository>? _allFavoriteRepositories;

    public UserRepositoriesListController(ILocalRepositoryManager localRepositoryManager, IInvalidRepositoryRemover invalidRepositoryRemover, IRepositoryCurrentBranchNameCache branchNameCache)
    {
        _localRepositoryManager = localRepositoryManager;
        _invalidRepositoryRemover = invalidRepositoryRemover;
        _branchNameCache = branchNameCache;
    }

    public async Task AssignCategoryAsync(Repository repository, string? category)
    {
        ArgumentNullException.ThrowIfNull(repository);

        await CategoryCommands.AssignAsync(_localRepositoryManager, repository, category);
    }

    public Task RenameCategoryAsync(IEnumerable<Repository> repositories, string? originalName, string? newName)
        => CategoryCommands.RenameAsync(_localRepositoryManager, repositories, originalName, newName);

    public Task ClearRecentAsync(IEnumerable<Repository> repositories)
        => CategoryCommands.ClearRecentAsync(_localRepositoryManager, repositories);

    /// <summary>
    /// Clears the repository cache. After this call the repository list will be loaded from disk.
    /// Note: The info in _branchNameCache is updated by Dashboard (but not read), the data is shared
    /// with the repo menus in both Dashboard and Browse.
    /// </summary>
    public void ClearCache()
    {
        _allRecentRepositories = null;
        _allFavoriteRepositories = null;
        _branchNameCache.InvalidateAll();
    }

    public string GetCurrentBranchName(string path)
    {
        if (!AppSettings.ShowRepoCurrentBranch || GitModule.IsBareRepository(path))
        {
            return string.Empty;
        }

        return _branchNameCache.GetCurrentBranchName(path);
    }

    public bool IsValidGitWorkingDir(string path)
    {
        return GitModule.IsValidGitWorkingDir(path);
    }

    public (IReadOnlyList<RecentRepoInfo> recentRepositories, IReadOnlyList<RecentRepoInfo> favouriteRepositories) PreRenderRepositories(string pattern)
    {
        RecentRepoSplitterOptions options = RecentRepoSplitterOptions.FromAppSettings(
            caption => TextRenderer.MeasureText(caption, AppFonts.App).Width);

        // The injected manager, not the RepositoryHistoryManager static - the class always took
        // the dependency and then bypassed it here.
        _allRecentRepositories ??= ThreadHelper.JoinableTaskFactory.Run(_localRepositoryManager.LoadRecentHistoryAsync);
        IReadOnlyList<RecentRepoInfo> recentRepositories = DashboardList.SplitAndMerge(DashboardList.Filter(_allRecentRepositories, pattern), options);

        _allFavoriteRepositories ??= ThreadHelper.JoinableTaskFactory.Run(_localRepositoryManager.LoadFavouriteHistoryAsync);
        IReadOnlyList<RecentRepoInfo> favouriteRepositories = DashboardList.SplitAndMerge(DashboardList.Filter(_allFavoriteRepositories, pattern), options);

        return (recentRepositories, favouriteRepositories);
    }

    public bool RemoveInvalidRepository(string path)
       => _invalidRepositoryRemover.ShowDeleteInvalidRepositoryDialog(path);
}
