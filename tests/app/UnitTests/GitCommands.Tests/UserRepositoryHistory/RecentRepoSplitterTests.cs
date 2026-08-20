using GitCommands.UserRepositoryHistory;

namespace GitCommandsTests.UserRepositoryHistory;
public class RecentRepoSplitterTests
{
    // The old parameterless splitter constructor read these from AppSettings; the tests ran on
    // the settings defaults (MaxTopRepositories = 0 etc.), which this helper reproduces.
    private static RecentRepoSplitterOptions Options(
        GitCommands.ShorteningRecentRepoPathStrategy strategy,
        bool sortTopRepos = false,
        bool sortRecentRepos = false,
        bool hideTopRepositoriesFromRecentList = false)
        => new(
            MaxTopRepositories: 0,
            hideTopRepositoriesFromRecentList,
            strategy,
            sortTopRepos,
            sortRecentRepos,
            RecentReposComboMinWidth: 0);

    private const string _relativeLongRepoPath = @"this\is\a\very_very_very_very_very_very_very\long\repo_path";
    private static readonly string repoPathInUserFolder = Path.Combine(Path.GetTempPath(), _relativeLongRepoPath);

    // "C:\" kept byte-identical on Windows; the drive letter itself isn't what
    // AddToOrderedSignDir's MostSignDir shortening cares about (it only extracts DirectoryInfo's
    // syntactic Name/Parent, no disk access needed), so a plain rooted POSIX path is the same
    // intent off Windows. Path.Combine(...) + Path.DirectorySeparatorChar in place of the
    // hardcoded "C:\this\is\a\...\" backslash join is the same "path\\sub" -> Path.Combine
    // substitution used elsewhere.
    private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\" : "/";
    private static readonly string repoAnchoredInTopPath1 = Path.Combine(_root, "this", "is", "a", "repo_anchored_in_top_path1") + Path.DirectorySeparatorChar;
    private static readonly string repoAnchoredInTopPath2 = Path.Combine(_root, "this", "is", "a", "repo_anchored_in_top_path2") + Path.DirectorySeparatorChar;
    private static readonly string repoAnchoredInRecentPath = Path.Combine(_root, "this", "is", "a", "repo_anchored_in_recent_path") + Path.DirectorySeparatorChar;
    private static readonly string repoNotAnchoredPath = Path.Combine(_root, "this", "is", "a", "repo_not_anchored_path") + Path.DirectorySeparatorChar;

    #region Shortening strategy
    [Test]
    public void SplitRecentRepos_Should_use_most_significant_folder_as_caption()
    {
        List<Repository> history =
        [
            new Repository(repoAnchoredInTopPath1) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
        ];

        RecentRepoSplitter sut = new(Options(GitCommands.ShorteningRecentRepoPathStrategy.MostSignDir));
        List<RecentRepoInfo> topRepoList = [];
        List<RecentRepoInfo> recentRepoList = [];

        sut.SplitRecentRepos(history, topRepoList, recentRepoList);

        topRepoList.Should().ContainSingle();
        topRepoList[0].Caption.Should().Be("repo_anchored_in_top_path1");
        recentRepoList.Should().ContainSingle();
    }

    [Test]
    public void SplitRecentRepos_Should_not_shorten_as_caption()
    {
        List<Repository> history =
        [
            new Repository(repoAnchoredInTopPath1) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
        ];

        RecentRepoSplitter sut = new(Options(GitCommands.ShorteningRecentRepoPathStrategy.None));
        List<RecentRepoInfo> topRepoList = [];
        List<RecentRepoInfo> recentRepoList = [];

        sut.SplitRecentRepos(history, topRepoList, recentRepoList);

        topRepoList.Should().ContainSingle();
        topRepoList[0].Caption.Should().Be(repoAnchoredInTopPath1);
        recentRepoList.Should().ContainSingle();
    }

    // PathUtil.GetDisplayPath's "~\..." substitution (applied to every caption regardless of
    // ShorteningStrategy) fires when the path is under the user's profile directory. On Windows
    // Path.GetTempPath() genuinely lives under %USERPROFILE%\AppData\Local\Temp, so
    // repoPathInUserFolder qualifies and picks up the literal "AppData" segment under test here;
    // off Windows the temp directory (/tmp) isn't nested under the user's home at all, so this
    // is a real environmental fact being tested, not a hardcoded literal to translate.
    [Platform(Include = "Win")]
    [Test]
    public void SplitRecentRepos_Should_not_shorten_but_handle_user_folder_as_caption()
    {
        List<Repository> history =
        [
            new Repository(repoPathInUserFolder) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
        ];

        RecentRepoSplitter sut = new(Options(GitCommands.ShorteningRecentRepoPathStrategy.None));
        List<RecentRepoInfo> topRepoList = [];
        List<RecentRepoInfo> recentRepoList = [];

        sut.SplitRecentRepos(history, topRepoList, recentRepoList);

        topRepoList.Should().ContainSingle();
        topRepoList[0].Caption.Should().StartWith(@"~\AppData").And.EndWith(_relativeLongRepoPath);
        recentRepoList.Should().ContainSingle();
    }

    // Same "temp lives under the user profile on Windows only" reasoning as the test above.
    [Platform(Include = "Win")]
    [Test]
    public void SplitRecentRepos_Should_display_middle_dots_in_caption()
    {
        // Warning: Able to shorten only an existing folder path
        Directory.CreateDirectory(repoPathInUserFolder);

        List<Repository> history =
        [
            new Repository(repoPathInUserFolder) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
        ];

        RecentRepoSplitter sut = new(Options(GitCommands.ShorteningRecentRepoPathStrategy.MiddleDots));
        List<RecentRepoInfo> topRepoList = [];
        List<RecentRepoInfo> recentRepoList = [];

        sut.SplitRecentRepos(history, topRepoList, recentRepoList);

        topRepoList.Should().ContainSingle();
        topRepoList[0].Caption.Should().Be(@"~\AppData\..\long\repo_path");
        recentRepoList.Should().ContainSingle();
    }

    // No [Platform] gate: unlike the "~"-substitution tests above, this one only needs the repo
    // path to exist on disk (AddToOrderedMiddleDots shortens existing dirs only, comparing
    // DirectoryInfo.FullName against the native path - fine for a temp path on any OS).
    [Test]
    public void SplitRecentRepos_Should_shorten_with_the_char_budget_when_no_width_measure_is_supplied()
    {
        // Warning: Able to shorten only an existing folder path
        string longRepoPath = Path.Combine(Path.GetTempPath(), "ge_char_budget", "very_long_middle_segment_for_char_budget_fallback_shortening", "repo");
        Directory.CreateDirectory(longRepoPath);

        List<Repository> history =
        [
            new Repository(longRepoPath) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
        ];

        // MiddleDots with a fixed combo width and MeasureCaptionWidth left at null: the
        // CharBudgetMeasure fallback drives the skip loop. (The old default measured every
        // caption as 0 pixels, so the loop exited on the first pass and the caption silently
        // stayed the full path.)
        RecentRepoSplitterOptions options = Options(GitCommands.ShorteningRecentRepoPathStrategy.MiddleDots) with { RecentReposComboMinWidth = 200 };
        RecentRepoSplitter sut = new(options);
        List<RecentRepoInfo> topRepoList = [];
        List<RecentRepoInfo> recentRepoList = [];

        sut.SplitRecentRepos(history, topRepoList, recentRepoList);

        topRepoList.Should().ContainSingle();
        topRepoList[0].Caption.Should().Contain("..");
        topRepoList[0].Caption!.Length.Should().BeLessThan(longRepoPath.Length);
    }
    #endregion

    #region Split repositories
    [Test]
    public void SplitRecentRepos_Should_split_depending_anchor()
    {
        List<Repository> history =
        [
            new Repository(repoAnchoredInTopPath1) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
            new Repository(repoAnchoredInTopPath2) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
            new Repository(repoAnchoredInRecentPath) { Anchor = Repository.RepositoryAnchor.AnchoredInRecent },
            new Repository(repoNotAnchoredPath) { Anchor = Repository.RepositoryAnchor.None },
        ];

        RecentRepoSplitter sut = new(Options(GitCommands.ShorteningRecentRepoPathStrategy.MostSignDir, sortTopRepos: false, sortRecentRepos: false));
        List<RecentRepoInfo> topRepoList = [];
        List<RecentRepoInfo> recentRepoList = [];

        sut.SplitRecentRepos(history, topRepoList, recentRepoList);

        topRepoList.Should().HaveCount(2);
        topRepoList[0].Caption.Should().Be("repo_anchored_in_top_path1");
        topRepoList[1].Caption.Should().Be("repo_anchored_in_top_path2");
        recentRepoList.Should().HaveCount(4);
        recentRepoList[0].Caption.Should().Be("repo_anchored_in_top_path1");
        recentRepoList[1].Caption.Should().Be("repo_anchored_in_top_path2");
        recentRepoList[2].Caption.Should().Be("repo_anchored_in_recent_path");
        recentRepoList[3].Caption.Should().Be("repo_not_anchored_path");
    }

    [Test]
    public void SplitRecentRepos_Should_split_depending_anchor_and_sort_alphabetically()
    {
        List<Repository> history =
        [
            // Unsorted!
            new Repository(repoNotAnchoredPath) { Anchor = Repository.RepositoryAnchor.None },
            new Repository(repoAnchoredInRecentPath) { Anchor = Repository.RepositoryAnchor.AnchoredInRecent },
            new Repository(repoAnchoredInTopPath2) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
            new Repository(repoAnchoredInTopPath1) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
        ];

        RecentRepoSplitter sut = new(Options(GitCommands.ShorteningRecentRepoPathStrategy.MostSignDir, sortTopRepos: true, sortRecentRepos: true));
        List<RecentRepoInfo> topRepoList = [];
        List<RecentRepoInfo> recentRepoList = [];

        sut.SplitRecentRepos(history, topRepoList, recentRepoList);

        topRepoList.Should().HaveCount(2);
        topRepoList[0].Caption.Should().Be("repo_anchored_in_top_path1");
        topRepoList[1].Caption.Should().Be("repo_anchored_in_top_path2");
        recentRepoList.Should().HaveCount(4);
        recentRepoList[0].Caption.Should().Be("repo_anchored_in_recent_path");
        recentRepoList[1].Caption.Should().Be("repo_anchored_in_top_path1");
        recentRepoList[2].Caption.Should().Be("repo_anchored_in_top_path2");
        recentRepoList[3].Caption.Should().Be("repo_not_anchored_path");
    }

    [Test]
    public void SplitRecentRepos_Should_split_depending_anchor_and_sort_alphabetically_Hiding_Top_Repo_In_Recent_list()
    {
        List<Repository> history =
        [
            // Unsorted!
            new Repository(repoNotAnchoredPath) { Anchor = Repository.RepositoryAnchor.None },
            new Repository(repoAnchoredInRecentPath) { Anchor = Repository.RepositoryAnchor.AnchoredInRecent },
            new Repository(repoAnchoredInTopPath2) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
            new Repository(repoAnchoredInTopPath1) { Anchor = Repository.RepositoryAnchor.AnchoredInTop },
        ];

        RecentRepoSplitter sut = new(Options(GitCommands.ShorteningRecentRepoPathStrategy.MostSignDir, sortTopRepos: true, sortRecentRepos: true, hideTopRepositoriesFromRecentList: true));
        List<RecentRepoInfo> topRepoList = [];
        List<RecentRepoInfo> recentRepoList = [];

        sut.SplitRecentRepos(history, topRepoList, recentRepoList);

        topRepoList.Should().HaveCount(2);
        topRepoList[0].Caption.Should().Be("repo_anchored_in_top_path1");
        topRepoList[1].Caption.Should().Be("repo_anchored_in_top_path2");
        recentRepoList.Should().HaveCount(2);
        recentRepoList[0].Caption.Should().Be("repo_anchored_in_recent_path");
        recentRepoList[1].Caption.Should().Be("repo_not_anchored_path");
    }
    #endregion
}
