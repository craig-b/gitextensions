using GitCommands.Settings;
using GitUI.Editor.Diff;

namespace GitCommandsTests.Editor.Diff;

public sealed class DiffViewArgumentsTests
{
    [Test]
    public void Options_become_the_winforms_flag_set()
    {
        DiffViewArguments.Build(IgnoreWhitespaceKind.None, 3, showEntireFile: false, treatAllFilesAsText: false)
            .ToString().Should().Be("--unified=3");

        DiffViewArguments.Build(IgnoreWhitespaceKind.AllSpace, 7, showEntireFile: false, treatAllFilesAsText: true)
            .ToString().Should().Be("--ignore-all-space --unified=7 --text");

        DiffViewArguments.Build(IgnoreWhitespaceKind.Eol, 3, showEntireFile: true, treatAllFilesAsText: false)
            .ToString().Should().Be("--ignore-space-at-eol --inter-hunk-context=9000 --unified=9000");

        DiffViewArguments.Build(IgnoreWhitespaceKind.Change, 0, showEntireFile: false, treatAllFilesAsText: false)
            .ToString().Should().Be("--ignore-space-change --unified=0");
    }
}
