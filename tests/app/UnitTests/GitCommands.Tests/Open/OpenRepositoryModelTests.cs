using GitCommands.Open;

namespace GitCommandsTests.Open;

public sealed class OpenRepositoryModelTests
{
    // OS-rooted paths: DirectoryInfo/Path.GetFullPath based logic must run on both Windows and Linux.
    private static readonly string _root = Path.GetFullPath(Path.DirectorySeparatorChar.ToString());
    private static readonly char _sep = Path.DirectorySeparatorChar;

    private static string Rooted(params string[] parts) => parts.Aggregate(_root, Path.Combine);

    [Test]
    public void CandidateDirectories_orders_clone_destination_then_parent_then_history()
    {
        string cloneDest = Rooted("dev", "clones");
        string currentWorkingDir = Rooted("repos", "current");
        string historyA = Rooted("hist", "a");
        string historyB = Rooted("hist", "b") + _sep;

        IReadOnlyList<string> directories = OpenRepositoryModel.CandidateDirectories(
            cloneDest,
            currentWorkingDir,
            [historyA, historyB],
            recentWorkingDir: Rooted("recent"),
            homeDir: Rooted("home"));

        directories.Should().Equal(
            cloneDest + _sep,          // default clone destination, trailing separator added
            Rooted("repos") + _sep,    // parent of the current working dir
            historyA,                  // history entries verbatim
            historyB);
    }

    [Test]
    public void CandidateDirectories_falls_back_to_recent_and_home_only_when_everything_else_is_empty()
    {
        IReadOnlyList<string> directories = OpenRepositoryModel.CandidateDirectories(
            defaultCloneDestinationPath: null,
            currentWorkingDir: null,
            historyPaths: [],
            recentWorkingDir: Rooted("recent"),
            homeDir: Rooted("home"));

        directories.Should().Equal(Rooted("recent") + _sep, Rooted("home") + _sep);
    }

    [Test]
    public void CandidateDirectories_suppresses_recent_and_home_when_any_other_entry_exists()
    {
        string historyA = Rooted("hist", "a");

        IReadOnlyList<string> directories = OpenRepositoryModel.CandidateDirectories(
            defaultCloneDestinationPath: null,
            currentWorkingDir: null,
            historyPaths: [historyA],
            recentWorkingDir: Rooted("recent"),
            homeDir: Rooted("home"));

        directories.Should().Equal(historyA);
    }

    [Test]
    public void CandidateDirectories_is_empty_when_no_input_is_usable()
    {
        OpenRepositoryModel.CandidateDirectories(null, null, [], null, null).Should().BeEmpty();
        OpenRepositoryModel.CandidateDirectories("   ", "", [], " ", "\t").Should().BeEmpty();
    }

    [Test]
    public void CandidateDirectories_applies_Distinct()
    {
        string dup = Rooted("dup") + _sep;

        IReadOnlyList<string> directories = OpenRepositoryModel.CandidateDirectories(
            defaultCloneDestinationPath: Rooted("dup"), // normalizes to dup with trailing separator
            currentWorkingDir: null,
            historyPaths: [dup, dup],
            recentWorkingDir: null,
            homeDir: null);

        directories.Should().Equal(dup);
    }

    [Test]
    public void CandidateDirectories_skips_whitespace_clone_destination_and_rootlevel_current_dir()
    {
        string historyA = Rooted("hist", "a");

        IReadOnlyList<string> directories = OpenRepositoryModel.CandidateDirectories(
            defaultCloneDestinationPath: "   ",
            currentWorkingDir: _root, // the filesystem root has no parent
            historyPaths: [historyA],
            recentWorkingDir: null,
            homeDir: null);

        directories.Should().Equal(historyA);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void TryGetOpenablePath_rejects_null_and_whitespace(string? path)
    {
        OpenRepositoryModel.TryGetOpenablePath(path, directoryExists: _ => true, isValidGitWorkingDir: _ => true)
            .Should().BeNull();
    }

    [Test]
    public void TryGetOpenablePath_trims_and_normalizes_a_valid_path()
    {
        string path = Rooted("repos", "mine");
        string? seenByExists = null;
        string? seenByValid = null;

        string? result = OpenRepositoryModel.TryGetOpenablePath(
            $"  {path}  ",
            directoryExists: p => { seenByExists = p; return true; },
            isValidGitWorkingDir: p => { seenByValid = p; return true; });

        result.Should().Be(path + _sep);
        seenByExists.Should().Be(path, because: "the existence check runs on the trimmed input");
        seenByValid.Should().Be(path + _sep, because: "the repository check runs on the normalized directory");
    }

    [Test]
    public void TryGetOpenablePath_rejects_a_missing_directory_without_asking_git()
    {
        bool validityChecked = false;

        OpenRepositoryModel.TryGetOpenablePath(
                Rooted("gone"),
                directoryExists: _ => false,
                isValidGitWorkingDir: _ => { validityChecked = true; return true; })
            .Should().BeNull();

        validityChecked.Should().BeFalse();
    }

    [Test]
    public void TryGetOpenablePath_rejects_a_directory_that_is_not_a_repository()
    {
        OpenRepositoryModel.TryGetOpenablePath(
                Rooted("not", "a", "repo"),
                directoryExists: _ => true,
                isValidGitWorkingDir: _ => false)
            .Should().BeNull();
    }

    [Test]
    public void TryGetOpenablePath_keeps_an_existing_trailing_separator()
    {
        string path = Rooted("repos", "mine") + _sep;

        OpenRepositoryModel.TryGetOpenablePath(path, _ => true, _ => true).Should().Be(path);
    }

    [Test]
    public void ParentOf_returns_the_parent_without_a_trailing_separator()
    {
        OpenRepositoryModel.ParentOf(Rooted("a", "b", "c")).Should().Be(Rooted("a", "b"));
        OpenRepositoryModel.ParentOf(Rooted("a", "b", "c") + _sep).Should().Be(Rooted("a", "b"));
    }

    [Test]
    public void ParentOf_is_null_at_the_root_and_for_garbage()
    {
        OpenRepositoryModel.ParentOf(_root).Should().BeNull();
        OpenRepositoryModel.ParentOf(null).Should().BeNull();
        OpenRepositoryModel.ParentOf("   ").Should().BeNull();
        OpenRepositoryModel.ParentOf("\0invalid").Should().BeNull();
    }

    [Test]
    public void CanGoUp_requires_an_existing_directory_with_a_parent()
    {
        OpenRepositoryModel.CanGoUp(Rooted("a", "b"), directoryExists: _ => true).Should().BeTrue();
        OpenRepositoryModel.CanGoUp(Rooted("a", "b"), directoryExists: _ => false).Should().BeFalse();
        OpenRepositoryModel.CanGoUp(_root, directoryExists: _ => true).Should().BeFalse(because: "the root has no parent");
        OpenRepositoryModel.CanGoUp(null, directoryExists: _ => true).Should().BeFalse();
        OpenRepositoryModel.CanGoUp("   ", directoryExists: _ => true).Should().BeFalse();
    }
}
