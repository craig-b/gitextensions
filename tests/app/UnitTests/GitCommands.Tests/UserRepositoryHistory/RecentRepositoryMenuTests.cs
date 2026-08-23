using GitCommands.UserRepositoryHistory;

namespace GitCommandsTests.UserRepositoryHistory;
public class RecentRepositoryMenuTests
{
    // Paths deliberately OUTSIDE the user profile: PathUtil.GetDisplayPath (applied to every
    // caption by the splitter) "~"-collapses paths under the profile, which would make a
    // strategy-None caption differ from the raw path and defeat the tooltip assertions.
    private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\" : "/";
    private static readonly string _pinnedPath1 = Path.Combine(_root, "src", "menus", "pinned_repo_1");
    private static readonly string _pinnedPath2 = Path.Combine(_root, "src", "menus", "pinned_repo_2");
    private static readonly string _recentPath1 = Path.Combine(_root, "src", "menus", "recent_repo_1");
    private static readonly string _recentPath2 = Path.Combine(_root, "src", "menus", "recent_repo_2");

    private static RecentRepoSplitterOptions Options(
        GitCommands.ShorteningRecentRepoPathStrategy strategy,
        bool hideTopRepositoriesFromRecentList = false)
        => new(
            MaxTopRepositories: 0,
            hideTopRepositoriesFromRecentList,
            strategy,
            SortTopRepos: false,
            SortRecentRepos: false,
            RecentReposComboMinWidth: 0);

    private static string? NoBranchName(string path) => null;

    [TestCase(1, "&1")]
    [TestCase(9, "&9")]
    [TestCase(10, "1&0")]
    [TestCase(11, "11")]
    public void AcceleratorText_should_render_the_classic_menu_numbering(int number, string expected)
    {
        RecentRepositoryMenu.AcceleratorText(number).Should().Be(expected);
    }

    [Test]
    public void BuildRecent_should_continue_numbering_from_the_pinned_block_into_the_recent_block()
    {
        List<Repository> history =
        [
            new Repository(_pinnedPath1) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
            new Repository(_pinnedPath2) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
            new Repository(_recentPath1) { Anchor = Repository.RepositoryAnchor.None },
            new Repository(_recentPath2) { Anchor = Repository.RepositoryAnchor.None },
        ];

        RecentRepositoriesMenuModel model = RecentRepositoryMenu.BuildRecent(history, Options(GitCommands.ShorteningRecentRepoPathStrategy.None), NoBranchName);

        model.Pinned.Select(e => e.Number).Should().Equal(1, 2);
        model.Pinned.Select(e => e.Repo.Path).Should().Equal(_pinnedPath1, _pinnedPath2);
        model.Recent.Select(e => e.Number).Should().Equal(3, 4, 5, 6);
        model.Recent.Select(e => e.Repo.Path).Should().Equal(_pinnedPath1, _pinnedPath2, _recentPath1, _recentPath2);
        model.ShowSeparator.Should().BeTrue();
    }

    [Test]
    public void BuildRecent_should_not_show_a_separator_when_there_are_no_pinned_repos()
    {
        List<Repository> history =
        [
            new Repository(_recentPath1) { Anchor = Repository.RepositoryAnchor.None },
        ];

        RecentRepositoriesMenuModel model = RecentRepositoryMenu.BuildRecent(history, Options(GitCommands.ShorteningRecentRepoPathStrategy.None), NoBranchName);

        model.Pinned.Should().BeEmpty();
        model.Recent.Select(e => e.Number).Should().Equal(1);
        model.ShowSeparator.Should().BeFalse();
    }

    [Test]
    public void BuildRecent_should_not_show_a_separator_when_the_recent_block_is_empty()
    {
        List<Repository> history =
        [
            new Repository(_pinnedPath1) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
        ];

        RecentRepositoriesMenuModel model = RecentRepositoryMenu.BuildRecent(
            history,
            Options(GitCommands.ShorteningRecentRepoPathStrategy.None, hideTopRepositoriesFromRecentList: true),
            NoBranchName);

        model.Pinned.Should().ContainSingle();
        model.Recent.Should().BeEmpty();
        model.ShowSeparator.Should().BeFalse();
    }

    [Test]
    public void BuildRecent_should_not_add_a_tooltip_when_the_caption_is_the_full_path()
    {
        List<Repository> history =
        [
            new Repository(_recentPath1) { Anchor = Repository.RepositoryAnchor.None },
        ];

        RecentRepositoriesMenuModel model = RecentRepositoryMenu.BuildRecent(history, Options(GitCommands.ShorteningRecentRepoPathStrategy.None), NoBranchName);

        RepoMenuEntry entry = model.Recent.Should().ContainSingle().Which;
        entry.Caption.Should().Be(_recentPath1);
        entry.Tooltip.Should().BeNull();
    }

    [Test]
    public void BuildRecent_should_add_the_full_path_as_tooltip_when_the_caption_hides_it()
    {
        List<Repository> history =
        [
            new Repository(_recentPath1) { Anchor = Repository.RepositoryAnchor.None },
        ];

        RecentRepositoriesMenuModel model = RecentRepositoryMenu.BuildRecent(history, Options(GitCommands.ShorteningRecentRepoPathStrategy.MostSignDir), NoBranchName);

        RepoMenuEntry entry = model.Recent.Should().ContainSingle().Which;
        entry.Caption.Should().Be("recent_repo_1");
        entry.Tooltip.Should().Be(_recentPath1);
    }

    [Test]
    public void BuildRecent_should_pin_exactly_the_anchored_repos()
    {
        List<Repository> history =
        [
            new Repository(_pinnedPath1) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
            new Repository(_recentPath1) { Anchor = Repository.RepositoryAnchor.None },
            new Repository(_recentPath2) { Anchor = Repository.RepositoryAnchor.AnchoredInRecent },
        ];

        RecentRepositoriesMenuModel model = RecentRepositoryMenu.BuildRecent(history, Options(GitCommands.ShorteningRecentRepoPathStrategy.None), NoBranchName);

        model.Pinned.Select(e => e.IsPinned).Should().Equal(true);
        model.Recent.Select(e => e.IsPinned).Should().Equal(true, false, true);
    }

    [Test]
    public void BuildRecent_should_take_branch_names_from_the_lookup()
    {
        List<Repository> history =
        [
            new Repository(_recentPath1) { Anchor = Repository.RepositoryAnchor.None },
            new Repository(_recentPath2) { Anchor = Repository.RepositoryAnchor.None },
        ];

        RecentRepositoriesMenuModel model = RecentRepositoryMenu.BuildRecent(
            history,
            Options(GitCommands.ShorteningRecentRepoPathStrategy.None),
            path => path == _recentPath1 ? "feature/menu" : null);

        model.Recent[0].BranchName.Should().Be("feature/menu");
        model.Recent[1].BranchName.Should().BeNull();
    }

    [Test]
    public void BuildFavourites_should_order_groups_by_category_with_null_first()
    {
        List<Repository> history =
        [
            new Repository(_recentPath1) { Category = "Work" },
            new Repository(_recentPath2) { Category = "Home" },
            new Repository(_pinnedPath1),
        ];

        IReadOnlyList<FavouriteCategoryGroup> groups = RecentRepositoryMenu.BuildFavourites(history, Options(GitCommands.ShorteningRecentRepoPathStrategy.None), NoBranchName);

        groups.Select(g => g.Category).Should().Equal(new string?[] { null, "Home", "Work" });
        groups[0].Entries.Select(e => e.Repo.Path).Should().Equal(_pinnedPath1);
    }

    [Test]
    public void BuildFavourites_should_restart_numbering_per_category_and_never_pin()
    {
        List<Repository> history =
        [
            new Repository(_pinnedPath1) { Anchor = Repository.RepositoryAnchor.AnchoredInTop, Category = "Work" },
            new Repository(_recentPath1) { Category = "Work" },
            new Repository(_recentPath2) { Category = "Home" },
        ];

        IReadOnlyList<FavouriteCategoryGroup> groups = RecentRepositoryMenu.BuildFavourites(history, Options(GitCommands.ShorteningRecentRepoPathStrategy.None), NoBranchName);

        groups.Should().HaveCount(2);
        groups[0].Category.Should().Be("Home");
        groups[0].Entries.Select(e => e.Number).Should().Equal(1);
        groups[1].Category.Should().Be("Work");
        groups[1].Entries.Select(e => e.Number).Should().Equal(1, 2);

        // The favourites menu never shows pin icons, even for anchored repos.
        groups.SelectMany(g => g.Entries).Should().OnlyContain(e => !e.IsPinned);
    }

    [Test]
    public void BuildFavourites_should_not_duplicate_repos_present_in_both_splitter_outputs()
    {
        // With HideTopRepositoriesFromRecentList false, the splitter emits the anchored repo in
        // both the pinned and the recent list; the favourites builder set-unions them.
        List<Repository> history =
        [
            new Repository(_pinnedPath1) { Anchor = Repository.RepositoryAnchor.AnchoredInTop, Category = "Work" },
        ];

        IReadOnlyList<FavouriteCategoryGroup> groups = RecentRepositoryMenu.BuildFavourites(
            history,
            Options(GitCommands.ShorteningRecentRepoPathStrategy.None, hideTopRepositoriesFromRecentList: false),
            NoBranchName);

        FavouriteCategoryGroup group = groups.Should().ContainSingle().Which;
        RepoMenuEntry entry = group.Entries.Should().ContainSingle().Which;
        entry.Repo.Path.Should().Be(_pinnedPath1);
    }
}
