using GitCommands.FileHistory;

namespace GitCommandsTests.FileHistory;

public sealed class FileHistoryModelTests
{
    [TestCase("src/file.cs", "\"src/file.cs\"", false)]
    [TestCase("  src/file.cs  ", "\"src/file.cs\"", false)]
    [TestCase("a path with spaces", "a path with spaces", true)]
    [TestCase("\"quoted path\"", "\"quoted path\"", false)]
    [TestCase("\"one\" \"two\"", "\"one\" \"two\"", true)]
    public void Path_argument_normalization_follows_the_quoting_heuristic(string input, string expectedPath, bool expectedMultiple)
    {
        (string path, bool multipleArgs) = FileHistoryPathFilter.NormalizeArgument(input);

        path.Should().Be(expectedPath);
        multipleArgs.Should().Be(expectedMultiple);
    }

    [Test]
    public void Historical_name_collection_is_gated()
    {
        FileHistoryPathFilter.ShouldCollectHistoricalNames("\"src/a.cs\"", multipleArgs: false, followRenames: true).Should().BeTrue();
        FileHistoryPathFilter.ShouldCollectHistoricalNames("\"src/a.cs\"", multipleArgs: false, followRenames: false).Should().BeFalse();
        FileHistoryPathFilter.ShouldCollectHistoricalNames("src/", multipleArgs: false, followRenames: true).Should().BeFalse();
        FileHistoryPathFilter.ShouldCollectHistoricalNames("\"src/\"", multipleArgs: false, followRenames: true).Should().BeFalse();
        FileHistoryPathFilter.ShouldCollectHistoricalNames("\"src/a.cs\"", multipleArgs: true, followRenames: true).Should().BeFalse();
    }

    [Test]
    public void Find_renames_options_honor_exact_only()
    {
        FileHistoryPathFilter.FindRenamesAndCopiesOptions(exactOnly: true).ToString()
            .Should().Be(" --find-renames=\"100%\" --find-copies=\"100%\"");
        FileHistoryPathFilter.FindRenamesAndCopiesOptions(exactOnly: false).ToString()
            .Should().Be(" --find-renames --find-copies");
    }

    [Test]
    public void Follow_names_command_matches_the_historical_shape()
    {
        string command = FileHistoryPathFilter.FollowNamesCommand("\"src/a.cs\"", "prefix:", exactOnly: false).ToString();

        command.Should().Contain("log --format=\"prefix:%H\" --name-only --follow");
        command.Should().Contain("--find-renames --find-copies");
        command.Should().EndWith("-- \"src/a.cs\"");
    }

    [Test]
    public void Combine_joins_names_and_falls_back_when_empty_or_too_long()
    {
        FileHistoryPathFilter.Combine("\"a.cs\"", ["a.cs", "old/a.cs"])
            .Should().Be((" \"a.cs\" \"old/a.cs\"", false));

        FileHistoryPathFilter.Combine("\"a.cs\"", [])
            .Should().Be(("\"a.cs\"", false));

        string[] huge = [.. Enumerable.Range(0, 500).Select(i => new string('x', 100) + i)];
        (string filter, bool tooLong) = FileHistoryPathFilter.Combine("\"a.cs\"", huge);
        filter.Should().Be("\"a.cs\"");
        tooLong.Should().BeTrue();
    }

    [Test]
    public void Tab_decision_for_a_regular_available_file()
    {
        FileHistoryTabDecision.Resolve(isArtificial: false, isFolder: false, fileExists: true, blameSupported: true)
            .Should().Be(new FileHistoryTabDecision(
                ShowCommitInfo: true, ShowDiff: true, ShowView: true, ShowBlame: true, PreferredTab: null, FileAvailable: true));
    }

    [Test]
    public void Artificial_revisions_lose_commit_info_and_prefer_the_diff()
    {
        FileHistoryTabDecision tabs = FileHistoryTabDecision.Resolve(isArtificial: true, isFolder: false, fileExists: true, blameSupported: true);

        tabs.ShowCommitInfo.Should().BeFalse();
        tabs.ShowView.Should().BeFalse();
        tabs.ShowBlame.Should().BeFalse();
        tabs.PreferredTab.Should().Be(FileHistoryTab.Diff);
    }

    [Test]
    public void Unavailable_files_lose_the_diff_and_prefer_commit_info()
    {
        FileHistoryTabDecision tabs = FileHistoryTabDecision.Resolve(isArtificial: false, isFolder: false, fileExists: false, blameSupported: true);

        tabs.ShowDiff.Should().BeFalse();
        tabs.ShowView.Should().BeFalse();
        tabs.ShowBlame.Should().BeFalse();
        tabs.PreferredTab.Should().Be(FileHistoryTab.CommitInfo);
    }

    [Test]
    public void Folders_count_as_unavailable()
    {
        FileHistoryTabDecision.Resolve(isArtificial: false, isFolder: true, fileExists: true, blameSupported: true)
            .FileAvailable.Should().BeFalse();
    }

    [Test]
    public void Blame_needs_support()
    {
        FileHistoryTabDecision.Resolve(isArtificial: false, isFolder: false, fileExists: true, blameSupported: false)
            .ShowBlame.Should().BeFalse();
    }

    [Test]
    public void Startup_rules()
    {
        FileHistoryStartup.NormalizeFileName("\"dir/file.cs\"").Should().Be("dir/file.cs");
        FileHistoryStartup.ShouldAutoLoad(blameTabSelected: true, loadBlameOnShow: true, loadHistoryOnShow: false).Should().BeTrue();
        FileHistoryStartup.ShouldAutoLoad(blameTabSelected: false, loadBlameOnShow: true, loadHistoryOnShow: false).Should().BeFalse();
        FileHistoryStartup.ShouldAutoLoad(blameTabSelected: false, loadBlameOnShow: false, loadHistoryOnShow: true).Should().BeTrue();
        FileHistoryStartup.InitialTab(blameTabExists: true, showBlame: true).Should().Be(FileHistoryTab.Blame);
        FileHistoryStartup.InitialTab(blameTabExists: false, showBlame: true).Should().Be(FileHistoryTab.Diff);
        FileHistoryStartup.BuildTitle("a.cs", "old/a.cs").Should().Be("a.cs (old/a.cs)");
        FileHistoryStartup.BuildTitle("a.cs", "a.cs").Should().Be("a.cs");
    }
}
