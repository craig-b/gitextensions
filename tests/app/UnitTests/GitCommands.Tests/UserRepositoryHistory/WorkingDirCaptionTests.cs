using GitCommands.UserRepositoryHistory;

namespace GitCommandsTests.UserRepositoryHistory;
public class WorkingDirCaptionTests
{
    // Paths OUTSIDE the user profile: both Compute outputs go through PathUtil.GetDisplayPath,
    // which is the identity for paths the "~"-substitution cannot reach.
    private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\" : "/";
    private static readonly string _repoInHistoryPath = Path.Combine(_root, "src", "captions", "repo_in_history");
    private static readonly string _otherRepoPath = Path.Combine(_root, "src", "captions", "other_repo");

    private static RecentRepoSplitterOptions Options(GitCommands.ShorteningRecentRepoPathStrategy strategy)
        => new(
            MaxTopRepositories: 0,
            HideTopRepositoriesFromRecentList: false,
            strategy,
            SortTopRepos: false,
            SortRecentRepos: false,
            RecentReposComboMinWidth: 0);

    [Test]
    public void Compute_should_return_the_menu_caption_for_a_repo_in_history()
    {
        List<Repository> history =
        [
            new Repository(_repoInHistoryPath),
            new Repository(_otherRepoPath),
        ];

        string caption = WorkingDirCaption.Compute(_repoInHistoryPath, history, Options(GitCommands.ShorteningRecentRepoPathStrategy.MostSignDir));

        caption.Should().Be("repo_in_history");
    }

    [Test]
    public void Compute_should_match_the_history_entry_case_insensitively()
    {
        List<Repository> history =
        [
            new Repository(_repoInHistoryPath),
            new Repository(_otherRepoPath),
        ];

        string caption = WorkingDirCaption.Compute(_repoInHistoryPath.ToUpperInvariant(), history, Options(GitCommands.ShorteningRecentRepoPathStrategy.MostSignDir));

        caption.Should().Be("repo_in_history");
    }

    [Test]
    public void Compute_should_return_the_raw_path_for_a_repo_not_in_history()
    {
        List<Repository> history =
        [
            new Repository(_repoInHistoryPath),
        ];

        string missingPath = Path.Combine(_root, "src", "captions", "not_in_history");

        string caption = WorkingDirCaption.Compute(missingPath, history, Options(GitCommands.ShorteningRecentRepoPathStrategy.MostSignDir));

        caption.Should().Be(missingPath);
    }
}
