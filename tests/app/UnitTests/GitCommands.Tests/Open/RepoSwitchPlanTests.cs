using GitCommands.Open;

namespace GitCommandsTests.Open;

public sealed class RepoSwitchPlanTests
{
    [Test]
    public void Reopening_the_same_valid_repository_keeps_its_view_state()
    {
        RepoSwitchPlan plan = RepoSwitchPlan.Create("/repos/a", "/repos/a", isValidWorkingDir: true);

        plan.PathChanged.Should().BeFalse();
        plan.PersistRecentWorkingDir.Should().BeTrue();
        plan.ShowDashboard.Should().BeFalse();
        plan.ResetRepositoryScopedViewState.Should().BeFalse();
    }

    [Test]
    public void Switching_to_a_different_valid_repository_persists_and_resets_scoped_state()
    {
        RepoSwitchPlan plan = RepoSwitchPlan.Create("/repos/a", "/repos/b", isValidWorkingDir: true);

        plan.PathChanged.Should().BeTrue();
        plan.PersistRecentWorkingDir.Should().BeTrue();
        plan.ShowDashboard.Should().BeFalse();
        plan.ResetRepositoryScopedViewState.Should().BeTrue();
    }

    [Test]
    public void Path_comparison_is_ordinal_so_a_case_difference_counts_as_a_change()
    {
        RepoSwitchPlan.Create("/repos/a", "/repos/A", isValidWorkingDir: true).PathChanged.Should().BeTrue();
    }

    [Test]
    public void An_invalid_working_directory_shows_the_dashboard_and_persists_nothing()
    {
        RepoSwitchPlan plan = RepoSwitchPlan.Create("/repos/a", "/repos/gone", isValidWorkingDir: false);

        plan.PathChanged.Should().BeTrue();
        plan.PersistRecentWorkingDir.Should().BeFalse();
        plan.ShowDashboard.Should().BeTrue();
        plan.ResetRepositoryScopedViewState.Should().BeFalse(because: "an invalid switch must not wipe the previous repository's view state");
    }

    [Test]
    public void Null_paths_compare_like_any_other_value()
    {
        RepoSwitchPlan.Create(null, "/repos/a", isValidWorkingDir: true).PathChanged.Should().BeTrue();
        RepoSwitchPlan.Create(null, null, isValidWorkingDir: false).PathChanged.Should().BeFalse();
    }
}
