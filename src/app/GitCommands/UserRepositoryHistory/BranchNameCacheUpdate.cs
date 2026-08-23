using GitUI;

namespace GitCommands.UserRepositoryHistory;

/// <summary>
///  Decides whether a branch-name cache update should start, de-duplicating the OnLoad vs
///  OnRevisionsLoaded race (OnLoad triggers with <c>onlyIfEmpty: true</c>, OnRevisionsLoaded with
///  <c>false</c>) and deferring to another surface (the dashboard) that already filled the cache.
/// </summary>
/// <remarks>
///  The old inline logic tried to mark "an update has started" by writing a sentinel path into the
///  cache — but it wrote it with an empty branch name, which the cache treats as a removal, so the
///  sentinel never landed and every trigger against a still-empty cache started another update.
///  The started-flag lives here now, where it can actually stick.
/// </remarks>
public sealed class BranchNameCacheUpdatePolicy(IRepositoryCurrentBranchNameCache cache)
{
    private bool _updateStarted;
    private bool _firstLoad = true;

    public bool ShouldUpdate(bool onlyIfEmpty)
    {
        if (cache.IsEmpty && !_updateStarted)
        {
            // The first trigger while nothing is cached and nothing is running: update.
            _updateStarted = true;
            return true;
        }

        if (_firstLoad)
        {
            // The cache exists (another surface filled it) or an update is already running:
            // suppress the second trigger of the initial load pair.
            _firstLoad = onlyIfEmpty;
            return false;
        }

        // After the initial load: OnRevisionsLoaded refreshes, OnLoad does not.
        return !onlyIfEmpty;
    }
}

/// <summary>Fetches current branch names for a set of repositories with bounded parallelism.</summary>
public static class BranchNameCacheUpdater
{
    private const int MaxBranchNameFetchParallelism = 4;

    public static void UpdateBranchNames(IReadOnlyList<string> repositoryPaths, IRepositoryCurrentBranchNameCache cache, CancellationToken cancellationToken)
    {
        repositoryPaths
            .AsParallel()
            .WithCancellation(cancellationToken)
            .WithDegreeOfParallelism(Math.Min(MaxBranchNameFetchParallelism, Math.Max(1, Environment.ProcessorCount / 2)))
            .ForAll(path => _ = cache.GetUpdatedBranchName(path));
    }
}
