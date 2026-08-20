using GitCommands;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs;
using GitUI.Properties;
using Microsoft.VisualStudio.Threading;
using ResourceManager;

namespace GitUI;

/// <summary>
///  Represents a service for managing the git repository history.
/// </summary>
public interface IRepositoryHistoryUIService
{
    /// <summary>
    ///  Occurs whenever the git module changes.
    /// </summary>
    event EventHandler<GitModuleEventArgs> GitModuleChanged;

    /// <summary>
    ///  Populates the "Favourite repositories" menu in the Dashboard.
    ///  Both the submenu to the WorkingDir button in Browse and menu in Dashboard.
    /// </summary>
    /// <param name="container">The container to populate with menu items.</param>
    void PopulateFavouriteRepositoriesMenu(ToolStripDropDownItem container);

    /// <summary>
    ///  Populates the "Recent repositories" menu.
    ///  Both the WorkingDir button in Browse and menu in Dashboard.
    /// </summary>
    /// <param name="container">The container to populate with menu items.</param>
    void PopulateRecentRepositoriesMenu(ToolStripDropDownItem container);

    /// <summary>
    ///  Start updating the branch name cache.
    /// </summary>
    /// <param name="onlyIfEmpty">Start updating only if the cache is empty.</param>
    void TriggerBranchNameCacheUpdate(bool onlyIfEmpty = false);
}

internal sealed class RepositoryHistoryUIService : IRepositoryHistoryUIService
{
    private static readonly TranslationString _noCategory = new("(no category)");

    private readonly IGitExecutorProvider _executorProvider;
    private readonly IRepositoryCurrentBranchNameCache _branchNameCache;
    private readonly IInvalidRepositoryRemover _invalidRepositoryRemover;
    private readonly BranchNameCacheUpdatePolicy _updatePolicy;
    private readonly CancellationTokenSequence _branchCacheSequence = new();
    private JoinableTask? _branchCacheUpdateTask;

    public event EventHandler<GitModuleEventArgs>? GitModuleChanged;

    internal RepositoryHistoryUIService(IGitExecutorProvider executorProvider, IRepositoryCurrentBranchNameCache branchNameCache, IInvalidRepositoryRemover invalidRepositoryRemover)
    {
        _executorProvider = executorProvider;
        _branchNameCache = branchNameCache;
        _invalidRepositoryRemover = invalidRepositoryRemover;
        _updatePolicy = new BranchNameCacheUpdatePolicy(branchNameCache);
    }

    private void AddRepositoryEntry(ToolStripDropDownItem menuItemContainer, RepoMenuEntry entry)
    {
        ToolStripMenuItem item = new($"{RecentRepositoryMenu.AcceleratorText(entry.Number)}: {entry.Caption}")
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
        };

        if (entry.IsPinned)
        {
            item.Image = Images.Pin;
        }

        menuItemContainer.DropDownItems.Add(item);

        item.Click += (_, _) => OpenRepo(entry.Repo.Path);

        if (entry.Tooltip is not null)
        {
            item.ToolTipText = entry.Tooltip;
        }

        if (entry.BranchName is not null)
        {
            item.ShortcutKeyDisplayString = entry.BranchName;
        }
    }

    private void ChangeWorkingDir(string path)
    {
        GitModule module = new(_executorProvider, path);
        if (module.IsValidGitWorkingDir())
        {
            GitModuleChanged?.Invoke(this, new GitModuleEventArgs(module));
            return;
        }

        _invalidRepositoryRemover.ShowDeleteInvalidRepositoryDialog(path);
    }

    private void OpenRepo(string repoPath)
    {
        if (Control.ModifierKeys != Keys.Control)
        {
            ChangeWorkingDir(repoPath);
            return;
        }

        GitUICommands.LaunchBrowse(repoPath);
    }

    public void PopulateFavouriteRepositoriesMenu(ToolStripDropDownItem container)
    {
        JoinableTask? branchCacheUpdateTask = _branchCacheUpdateTask;
        if (branchCacheUpdateTask is not null && branchCacheUpdateTask.IsCompleted)
        {
            try
            {
                branchCacheUpdateTask.Join();
            }
            catch (OperationCanceledException)
            {
                // OK
            }
        }

        container.DropDownItems.Clear();

        IList<Repository> repositoryHistory = ThreadHelper.JoinableTaskFactory.Run(
            RepositoryHistoryManager.Locals.LoadFavouriteHistoryAsync);

        if (repositoryHistory.Count < 1)
        {
            return;
        }

        PopulateFavouriteRepositoriesMenu(container, repositoryHistory);
    }

    private void PopulateFavouriteRepositoriesMenu(ToolStripDropDownItem container, in IList<Repository> repositoryHistory)
    {
        RecentRepoSplitterOptions options = RecentRepoSplitterOptions.FromAppSettings(
            caption => TextRenderer.MeasureText(caption, container.Font).Width);

        foreach (FavouriteCategoryGroup group in RecentRepositoryMenu.BuildFavourites(repositoryHistory, options, _branchNameCache.GetCachedBranchName))
        {
            ToolStripMenuItem menuItemCategory = new(group.Category ?? _noCategory.Text);
            container.DropDownItems.Add(menuItemCategory);

            menuItemCategory.DropDown.SuspendLayout();
            foreach (RepoMenuEntry entry in group.Entries)
            {
                AddRepositoryEntry(menuItemCategory, entry);
            }

            menuItemCategory.DropDown.ResumeLayout();
        }
    }

    public void PopulateRecentRepositoriesMenu(ToolStripDropDownItem container)
    {
        JoinableTask? branchCacheUpdateTask = _branchCacheUpdateTask;
        if (branchCacheUpdateTask is not null && branchCacheUpdateTask.IsCompleted)
        {
            try
            {
                branchCacheUpdateTask.Join();
            }
            catch (OperationCanceledException)
            {
                // OK
            }
        }

        IList<Repository> repositoryHistory = ThreadHelper.JoinableTaskFactory.Run(
            RepositoryHistoryManager.Locals.LoadRecentHistoryAsync);

        if (repositoryHistory.Count < 1)
        {
            return;
        }

        RecentRepoSplitterOptions options = RecentRepoSplitterOptions.FromAppSettings(
            caption => TextRenderer.MeasureText(caption, container.Font).Width);
        RecentRepositoriesMenuModel model = RecentRepositoryMenu.BuildRecent(repositoryHistory, options, _branchNameCache.GetCachedBranchName);

        foreach (RepoMenuEntry entry in model.Pinned)
        {
            AddRepositoryEntry(container, entry);
        }

        if (model.ShowSeparator)
        {
            container.DropDownItems.Add(new ToolStripSeparator());
        }

        foreach (RepoMenuEntry entry in model.Recent)
        {
            AddRepositoryEntry(container, entry);
        }
    }

    public void TriggerBranchNameCacheUpdate(bool onlyIfEmpty = false)
    {
        // OnLoad triggers with onlyIfEmpty: true, OnRevisionsLoaded with false; the portable
        // policy de-duplicates their race and defers to a surface that already filled the cache.
        if (!_updatePolicy.ShouldUpdate(onlyIfEmpty))
        {
            return;
        }

        _branchCacheUpdateTask = ThreadHelper.JoinableTaskFactory.RunAsync(UpdateBranchNameCacheAsync);

        return;

        async Task UpdateBranchNameCacheAsync()
        {
            CancellationToken cancellationToken = _branchCacheSequence.Next();
            IList<Repository> recentHistory = await RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
            IList<Repository> favouriteHistory = await RepositoryHistoryManager.Locals.LoadFavouriteHistoryAsync();

            string[] paths = [.. recentHistory
                .Concat(favouriteHistory)
                .Select(r => r.Path)
                .Distinct(StringComparer.InvariantCulture)];

            if (paths.Length > 0)
            {
                BranchNameCacheUpdater.UpdateBranchNames(paths, _branchNameCache, cancellationToken);
            }
        }
    }

    internal TestAccessor GetTestAccessor()
        => new(this);

    internal readonly struct TestAccessor(RepositoryHistoryUIService service)
    {
        internal void AddRepositoryEntry(ToolStripDropDownItem menuItemContainer, RepoMenuEntry entry)
            => service.AddRepositoryEntry(menuItemContainer, entry);

        internal void PopulateFavouriteRepositoriesMenu(ToolStripDropDownItem container, in IList<Repository> repositoryHistory)
            => service.PopulateFavouriteRepositoriesMenu(container, repositoryHistory);
    }
}
