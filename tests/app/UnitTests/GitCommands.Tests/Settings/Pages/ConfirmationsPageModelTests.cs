using GitCommands;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class ConfirmationsPageModelTests
{
    // One probe per model entry, in the model's display order. Get/Set address the raw
    // storage so the tests can prove the view-semantics inversion and exact wiring.
    private static readonly (string Name, Func<bool?> Get, Action<bool?> Set)[] _storage =
    [
        ("DontConfirmAmend", () => AppSettings.DontConfirmAmend, value => AppSettings.DontConfirmAmend = value!.Value),
        ("DontConfirmUndoLastCommit", () => AppSettings.DontConfirmUndoLastCommit, value => AppSettings.DontConfirmUndoLastCommit = value!.Value),
        ("DontConfirmCommitIfNoBranch", () => AppSettings.DontConfirmCommitIfNoBranch, value => AppSettings.DontConfirmCommitIfNoBranch = value!.Value),
        ("DontConfirmRebase", () => AppSettings.DontConfirmRebase, value => AppSettings.DontConfirmRebase = value!.Value),
        ("DontConfirmFetchAndPruneAll", () => AppSettings.DontConfirmFetchAndPruneAll, value => AppSettings.DontConfirmFetchAndPruneAll = value!.Value),
        ("DontConfirmPushNewBranch", () => AppSettings.DontConfirmPushNewBranch, value => AppSettings.DontConfirmPushNewBranch = value!.Value),
        ("DontConfirmAddTrackingRef", () => AppSettings.DontConfirmAddTrackingRef, value => AppSettings.DontConfirmAddTrackingRef = value!.Value),
        ("DontConfirmDeleteUnmergedBranch", () => AppSettings.DontConfirmDeleteUnmergedBranch, value => AppSettings.DontConfirmDeleteUnmergedBranch = value!.Value),
        ("ConfirmBranchCheckout", () => AppSettings.ConfirmBranchCheckout.Value, value => AppSettings.ConfirmBranchCheckout.Value = value!.Value),
        ("AutoPopStashAfterCheckoutBranch", () => AppSettings.AutoPopStashAfterCheckoutBranch, value => AppSettings.AutoPopStashAfterCheckoutBranch = value),
        ("AutoPopStashAfterPull", () => AppSettings.AutoPopStashAfterPull, value => AppSettings.AutoPopStashAfterPull = value),
        ("DontConfirmStashDrop", () => AppSettings.DontConfirmStashDrop, value => AppSettings.DontConfirmStashDrop = value!.Value),
        ("DontConfirmResolveConflicts", () => AppSettings.DontConfirmResolveConflicts, value => AppSettings.DontConfirmResolveConflicts = value!.Value),
        ("DontConfirmCommitAfterConflictsResolved", () => AppSettings.DontConfirmCommitAfterConflictsResolved, value => AppSettings.DontConfirmCommitAfterConflictsResolved = value!.Value),
        ("DontConfirmSecondAbortConfirmation", () => AppSettings.DontConfirmSecondAbortConfirmation, value => AppSettings.DontConfirmSecondAbortConfirmation = value!.Value),
        ("DontConfirmUpdateSubmodulesOnCheckout", () => AppSettings.DontConfirmUpdateSubmodulesOnCheckout, value => AppSettings.DontConfirmUpdateSubmodulesOnCheckout = value),
        ("DontConfirmSwitchWorktree", () => AppSettings.DontConfirmSwitchWorktree, value => AppSettings.DontConfirmSwitchWorktree = value!.Value),
    ];

    private bool?[] _saved = [];

    [SetUp]
    public void SetUp()
    {
        _saved = [.. _storage.Select(probe => probe.Get())];
    }

    [TearDown]
    public void TearDown()
    {
        foreach (((_, _, Action<bool?> set), bool? saved) in _storage.Zip(_saved))
        {
            set(saved);
        }
    }

    [Test]
    public void Entries_speak_the_positive_view_sense_over_DontConfirm_storage()
    {
        AppSettings.DontConfirmAmend = true;
        ConfirmationsPageModel model = new();

        model.Load();
        model.AmendLastCommit.Value.Should().BeFalse();

        model.AmendLastCommit.Value = true;
        model.Save();
        AppSettings.DontConfirmAmend.Should().BeFalse();
    }

    [Test]
    public void CheckoutBranchUsingLeftPanel_is_stored_without_inversion()
    {
        AppSettings.ConfirmBranchCheckout.Value = true;
        ConfirmationsPageModel model = new();

        model.Load();
        model.CheckoutBranchUsingLeftPanel.Value.Should().BeTrue();

        model.CheckoutBranchUsingLeftPanel.Value = false;
        model.Save();
        AppSettings.ConfirmBranchCheckout.Value.Should().BeFalse();
    }

    [TestCase(null, null)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void ApplyStashAfterPull_maps_indeterminate_and_inverts(bool? stored, bool? view)
    {
        AppSettings.AutoPopStashAfterPull = stored;
        ConfirmationsPageModel model = new();

        model.Load();
        model.ApplyStashAfterPull.Value.Should().Be(view);

        model.Save();
        AppSettings.AutoPopStashAfterPull.Should().Be(stored);
    }

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        ConfirmationsPageModel model = new();

        model.Title.Should().Be("Confirmations");
        model.Groups.Select(group => group.Caption).Should().Equal(
            "Commits:",
            "Branches:",
            "Stash:",
            "Rebase / conflict resolution:",
            "Submodules:",
            "Worktrees:");
        model.Entries.Should().HaveCount(_storage.Length);
        model.Entries.Select(entry => entry.Caption).Should().OnlyHaveUniqueItems()
            .And.NotContainNulls().And.NotContain(string.Empty);
    }

    [Test]
    public void Load_then_Save_is_a_no_op_on_storage()
    {
        for (int i = 0; i < _storage.Length; i++)
        {
            _storage[i].Set(MixedPattern(i));
        }

        bool?[] before = [.. _storage.Select(probe => probe.Get())];

        ConfirmationsPageModel model = new();
        model.Load();
        model.Save();

        _storage.Select(probe => probe.Get()).Should().Equal(before);

        static bool? MixedPattern(int index)
            => index switch
            {
                // tri-state slots exercise the unset value
                9 or 10 or 15 => null,
                _ => index % 2 == 0,
            };
    }

    [Test]
    public void Each_entry_writes_exactly_its_own_storage_slot()
    {
        ConfirmationsPageModel model = new();
        SettingsEntry[] entries = [.. model.Entries];
        entries.Should().HaveCount(_storage.Length);

        for (int i = 0; i < entries.Length; i++)
        {
            bool?[] before = [.. _storage.Select(probe => probe.Get())];

            model.Load();
            Toggle(entries[i]);
            model.Save();

            bool?[] after = [.. _storage.Select(probe => probe.Get())];
            for (int j = 0; j < _storage.Length; j++)
            {
                if (j == i)
                {
                    after[j].Should().NotBe(before[j], because: $"toggling '{entries[i].Caption}' must change {_storage[j].Name}");
                }
                else
                {
                    after[j].Should().Be(before[j], because: $"toggling '{entries[i].Caption}' must not touch {_storage[j].Name}");
                }
            }
        }

        static void Toggle(SettingsEntry entry)
        {
            switch (entry)
            {
                case BoolSettingsEntry boolEntry:
                    boolEntry.Value = !boolEntry.Value;
                    break;
                case TriStateSettingsEntry triStateEntry:
                    // cycle so every state lands on a state with a different stored value
                    triStateEntry.Value = triStateEntry.Value switch
                    {
                        null => true,
                        true => false,
                        false => null,
                    };
                    break;
            }
        }
    }
}
