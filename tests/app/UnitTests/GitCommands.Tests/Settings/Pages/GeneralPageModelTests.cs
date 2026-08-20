using GitCommands;
using GitCommands.Settings.Pages;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.Settings.Pages;

internal sealed class GeneralPageModelTests
{
    private static readonly (string Name, Func<object?> Get, Action<object?> Set)[] _storage =
    [
        ("ShowGitStatusInBrowseToolbar", () => AppSettings.ShowGitStatusInBrowseToolbar, value => AppSettings.ShowGitStatusInBrowseToolbar = (bool)value!),
        ("ShowGitStatusForArtificialCommits", () => AppSettings.ShowGitStatusForArtificialCommits, value => AppSettings.ShowGitStatusForArtificialCommits = (bool)value!),
        ("ShowSubmoduleStatus", () => AppSettings.ShowSubmoduleStatus, value => AppSettings.ShowSubmoduleStatus = (bool)value!),
        ("ShowStashCount", () => AppSettings.ShowStashCount, value => AppSettings.ShowStashCount = (bool)value!),
        ("ShowAheadBehindData", () => AppSettings.ShowAheadBehindData, value => AppSettings.ShowAheadBehindData = (bool)value!),
        ("CheckForUncommittedChangesInCheckoutBranch", () => AppSettings.CheckForUncommittedChangesInCheckoutBranch, value => AppSettings.CheckForUncommittedChangesInCheckoutBranch = (bool)value!),
        ("MaxRevisionGraphCommits", () => AppSettings.MaxRevisionGraphCommits, value => AppSettings.MaxRevisionGraphCommits = (int)value!),
        ("CloseProcessDialog", () => AppSettings.CloseProcessDialog, value => AppSettings.CloseProcessDialog = (bool)value!),
        ("ShowGitCommandLine", () => AppSettings.ShowGitCommandLine, value => AppSettings.ShowGitCommandLine = (bool)value!),
        ("UseHistogramDiffAlgorithm", () => AppSettings.UseHistogramDiffAlgorithm, value => AppSettings.UseHistogramDiffAlgorithm = (bool)value!),
        ("IncludeUntrackedFilesInAutoStash", () => AppSettings.IncludeUntrackedFilesInAutoStash, value => AppSettings.IncludeUntrackedFilesInAutoStash = (bool)value!),
        ("UpdateSubmodulesOnCheckout", () => AppSettings.UpdateSubmodulesOnCheckout, value => AppSettings.UpdateSubmodulesOnCheckout = (bool?)value),
        ("FollowRenamesInFileHistory", () => AppSettings.FollowRenamesInFileHistory, value => AppSettings.FollowRenamesInFileHistory = (bool)value!),
        ("FollowRenamesInFileHistoryExactOnly", () => AppSettings.FollowRenamesInFileHistoryExactOnly, value => AppSettings.FollowRenamesInFileHistoryExactOnly = (bool)value!),
        ("StartWithRecentWorkingDir", () => AppSettings.StartWithRecentWorkingDir, value => AppSettings.StartWithRecentWorkingDir = (bool)value!),
        ("DefaultCloneDestinationPath", () => AppSettings.DefaultCloneDestinationPath, value => AppSettings.DefaultCloneDestinationPath = (string)value!),
        ("DefaultPullAction", () => AppSettings.DefaultPullAction, value => AppSettings.DefaultPullAction = (GitPullAction)value!),
        ("RevisionGridQuickSearchTimeout", () => AppSettings.RevisionGridQuickSearchTimeout, value => AppSettings.RevisionGridQuickSearchTimeout = (int)value!),
        ("TelemetryEnabled", () => AppSettings.TelemetryEnabled, value => AppSettings.TelemetryEnabled = (bool?)value),
    ];

    private object?[] _saved = [];

    [SetUp]
    public void SetUp()
    {
        _saved = [.. _storage.Select(probe => probe.Get())];
        ApplyBaseline();
    }

    // A self-consistent baseline: the submodule-status value's gate (either git-status
    // option) is on, and the nullable telemetry flag is definite — states in which
    // load-then-save round-trips storage unchanged, as it does in the app.
    private static void ApplyBaseline()
    {
        AppSettings.ShowGitStatusInBrowseToolbar = true;
        AppSettings.ShowGitStatusForArtificialCommits = true;
        AppSettings.ShowSubmoduleStatus = true;
        AppSettings.ShowStashCount = false;
        AppSettings.ShowAheadBehindData = true;
        AppSettings.CheckForUncommittedChangesInCheckoutBranch = false;
        AppSettings.MaxRevisionGraphCommits = 200_000;
        AppSettings.CloseProcessDialog = true;
        AppSettings.ShowGitCommandLine = false;
        AppSettings.UseHistogramDiffAlgorithm = true;
        AppSettings.IncludeUntrackedFilesInAutoStash = false;
        AppSettings.UpdateSubmodulesOnCheckout = null;
        AppSettings.FollowRenamesInFileHistory = true;
        AppSettings.FollowRenamesInFileHistoryExactOnly = false;
        AppSettings.StartWithRecentWorkingDir = true;
        AppSettings.DefaultCloneDestinationPath = "/tmp/clones";
        AppSettings.DefaultPullAction = GitPullAction.Merge;
        AppSettings.RevisionGridQuickSearchTimeout = 750;
        AppSettings.TelemetryEnabled = false;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (((_, _, Action<object?> set), object? saved) in _storage.Zip(_saved))
        {
            set(saved);
        }
    }

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        GeneralPageModel model = new();

        model.Title.Should().Be("General");
        model.Groups.Select(group => group.Caption).Should().Equal("Performance", "Behaviour", "Telemetry");
        model.Entries.Should().HaveCount(_storage.Length);
    }

    [Test]
    public void Stored_Default_pull_action_normalizes_to_open_pull_dialog()
    {
        AppSettings.DefaultPullAction = GitPullAction.Default;
        GeneralPageModel model = new();

        model.Load();
        model.DefaultPullAction.SelectedIndex.Should().Be(0);
        model.DefaultPullAction.Choices[0].Should().Be("Open pull dialog");

        model.Save();
        AppSettings.DefaultPullAction.Should().Be(GitPullAction.None);
    }

    [Test]
    public void Pull_action_choice_maps_index_to_value()
    {
        GeneralPageModel model = new();

        model.Load();
        model.DefaultPullAction.SelectedIndex.Should().Be(1, because: "the baseline stores Merge");

        model.DefaultPullAction.SelectedIndex = 5;
        model.Save();
        AppSettings.DefaultPullAction.Should().Be(GitPullAction.FetchPruneAll);
    }

    [Test]
    public void Commits_limit_gate_maps_zero_to_disabled()
    {
        AppSettings.MaxRevisionGraphCommits = 0;
        GeneralPageModel model = new();

        model.Load();
        model.CommitsLimit.Enabled.Should().BeFalse();

        model.CommitsLimit.Number = 12_345;
        model.Save();
        AppSettings.MaxRevisionGraphCommits.Should().Be(0, because: "the gate is still off");

        model.CommitsLimit.Enabled = true;
        model.Save();
        AppSettings.MaxRevisionGraphCommits.Should().Be(12_345);
    }

    [Test]
    public void Submodule_status_is_stored_false_while_both_git_status_options_are_off()
    {
        GeneralPageModel model = new();
        model.Load();

        model.ShowGitStatusInToolbar.Value = false;
        model.ShowGitStatusForArtificialCommits.Value = false;
        model.ShowSubmoduleStatus.Value = true;
        model.Save();
        AppSettings.ShowSubmoduleStatus.Should().BeFalse();

        model.ShowGitStatusForArtificialCommits.Value = true;
        model.Save();
        AppSettings.ShowSubmoduleStatus.Should().BeTrue();
    }

    [Test]
    public void Load_then_Save_is_a_no_op_on_storage()
    {
        object?[] before = [.. _storage.Select(probe => probe.Get())];

        GeneralPageModel model = new();
        model.Load();
        model.Save();

        _storage.Select(probe => probe.Get()).Should().Equal(before);
    }

    [Test]
    public void Each_entry_writes_exactly_its_own_storage_slot()
    {
        GeneralPageModel model = new();
        SettingsEntry[] entries = [.. model.Entries];
        entries.Should().HaveCount(_storage.Length);

        for (int i = 0; i < entries.Length; i++)
        {
            // iterations must be independent: a leftover mutation (e.g. both git-status
            // options off) would make the submodule coercion span two slots
            ApplyBaseline();
            object?[] before = [.. _storage.Select(probe => probe.Get())];

            model.Load();
            Change(entries[i]);
            model.Save();

            object?[] after = [.. _storage.Select(probe => probe.Get())];
            for (int j = 0; j < _storage.Length; j++)
            {
                if (j == i)
                {
                    after[j].Should().NotBe(before[j], because: $"changing '{entries[i].Caption}' must change {_storage[j].Name}");
                }
                else
                {
                    after[j].Should().Be(before[j], because: $"changing '{entries[i].Caption}' must not touch {_storage[j].Name}");
                }
            }
        }

        static void Change(SettingsEntry entry)
        {
            switch (entry)
            {
                case BoolSettingsEntry boolEntry:
                    boolEntry.Value = !boolEntry.Value;
                    break;
                case TriStateSettingsEntry triStateEntry:
                    triStateEntry.Value = triStateEntry.Value switch
                    {
                        null => true,
                        true => false,
                        false => null,
                    };
                    break;
                case NumberSettingsEntry numberEntry:
                    numberEntry.Value++;
                    break;
                case OptionalNumberSettingsEntry optionalNumberEntry:
                    optionalNumberEntry.Enabled = !optionalNumberEntry.Enabled;
                    break;
                case StringSettingsEntry stringEntry:
                    stringEntry.Value += "x";
                    break;
                case ChoiceSettingsEntry choiceEntry:
                    choiceEntry.SelectedIndex = (choiceEntry.SelectedIndex + 1) % choiceEntry.Choices.Count;
                    break;
            }
        }
    }
}
