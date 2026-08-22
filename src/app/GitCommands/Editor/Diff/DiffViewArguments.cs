using GitCommands;
using GitCommands.Settings;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitUI.Editor.Diff;

/// <summary>
///  The view-option half of WinForms' GetExtraDiffArguments, made pure: the whitespace mode,
///  context-line count, entire-file, and treat-as-text settings become the extra git-diff
///  flags. The client's view bar routes its toggles through this - the settings finally
///  reach the actual git invocation.
/// </summary>
public static class DiffViewArguments
{
    public static ArgumentString Build(IgnoreWhitespaceKind ignoreWhitespace, int contextLines, bool showEntireFile, bool treatAllFilesAsText)
        => new ArgumentBuilder
        {
            { ignoreWhitespace == IgnoreWhitespaceKind.AllSpace, "--ignore-all-space" },
            { ignoreWhitespace == IgnoreWhitespaceKind.Change, "--ignore-space-change" },
            { ignoreWhitespace == IgnoreWhitespaceKind.Eol, "--ignore-space-at-eol" },
            { showEntireFile, "--inter-hunk-context=9000 --unified=9000", $"--unified={contextLines}" },
            { treatAllFilesAsText, "--text" },
        };
}
