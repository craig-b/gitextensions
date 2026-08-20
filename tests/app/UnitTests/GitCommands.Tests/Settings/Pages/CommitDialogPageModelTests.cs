using GitCommands;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class CommitDialogPageModelTests
{
    private static readonly (string Name, Func<object> Get, Action<object> Set)[] _storage =
    [
        ("ProvideAutocompletion", () => AppSettings.ProvideAutocompletion, value => AppSettings.ProvideAutocompletion = (bool)value),
        ("ShowErrorsWhenStagingFiles", () => AppSettings.ShowErrorsWhenStagingFiles, value => AppSettings.ShowErrorsWhenStagingFiles = (bool)value),
        ("EnsureCommitMessageSecondLineEmpty", () => AppSettings.EnsureCommitMessageSecondLineEmpty, value => AppSettings.EnsureCommitMessageSecondLineEmpty = (bool)value),
        ("UseFormCommitMessage", () => AppSettings.UseFormCommitMessage, value => AppSettings.UseFormCommitMessage = (bool)value),
        ("CommitDialogNumberOfPreviousMessages", () => AppSettings.CommitDialogNumberOfPreviousMessages, value => AppSettings.CommitDialogNumberOfPreviousMessages = (int)value),
        ("RememberAmendCommitState", () => AppSettings.RememberAmendCommitState, value => AppSettings.RememberAmendCommitState = (bool)value),
        ("ShowCommitAndPush", () => AppSettings.ShowCommitAndPush, value => AppSettings.ShowCommitAndPush = (bool)value),
        ("ShowResetWorkTreeChanges", () => AppSettings.ShowResetWorkTreeChanges, value => AppSettings.ShowResetWorkTreeChanges = (bool)value),
        ("ShowResetAllChanges", () => AppSettings.ShowResetAllChanges, value => AppSettings.ShowResetAllChanges = (bool)value),
    ];

    private object[] _saved = [];

    [SetUp]
    public void SetUp()
    {
        _saved = [.. _storage.Select(probe => probe.Get())];
    }

    [TearDown]
    public void TearDown()
    {
        foreach (((_, _, Action<object> set), object saved) in _storage.Zip(_saved))
        {
            set(saved);
        }
    }

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        CommitDialogPageModel model = new();

        model.Title.Should().Be("Commit dialog");
        model.Groups.Select(group => group.Caption).Should().Equal(
            "Behaviour",
            "Show additional buttons in commit button area");
        model.Entries.Should().HaveCount(_storage.Length);
    }

    [Test]
    public void Number_entry_round_trips_and_carries_the_page_bounds()
    {
        AppSettings.CommitDialogNumberOfPreviousMessages = 7;
        CommitDialogPageModel model = new();

        model.NumberOfPreviousMessages.Minimum.Should().Be(1);
        model.NumberOfPreviousMessages.Maximum.Should().Be(999);

        model.Load();
        model.NumberOfPreviousMessages.Value.Should().Be(7);

        model.NumberOfPreviousMessages.Value = 42;
        model.Save();
        AppSettings.CommitDialogNumberOfPreviousMessages.Should().Be(42);
    }

    [Test]
    public void Load_then_Save_is_a_no_op_on_storage()
    {
        object[] before = [.. _storage.Select(probe => probe.Get())];

        CommitDialogPageModel model = new();
        model.Load();
        model.Save();

        _storage.Select(probe => probe.Get()).Should().Equal(before);
    }

    [Test]
    public void Each_entry_writes_exactly_its_own_storage_slot()
    {
        CommitDialogPageModel model = new();
        SettingsEntry[] entries = [.. model.Entries];
        entries.Should().HaveCount(_storage.Length);

        for (int i = 0; i < entries.Length; i++)
        {
            object[] before = [.. _storage.Select(probe => probe.Get())];

            model.Load();
            switch (entries[i])
            {
                case BoolSettingsEntry boolEntry:
                    boolEntry.Value = !boolEntry.Value;
                    break;
                case NumberSettingsEntry numberEntry:
                    numberEntry.Value++;
                    break;
            }

            model.Save();

            object[] after = [.. _storage.Select(probe => probe.Get())];
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
    }
}
