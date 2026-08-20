using GitCommands;
using GitCommands.Settings;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class DetailedPageModelTests : SettingsPageModelTestBase
{
    private static readonly MemorySettingsSource _source = new();

    protected override (string Name, Func<object?> Get, Action<object?> Set)[] Storage { get; } =
    [
        ("MergeGraphLanesHavingCommonParent", () => AppSettings.MergeGraphLanesHavingCommonParent.Value, value => AppSettings.MergeGraphLanesHavingCommonParent.Value = (bool)value!),
        ("RenderGraphWithDiagonals", () => AppSettings.RenderGraphWithDiagonals.Value, value => AppSettings.RenderGraphWithDiagonals.Value = (bool)value!),
        ("StraightenGraphDiagonals", () => AppSettings.StraightenGraphDiagonals.Value, value => AppSettings.StraightenGraphDiagonals.Value = (bool)value!),
        ("Detailed.GetRemoteBranchesDirectlyFromRemote", () => DetailedSettings.GetRemoteBranchesDirectlyFromRemote[_source], value => DetailedSettings.GetRemoteBranchesDirectlyFromRemote[_source] = (bool?)value),
        ("Detailed.AddMergeLogMessages", () => DetailedSettings.AddMergeLogMessages[_source], value => DetailedSettings.AddMergeLogMessages[_source] = (bool?)value),
        ("Detailed.MergeLogMessagesCount", () => DetailedSettings.MergeLogMessagesCount[_source], value => DetailedSettings.MergeLogMessagesCount[_source] = value),
    ];

    protected override SettingsPageModel CreateModel() => new DetailedPageModel(() => _source);

    protected override void ApplyBaseline()
    {
        // the count loads via ValueOrDefault, so an unset value would save as the default;
        // pin stored values so load-then-save round-trips
        DetailedSettings.GetRemoteBranchesDirectlyFromRemote[_source] = true;
        DetailedSettings.AddMergeLogMessages[_source] = false;
        DetailedSettings.MergeLogMessagesCount[_source] = 42;
    }

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        DetailedPageModel model = new(() => _source);

        model.Title.Should().Be("Detailed");
        model.Groups.Select(group => group.Caption).Should().Equal("Revision graph", "Push window", "Merge window");
        model.Entries.Should().HaveCount(Storage.Length);
    }

    [Test]
    public void Merge_log_message_count_defaults_when_unset()
    {
        DetailedSettings.MergeLogMessagesCount[_source] = null;

        DetailedPageModel model = new(() => _source);
        model.Load();

        model.MergeLogMessagesCount.Value.Should().Be(20);
    }
}
