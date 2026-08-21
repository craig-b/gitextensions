using GitCommands.Dashboard;
using GitCommands.UserRepositoryHistory;

namespace GitCommandsTests.Dashboard;
public class DashboardListTests
{
    // Same OS-rooted scheme as RecentRepoSplitterTests: drive-rooted on Windows, POSIX-rooted
    // elsewhere, so the syntactic DirectoryInfo work in RecentRepoInfo behaves identically.
    private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\" : "/";

    private static string RepoPath(string name) => Path.Combine(_root, "repos", name);

    private static RecentRepoInfo Info(string name, string? category = null, string? caption = null)
    {
        RecentRepoInfo info = new(new Repository(RepoPath(name)) { Category = category }, topRepo: false, anchored: false);
        info.Caption = caption;
        return info;
    }

    // Splitter tests' Options helper reproduced: the settings defaults (MaxTopRepositories = 0 etc.).
    private static RecentRepoSplitterOptions Options(bool hideTopRepositoriesFromRecentList = false)
        => new(
            MaxTopRepositories: 0,
            hideTopRepositoriesFromRecentList,
            GitCommands.ShorteningRecentRepoPathStrategy.None,
            SortTopRepos: false,
            SortRecentRepos: false,
            RecentReposComboMinWidth: 0);

    #region Filter
    [Test]
    public void Filter_should_return_the_same_list_instance_for_an_empty_pattern()
    {
        List<Repository> repositories =
        [
            new Repository(RepoPath("alpha")),
            new Repository(RepoPath("beta")),
        ];

        IList<Repository> filtered = DashboardList.Filter(repositories, "");

        filtered.Should().BeSameAs(repositories);
    }

    [Test]
    public void Filter_should_match_path_substring_case_insensitively()
    {
        Repository match = new(RepoPath("GitExtensions"));
        List<Repository> repositories =
        [
            match,
            new Repository(RepoPath("other")),
        ];

        IList<Repository> filtered = DashboardList.Filter(repositories, "gitext");

        filtered.Should().ContainSingle().Which.Should().BeSameAs(match);
    }

    [Test]
    public void Filter_should_match_category_when_path_does_not_match()
    {
        // The historical filter searched the path only, despite the "Search repositories"
        // placeholder; matching the category too is a deliberate fix.
        Repository match = new(RepoPath("alpha")) { Category = "Tools" };
        List<Repository> repositories =
        [
            match,
            new Repository(RepoPath("beta")) { Category = "Work" },
        ];

        IList<Repository> filtered = DashboardList.Filter(repositories, "tool");

        filtered.Should().ContainSingle().Which.Should().BeSameAs(match);
    }

    [Test]
    public void Filter_should_return_empty_when_nothing_matches()
    {
        List<Repository> repositories =
        [
            new Repository(RepoPath("alpha")) { Category = "Tools" },
            new Repository(RepoPath("beta")),
        ];

        IList<Repository> filtered = DashboardList.Filter(repositories, "no_such_repo");

        filtered.Should().BeEmpty();
    }
    #endregion

    #region SplitAndMerge
    [Test]
    public void SplitAndMerge_should_list_a_repo_appearing_in_both_blocks_once_with_the_top_block_first()
    {
        Repository anchoredInTop = new(RepoPath("anchored_in_top")) { Anchor = Repository.RepositoryAnchor.AnchoredInTop };
        Repository notAnchored = new(RepoPath("not_anchored")) { Anchor = Repository.RepositoryAnchor.None };

        // The not-anchored repo first: with HideTopRepositoriesFromRecentList = false the recent
        // block is [notAnchored, anchoredInTop] while the top block is [anchoredInTop], so the
        // anchored repo leading the merged list proves the top block goes first, and the total
        // count proves the reference-identity union listed it once.
        List<Repository> history = [notAnchored, anchoredInTop];

        IReadOnlyList<RecentRepoInfo> merged = DashboardList.SplitAndMerge(history, Options(hideTopRepositoriesFromRecentList: false));

        merged.Should().HaveCount(2);
        merged[0].Repo.Should().BeSameAs(anchoredInTop);
        merged[1].Repo.Should().BeSameAs(notAnchored);
        merged.Count(info => ReferenceEquals(info.Repo, anchoredInTop)).Should().Be(1);
    }
    #endregion

    #region CategoryHeaders
    [Test]
    public void CategoryHeaders_should_return_distinct_ordered_categories_over_both_lists_dropping_blanks()
    {
        List<RecentRepoInfo> recent =
        [
            Info("r1", category: "beta"),
            Info("r2", category: null),
            Info("r3", category: "   "),
        ];
        List<RecentRepoInfo> favourites =
        [
            Info("f1", category: "beta"),
            Info("f2", category: "alpha"),
            Info("f3", category: ""),
        ];

        IReadOnlyList<string> headers = DashboardList.CategoryHeaders(recent, favourites);

        headers.Should().Equal("alpha", "beta");
    }
    #endregion

    #region Groups
    [Test]
    public void Groups_should_put_the_recent_group_first_with_a_null_category_and_the_recent_items_in_order()
    {
        List<RecentRepoInfo> recent =
        [
            Info("r1"),
            Info("r2"),
        ];

        IReadOnlyList<DashboardRepositoryGroup> groups = DashboardList.Groups(recent, favouriteRepositories: []);

        groups.Should().ContainSingle();
        groups[0].IsRecent.Should().BeTrue();
        groups[0].Category.Should().BeNull();
        groups[0].Items.Select(item => item.Repo).Should().Equal(recent[0].Repo, recent[1].Repo);
        groups[0].Items.Should().OnlyContain(item => !item.IsFavourite);
    }

    [Test]
    public void Groups_should_build_one_group_per_category_containing_only_the_matching_favourites()
    {
        RecentRepoInfo alpha1 = Info("f1", category: "alpha");
        RecentRepoInfo beta = Info("f2", category: "beta");
        RecentRepoInfo alpha2 = Info("f3", category: "alpha");

        IReadOnlyList<DashboardRepositoryGroup> groups = DashboardList.Groups(recentRepositories: [], [alpha1, beta, alpha2]);

        groups.Should().HaveCount(3);
        groups[1].Category.Should().Be("alpha");
        groups[1].IsRecent.Should().BeFalse();
        groups[1].Items.Select(item => item.Repo).Should().Equal(alpha1.Repo, alpha2.Repo);
        groups[1].Items.Should().OnlyContain(item => item.IsFavourite);
        groups[2].Category.Should().Be("beta");
        groups[2].Items.Select(item => item.Repo).Should().Equal(beta.Repo);
    }

    [Test]
    public void Groups_should_place_a_favourite_with_a_blank_category_in_no_category_group()
    {
        // Documented rule: a favourite IS a repository with a category, so a blank category
        // produces no header and the repo lands nowhere.
        List<RecentRepoInfo> favourites =
        [
            Info("f1", category: "alpha"),
            Info("f2", category: "   "),
        ];

        IReadOnlyList<DashboardRepositoryGroup> groups = DashboardList.Groups(recentRepositories: [], favourites);

        groups.Should().HaveCount(2);
        groups.Skip(1).SelectMany(group => group.Items).Select(item => item.Repo)
            .Should().Equal(favourites[0].Repo);
    }

    [Test]
    public void Groups_should_fall_back_to_the_repo_path_when_the_splitter_left_the_caption_null()
    {
        RecentRepoInfo withCaption = Info("r1", caption: "short_caption");
        RecentRepoInfo withoutCaption = Info("r2", caption: null);

        IReadOnlyList<DashboardRepositoryGroup> groups = DashboardList.Groups([withCaption, withoutCaption], favouriteRepositories: []);

        groups[0].Items.Select(item => item.Caption).Should().Equal("short_caption", withoutCaption.Repo.Path);
    }
    #endregion
}
