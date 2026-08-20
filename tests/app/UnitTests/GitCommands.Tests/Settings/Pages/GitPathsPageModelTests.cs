using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

/// <summary>
///  No slot-exactness proof here: both paths are registry-backed on non-portable
///  installations, so storage round-trips only on Windows (on Linux the registry shims
///  read as defaults and writes are dropped — the app falls back to git from PATH).
/// </summary>
internal sealed class GitPathsPageModelTests
{
    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        GitPathsPageModel model = new();

        model.Title.Should().Be("Paths");
        model.Groups.Should().ContainSingle();
        model.Entries.Should().HaveCount(2);
        model.Entries.Select(entry => entry.Caption).Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void Effective_config_environment_reports_a_name()
    {
        (string name, _, bool configEnvIsSet) = GitPathsPageModel.GetEffectiveConfigEnvironment();

        name.Should().Be(configEnvIsSet ? "GIT_CONFIG_GLOBAL" : "HOME");
    }
}
