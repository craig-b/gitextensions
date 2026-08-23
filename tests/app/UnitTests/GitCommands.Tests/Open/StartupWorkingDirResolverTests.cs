using GitCommands.Open;

namespace GitCommandsTests.Open;

public sealed class StartupWorkingDirResolverTests
{
    [Test]
    public void Disabled_setting_starts_without_a_repository_and_prunes_nothing()
    {
        StartupWorkingDir result = StartupWorkingDir.Resolve(
            startWithRecentWorkingDir: false,
            recentWorkingDir: "/repos/a",
            isValidGitWorkingDir: _ => true);

        result.WorkingDir.Should().BeNull();
        result.StalePathToPrune.Should().BeNull();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Missing_recent_directory_starts_without_a_repository(string? recentWorkingDir)
    {
        StartupWorkingDir result = StartupWorkingDir.Resolve(
            startWithRecentWorkingDir: true,
            recentWorkingDir,
            isValidGitWorkingDir: _ => true);

        result.WorkingDir.Should().BeNull();
        result.StalePathToPrune.Should().BeNull();
    }

    [Test]
    public void Valid_recent_directory_is_used_and_not_pruned()
    {
        StartupWorkingDir result = StartupWorkingDir.Resolve(
            startWithRecentWorkingDir: true,
            recentWorkingDir: "/repos/a",
            isValidGitWorkingDir: path => path == "/repos/a");

        result.WorkingDir.Should().Be("/repos/a");
        result.StalePathToPrune.Should().BeNull();
    }

    [Test]
    public void Stale_recent_directory_is_reported_for_pruning_instead_of_being_opened()
    {
        StartupWorkingDir result = StartupWorkingDir.Resolve(
            startWithRecentWorkingDir: true,
            recentWorkingDir: "/repos/gone",
            isValidGitWorkingDir: _ => false);

        result.WorkingDir.Should().BeNull();
        result.StalePathToPrune.Should().Be("/repos/gone");
    }
}
