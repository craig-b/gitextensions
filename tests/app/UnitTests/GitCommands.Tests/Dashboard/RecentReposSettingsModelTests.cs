using GitCommands;
using GitCommands.Dashboard;
using GitCommands.UserRepositoryHistory;

namespace GitCommandsTests.Dashboard;
public class RecentReposSettingsModelTests
{
    #region ComboWidthRule.Snap
    [TestCase(40, 40, 40)]
    [TestCase(20, 20, 20)]
    public void Snap_should_leave_the_same_value_unchanged(int previousValue, int newValue, int expected)
    {
        ComboWidthRule.Snap(previousValue, newValue).Should().Be(expected);
    }

    [TestCase(40, 35, 35)]
    [TestCase(35, 40, 40)]
    [TestCase(0, 30, 30)]
    public void Snap_should_leave_values_at_or_above_the_minimum_unchanged_in_both_directions(int previousValue, int newValue, int expected)
    {
        ComboWidthRule.Snap(previousValue, newValue).Should().Be(expected);
    }

    [TestCase(30, 29)]
    [TestCase(29, 10)]
    public void Snap_should_snap_to_zero_when_shrinking_below_the_minimum(int previousValue, int newValue)
    {
        ComboWidthRule.Snap(previousValue, newValue).Should().Be(0);
    }

    [TestCase(0, 1)]
    [TestCase(10, 29)]
    public void Snap_should_snap_up_to_the_minimum_when_growing_from_below_it(int previousValue, int newValue)
    {
        ComboWidthRule.Snap(previousValue, newValue).Should().Be(ComboWidthRule.MinComboWidthAllowed);
    }
    #endregion

    #region AnchorCommandAvailability
    [TestCase(Repository.RepositoryAnchor.AnchoredInTop, false, true, true)]
    [TestCase(Repository.RepositoryAnchor.AnchoredInRecent, true, false, true)]
    [TestCase(Repository.RepositoryAnchor.None, true, true, false)]
    public void For_should_offer_the_commands_that_change_the_current_anchor(
        Repository.RepositoryAnchor anchor, bool expectedCanAnchorTop, bool expectedCanAnchorRecent, bool expectedCanRemoveAnchor)
    {
        (bool canAnchorTop, bool canAnchorRecent, bool canRemoveAnchor) = AnchorCommandAvailability.For(anchor);

        canAnchorTop.Should().Be(expectedCanAnchorTop);
        canAnchorRecent.Should().Be(expectedCanAnchorRecent);
        canRemoveAnchor.Should().Be(expectedCanRemoveAnchor);
    }
    #endregion

    #region RecentReposSettingsSnapshot
    [Test]
    public void ToSplitterOptions_should_map_every_splitter_relevant_field()
    {
        RecentReposSettingsSnapshot snapshot = new(
            ShorteningStrategy: ShorteningRecentRepoPathStrategy.MiddleDots,
            HideTopRepositoriesFromRecentList: true,
            SortTopRepos: true,
            SortRecentRepos: false,
            RecentReposComboMinWidth: 120,
            MaxTopRepositories: 7,
            RecentRepositoriesHistorySize: 42);
        Func<string?, int> measure = caption => 5;

        RecentRepoSplitterOptions options = snapshot.ToSplitterOptions(measure);

        options.MaxTopRepositories.Should().Be(7);
        options.HideTopRepositoriesFromRecentList.Should().BeTrue();
        options.ShorteningStrategy.Should().Be(ShorteningRecentRepoPathStrategy.MiddleDots);
        options.SortTopRepos.Should().BeTrue();
        options.SortRecentRepos.Should().BeFalse();
        options.RecentReposComboMinWidth.Should().Be(120);
        options.MeasureCaptionWidth.Should().BeSameAs(measure);
    }

    [Test]
    public void ToSplitterOptions_should_leave_the_width_measure_null_when_none_is_supplied()
    {
        RecentReposSettingsSnapshot snapshot = new(
            ShorteningStrategy: ShorteningRecentRepoPathStrategy.None,
            HideTopRepositoriesFromRecentList: false,
            SortTopRepos: false,
            SortRecentRepos: false,
            RecentReposComboMinWidth: 0,
            MaxTopRepositories: 0,
            RecentRepositoriesHistorySize: 30);

        snapshot.ToSplitterOptions().MeasureCaptionWidth.Should().BeNull();
    }

    // The assembly-level [TestAppSettings] harness gives every test run its own settings file
    // (same isolation LocalRepositoryManagerTests relies on to touch AppSettings directly), so a
    // real round-trip is safe; the backup/restore mirrors that test's SetUp/TearDown pattern.
    [Test]
    public void Load_should_round_trip_what_Save_persisted()
    {
        RecentReposSettingsSnapshot backup = RecentReposSettingsSnapshot.Load();
        try
        {
            RecentReposSettingsSnapshot snapshot = new(
                ShorteningStrategy: ShorteningRecentRepoPathStrategy.MostSignDir,
                HideTopRepositoriesFromRecentList: !backup.HideTopRepositoriesFromRecentList,
                SortTopRepos: !backup.SortTopRepos,
                SortRecentRepos: !backup.SortRecentRepos,
                RecentReposComboMinWidth: 123,
                MaxTopRepositories: 9,
                RecentRepositoriesHistorySize: 77);

            snapshot.Save();

            RecentReposSettingsSnapshot.Load().Should().Be(snapshot);
        }
        finally
        {
            backup.Save();
        }
    }
    #endregion
}
