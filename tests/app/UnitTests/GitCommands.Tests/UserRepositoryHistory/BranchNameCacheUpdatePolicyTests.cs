using GitCommands.UserRepositoryHistory;
using GitUI;

namespace GitCommandsTests.UserRepositoryHistory;
public class BranchNameCacheUpdatePolicyTests
{
    private static readonly string _repoPath = Path.Combine(OperatingSystem.IsWindows() ? @"C:\" : "/", "src", "branch_cache", "repo");

    [Test]
    public void ShouldUpdate_should_start_once_for_an_empty_cache_and_keep_the_started_flag()
    {
        RepositoryCurrentBranchNameCache cache = new(new StubBranchNameProvider());
        BranchNameCacheUpdatePolicy policy = new(cache);

        // OnLoad fires first: nothing cached, nothing running - start the update.
        policy.ShouldUpdate(onlyIfEmpty: true).Should().BeTrue();

        // OnRevisionsLoaded races in while the cache is still empty: the started flag sticks.
        // (The old inline logic wrote a sentinel path with an empty branch name, which the cache
        // treats as a removal, so the sentinel never landed and this trigger started a second
        // update against the still-empty cache.)
        policy.ShouldUpdate(onlyIfEmpty: false).Should().BeFalse();

        // After the initial pair, OnRevisionsLoaded refreshes.
        policy.ShouldUpdate(onlyIfEmpty: false).Should().BeTrue();
    }

    [Test]
    public void ShouldUpdate_should_defer_to_a_surface_that_already_filled_the_cache()
    {
        RepositoryCurrentBranchNameCache cache = new(new StubBranchNameProvider());
        cache.UpdateCache(_repoPath, "main");

        BranchNameCacheUpdatePolicy policy = new(cache);

        // The initial load pair is fully suppressed: the dashboard already warmed the cache.
        policy.ShouldUpdate(onlyIfEmpty: true).Should().BeFalse();
        policy.ShouldUpdate(onlyIfEmpty: false).Should().BeFalse();

        // The next OnRevisionsLoaded refreshes.
        policy.ShouldUpdate(onlyIfEmpty: false).Should().BeTrue();
    }

    [Test]
    public void ShouldUpdate_should_refresh_only_on_revisions_loaded_after_the_first_load()
    {
        RepositoryCurrentBranchNameCache cache = new(new StubBranchNameProvider());
        BranchNameCacheUpdatePolicy policy = new(cache);

        // The initial load pair.
        policy.ShouldUpdate(onlyIfEmpty: true).Should().BeTrue();
        policy.ShouldUpdate(onlyIfEmpty: false).Should().BeFalse();

        // Steady state: OnLoad (onlyIfEmpty: true) never refreshes, OnRevisionsLoaded always does.
        policy.ShouldUpdate(onlyIfEmpty: true).Should().BeFalse();
        policy.ShouldUpdate(onlyIfEmpty: false).Should().BeTrue();
        policy.ShouldUpdate(onlyIfEmpty: true).Should().BeFalse();
        policy.ShouldUpdate(onlyIfEmpty: false).Should().BeTrue();
    }

    private sealed class StubBranchNameProvider : IRepositoryCurrentBranchNameProvider
    {
        public string GetCurrentBranchName(string repositoryPath) => "main";
    }
}
