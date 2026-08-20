using GitCommands.Config;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class GitConfigPageModelTests
{
    private readonly MemorySettingsSource _source = new();

    private GitConfigPageModel CreateModel(bool canSave = true) => new(() => _source, () => canSave);

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        GitConfigPageModel model = CreateModel();

        model.Title.Should().Be("Config");
        model.Groups.Select(group => group.Caption).Should().Equal("Config", "Line endings");
        model.LineEndings.Choices.Should().HaveCount(4).And.EndWith("Not set");
    }

    [Test]
    public void Identity_round_trips_through_the_source()
    {
        _source.SetValue(SettingKeyString.UserName, "Ada Lovelace");
        _source.SetValue(SettingKeyString.UserEmail, "ada@example.com");

        GitConfigPageModel model = CreateModel();
        model.Load();
        model.UserName.Value.Should().Be("Ada Lovelace");
        model.UserEmail.Value.Should().Be("ada@example.com");

        model.UserName.Value = "Grace Hopper";
        model.Save();
        _source.GetValue(SettingKeyString.UserName).Should().Be("Grace Hopper");
    }

    [Test]
    public void Git_settings_do_not_save_while_the_gate_is_closed()
    {
        _source.SetValue(SettingKeyString.UserName, "Ada Lovelace");

        GitConfigPageModel model = CreateModel(canSave: false);
        model.Load();
        model.UserName.Value = "Someone Else";
        model.Save();

        _source.GetValue(SettingKeyString.UserName).Should().Be("Ada Lovelace");
    }

    [TestCase(null, 3)]
    [TestCase("true", 0)]
    [TestCase("input", 1)]
    [TestCase("false", 2)]
    public void Line_endings_map_core_autocrlf(string? stored, int expectedIndex)
    {
        _source.SetValue("core.autocrlf", stored);

        GitConfigPageModel model = CreateModel();
        model.Load();
        model.LineEndings.SelectedIndex.Should().Be(expectedIndex);

        model.Save();
        _source.GetValue("core.autocrlf").Should().Be(stored);
    }

    [Test]
    public void Not_set_choice_unsets_core_autocrlf()
    {
        _source.SetValue("core.autocrlf", "true");

        GitConfigPageModel model = CreateModel();
        model.Load();
        model.LineEndings.SelectedIndex = 3;
        model.Save();

        _source.GetValue("core.autocrlf").Should().BeNull();
    }
}
