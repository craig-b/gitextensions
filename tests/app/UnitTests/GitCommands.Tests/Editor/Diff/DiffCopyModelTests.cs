using GitUI.Editor.Diff;

namespace GitCommandsTests.Editor.Diff;

public sealed class DiffCopyModelTests
{
    private static readonly string[] PlainPrefixes = ["+", "-", " "];

    private const string Diff =
        "diff --git a/f.txt b/f.txt\n" +
        "--- a/f.txt\n" +
        "+++ b/f.txt\n" +
        "@@ -1,3 +1,3 @@\n" +
        " context\n" +
        "-old line\n" +
        "+new line\n" +
        " tail\n";

    private static string Select(int start, int length) => Diff.Substring(start, length);

    [Test]
    public void Copy_strips_prefixes_inside_hunks()
    {
        int start = Diff.IndexOf(" context");
        string selected = Diff[start..Diff.IndexOf(" tail")];

        DiffCopyModel.CopySelection(Diff, start, selected, PlainPrefixes)
            .Should().Be("context\nold line\nnew line\n");
    }

    [Test]
    public void Copy_keeps_the_header_verbatim()
    {
        string selected = Diff[..Diff.IndexOf("@@")];

        DiffCopyModel.CopySelection(Diff, 0, selected, PlainPrefixes)
            .Should().Be(selected);
    }

    [Test]
    public void Copy_pads_a_mid_line_selection_so_the_first_line_survives_stripping()
    {
        // Selection starts inside "-old line": the artificial pad is what gets stripped.
        int start = Diff.IndexOf("old line");
        string selected = Diff[start..Diff.IndexOf(" tail")];

        DiffCopyModel.CopySelection(Diff, start, selected, PlainPrefixes)
            .Should().Be("old line\nnew line\n");
    }

    [Test]
    public void New_version_drops_removed_lines_and_old_version_drops_added_ones()
    {
        int start = Diff.IndexOf(" context");
        string selected = Diff[start..];

        DiffCopyModel.CopyNewVersion(Diff, start, selected)
            .Should().Be("context\nnew line\ntail\n");
        DiffCopyModel.CopyOldVersion(Diff, start, selected)
            .Should().Be("context\nold line\ntail\n");
    }

    [Test]
    public void Version_copies_keep_file_header_lines_and_their_markers()
    {
        // "---"/"+++" lines (three identical leading chars) survive the drop rule, and a
        // header-region selection keeps prefixes verbatim.
        DiffCopyModel.CopyOldVersion(Diff, 0, Diff)
            .Should().StartWith("diff --git a/f.txt b/f.txt\n--- a/f.txt\n+++ b/f.txt\n");
    }

    [Test]
    public void Empty_selection_returns_null_no_whole_document_fallback()
    {
        DiffCopyModel.CopySelection(Diff, 0, "", PlainPrefixes).Should().BeNull();
        DiffCopyModel.CopyNewVersion(Diff, 0, "").Should().BeNull();
    }
}
