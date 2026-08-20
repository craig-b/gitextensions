using GitCommands.Actions;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Actions;

public sealed class HotkeyResolutionTests
{
    [Test]
    public void Effective_prefers_the_override_and_honors_the_none_sentinel()
    {
        HotkeyResolution.Effective(null, "Ctrl+B").Should().Be("Ctrl+B");
        HotkeyResolution.Effective("  ", "Ctrl+B").Should().Be("Ctrl+B");
        HotkeyResolution.Effective("Ctrl+Shift+B", "Ctrl+B").Should().Be("Ctrl+Shift+B");
        HotkeyResolution.Effective("none", "Ctrl+B").Should().BeNull();
        HotkeyResolution.Effective("NONE", "Ctrl+B").Should().BeNull();
        HotkeyResolution.Effective("F2", null).Should().Be("F2");
    }

    [Test]
    public void ResolveAll_maps_only_actions_with_a_gesture()
    {
        IReadOnlyDictionary<string, string> map = HotkeyResolution.ResolveAll(
            GridMenuRegistry.CommitActions,
            actionId => actionId == "commit.cherryPick" ? "Ctrl+K" : null);

        map["commit.createBranch"].Should().Be("Ctrl+B");
        map["commit.cherryPick"].Should().Be("Ctrl+K");
        map.Should().NotContainKey("commit.revert");
    }

    [Test]
    public void Normalize_orders_modifiers_canonically()
    {
        HotkeyResolution.Normalize(control: true, shift: true, alt: false, "B").Should().Be("Ctrl+Shift+B");
        HotkeyResolution.Normalize(control: false, shift: false, alt: false, "Del").Should().Be("Del");
        HotkeyResolution.Normalize(control: false, shift: false, alt: true, "F2").Should().Be("Alt+F2");
    }

    [Test]
    public void Custom_order_parses_separators_and_falls_back_to_null()
    {
        MenuCustomOrder.Parse("commit.createBranch, commit.cherryPick\nref.delete;  ")
            .Should().Equal("commit.createBranch", "commit.cherryPick", "ref.delete");
        MenuCustomOrder.Parse("").Should().BeNull();
        MenuCustomOrder.Parse("  ,\n; ").Should().BeNull();
        MenuCustomOrder.DefaultText(GridMenuRegistry.RefActions).Should().StartWith("ref.checkout, ");
    }

    [Test]
    public void Hotkeys_page_projects_every_registry_action()
    {
        HotkeysPageModel page = new();

        int entryCount = page.Groups.Sum(group => group.Entries.Count);
        entryCount.Should().Be(GridMenuRegistry.CommitActions.Count + GridMenuRegistry.RefActions.Count);
        page.Groups.Select(group => group.Caption).Should().Equal("Commit menu", "Ref menu");
    }
}
