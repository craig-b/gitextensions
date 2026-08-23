using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

/// <summary>
///  Shared proof obligations for a settings page model: load-then-save leaves storage
///  untouched, and changing any one entry changes exactly its own storage slot.
///  Fixtures supply the storage probes in the model's entry order.
/// </summary>
internal abstract class SettingsPageModelTestBase
{
    private object?[] _saved = [];

    protected abstract (string Name, Func<object?> Get, Action<object?> Set)[] Storage { get; }

    protected abstract SettingsPageModel CreateModel();

    /// <summary>Puts storage into a state where load-then-save round-trips unchanged.</summary>
    protected virtual void ApplyBaseline()
    {
    }

    [SetUp]
    public void SnapshotStorage()
    {
        _saved = [.. Storage.Select(probe => probe.Get())];
        ApplyBaseline();
    }

    [TearDown]
    public void RestoreStorage()
    {
        foreach (((_, _, Action<object?> set), object? saved) in Storage.Zip(_saved))
        {
            set(saved);
        }
    }

    [Test]
    public void Load_then_Save_is_a_no_op_on_storage()
    {
        object?[] before = [.. Storage.Select(probe => probe.Get())];

        SettingsPageModel model = CreateModel();
        model.Load();
        model.Save();

        Storage.Select(probe => probe.Get()).Should().Equal(before);
    }

    [Test]
    public void Each_entry_writes_exactly_its_own_storage_slot()
    {
        SettingsPageModel model = CreateModel();
        SettingsEntry[] entries = [.. model.Entries];
        entries.Should().HaveCount(Storage.Length, because: "the probes must mirror the model's entries in order");

        for (int i = 0; i < entries.Length; i++)
        {
            ApplyBaseline();
            object?[] before = [.. Storage.Select(probe => probe.Get())];

            model.Load();
            Change(entries[i]);
            model.Save();

            object?[] after = [.. Storage.Select(probe => probe.Get())];
            for (int j = 0; j < Storage.Length; j++)
            {
                if (j == i)
                {
                    after[j].Should().NotBe(before[j], because: $"changing '{entries[i].Caption}' must change {Storage[j].Name}");
                }
                else
                {
                    after[j].Should().Be(before[j], because: $"changing '{entries[i].Caption}' must not touch {Storage[j].Name}");
                }
            }
        }
    }

    private static void Change(SettingsEntry entry)
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
            default:
                throw new NotSupportedException(entry.GetType().Name);
        }
    }
}
