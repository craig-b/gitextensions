using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class GitConfigAdvancedPageModelTests
{
    private static readonly string[] _keys =
    [
        "pull.rebase",
        "fetch.prune",
        "merge.autostash",
        "rebase.autostash",
        "rebase.autosquash",
        "rebase.updaterefs",
        "rerere.enabled",
        "rerere.autoupdate",
    ];

    private readonly MemorySettingsSource _source = new();

    [Test]
    public void Page_has_one_tri_state_entry_per_git_setting()
    {
        GitConfigAdvancedPageModel model = new(() => _source);

        model.Title.Should().Be("Advanced");
        model.Entries.Should().AllBeOfType<TriStateSettingsEntry>();
        model.Entries.Select(entry => entry.Caption).Should().OnlyHaveUniqueItems();
        foreach ((SettingsEntry entry, string key) in model.Entries.Zip(_keys))
        {
            entry.Caption.Should().EndWith($"[{key}]");
        }
    }

    [TestCase("true", true)]
    [TestCase("yes", true)]
    [TestCase("on", true)]
    [TestCase("1", true)]
    [TestCase("false", false)]
    [TestCase("no", false)]
    [TestCase("off", false)]
    [TestCase("0", false)]
    [TestCase("", false)]
    [TestCase("maybe", null)]
    [TestCase(null, null)]
    public void Load_parses_git_boolean_spellings(string? stored, bool? expected)
    {
        _source.SetValue("pull.rebase", stored);

        GitConfigAdvancedPageModel model = new(() => _source);
        model.Load();

        ((TriStateSettingsEntry)model.Entries.First()).Value.Should().Be(expected);
    }

    [TestCase(true, "true")]
    [TestCase(false, "false")]
    [TestCase(null, null)]
    public void Save_writes_canonical_values_and_unsets_on_indeterminate(bool? value, string? expected)
    {
        _source.SetValue("pull.rebase", "yes");

        GitConfigAdvancedPageModel model = new(() => _source);
        model.Load();
        ((TriStateSettingsEntry)model.Entries.First()).Value = value;
        model.Save();

        _source.GetValue("pull.rebase").Should().Be(expected);
    }

    [Test]
    public void Each_entry_writes_exactly_its_own_key()
    {
        GitConfigAdvancedPageModel model = new(() => _source);
        SettingsEntry[] entries = [.. model.Entries];

        for (int i = 0; i < entries.Length; i++)
        {
            foreach (string key in _keys)
            {
                _source.SetValue(key, "true");
            }

            model.Load();
            ((TriStateSettingsEntry)entries[i]).Value = false;
            model.Save();

            for (int j = 0; j < _keys.Length; j++)
            {
                _source.GetValue(_keys[j]).Should().Be(j == i ? "false" : "true",
                    because: $"changing '{entries[i].Caption}' must affect only {_keys[i]}");
            }
        }
    }
}
