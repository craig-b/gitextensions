using System.Collections.Immutable;
using GitExtUtils.GitUI.Theming;
using GitUI.Editor.Diff;
using GitUI.Theming;

namespace GitUITests.Editor.Diff;

[Apartment(ApartmentState.STA)]
public class DiffHighlightServiceTests
{
    [Test]
    public void GetDifferenceMarkers_should_dim_identical_parts_at_begin_and_end()
    {
        const string identicalPartBefore = "identical_part_before_";
        const string identicalPartAfter = "_identical_part_after";
        const string differentRemoved = "RemovedX";
        const string differentAdded = "AddedY";
        const string removedLineText = $"-{identicalPartBefore}{differentRemoved}{identicalPartAfter}";
        const string addedLineText = $"+{identicalPartBefore}{differentAdded}{identicalPartAfter}";
        const string text = $"{removedLineText}\n{addedLineText}";
        Segment removedLine = new(Offset: text.IndexOf(removedLineText), Length: removedLineText.Length);
        Segment addedLine = new(Offset: text.IndexOf(addedLineText), Length: addedLineText.Length);
        const int beginOffset = 1;

        List<StyledSpan> markers = [];
        DiffHighlightService.AddDifferenceMarkers(markers, GetText, removedLine, addedLine, beginOffset, dimBackground: true);
        IReadOnlyList<StyledSpan> sortedMarkers = markers.ToImmutableSortedSet(new MarkerComparer());

        StyledSpan[] expectedMarkers =
        [
            CreateDimmedMarker(removedLine, offset: 0, length: identicalPartBefore.Length),
            CreateDimmedMarker(removedLine, offset: identicalPartBefore.Length + differentRemoved.Length, length: identicalPartAfter.Length),
            CreateDimmedMarker(addedLine, offset: 0, length: identicalPartBefore.Length),
            CreateDimmedMarker(addedLine, offset: identicalPartBefore.Length + differentAdded.Length, length: identicalPartAfter.Length),
        ];
        sortedMarkers.Should().BeEquivalentTo(expectedMarkers);

        return;

        StyledSpan CreateDimmedMarker(Segment line, int offset, int length)
            => DiffHighlightServiceTests.CreateDimmedMarker(line.Offset + beginOffset + offset, length, isAdded: line == addedLine);

        string GetText(Segment line) => (line == removedLine ? removedLineText : addedLineText)[beginOffset..];
    }

    [Test]
    public void GetDifferenceMarkers_should_add_anchor_markers()
    {
        const string deletion = nameof(deletion);
        const string insertion = nameof(insertion);
        const string identicalPartBefore = " identical_part_before ";
        const string identicalPartAfter = " identical_part_after ";
        const string differentRemoved = "RemovedX";
        const string differentAdded = "AddedY";
        const string removedLineText = $"-{deletion}{identicalPartBefore}{differentRemoved}{identicalPartAfter}";
        const string addedLineText = $"+{identicalPartBefore}{differentAdded}{identicalPartAfter}{insertion}";
        const string text = $"{removedLineText}\n{addedLineText}";
        Segment removedLine = new(Offset: text.IndexOf(removedLineText), Length: removedLineText.Length);
        Segment addedLine = new(Offset: text.IndexOf(addedLineText), Length: addedLineText.Length);
        const int beginOffset = 1;

        List<StyledSpan> markers = [];
        DiffHighlightService.AddDifferenceMarkers(markers, GetText, removedLine, addedLine, beginOffset, dimBackground: true);
        IReadOnlyList<StyledSpan> sortedMarkers = markers.ToImmutableSortedSet(new MarkerComparer());

        StyledSpan[] expectedMarkers =
        [
            CreateDimmedMarker(removedLine, offset: deletion.Length, length: identicalPartBefore.Length),
            CreateDimmedMarker(removedLine, offset: deletion.Length + identicalPartBefore.Length + differentRemoved.Length, length: identicalPartAfter.Length),
            CreateAnchorMarker(removedLine, offset: removedLine.Length - 1),
            CreateAnchorMarker(addedLine, offset: 0),
            CreateDimmedMarker(addedLine, offset: 0, length: identicalPartBefore.Length),
            CreateDimmedMarker(addedLine, offset: identicalPartBefore.Length + differentAdded.Length, length: identicalPartAfter.Length),
        ];
        sortedMarkers.Should().BeEquivalentTo(expectedMarkers);

        return;

        StyledSpan CreateAnchorMarker(Segment line, int offset)
            => DiffHighlightServiceTests.CreateAnchorMarker(line.Offset + beginOffset + offset, isAdded: line == addedLine);

        StyledSpan CreateDimmedMarker(Segment line, int offset, int length)
            => DiffHighlightServiceTests.CreateDimmedMarker(line.Offset + beginOffset + offset, length, isAdded: line == addedLine);

        string GetText(Segment line) => (line == removedLine ? removedLineText : addedLineText)[beginOffset..];
    }

    [Test]
    public async Task MarkInlineGap()
    {
        string testDataDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Editor", "Diff");
        string text = File.ReadAllText(Path.Combine(testDataDir, "gaps.diff"));

        // ensure that the test doesn't fail if the local Git configuration has core.autocrlf enabled
        text = text.Replace("\r\n", "\n");

        PatchHighlightService service = new(ref text, useGitColoring: true);
        DiffLinesInfo result = service.DiffLinesInfo;
        DiffLineInfo[] diffLines = [.. result.DiffLines.Values.OrderBy(l => l.LineNumInDiff)];

        int index = 0;
        List<(IReadOnlyList<Segment> removed, IReadOnlyList<Segment> added)> sections = [];
        while (index < diffLines.Length)
        {
            // git-diff presents the removed lines directly followed by the added in a "block"
            IReadOnlyList<Segment> linesRemoved = DiffHighlightService.TestAccessor.GetBlockOfLines(diffLines, DiffLineType.Minus, ref index, found: false);
            if (linesRemoved.Count == 0)
            {
                continue;
            }

            IReadOnlyList<Segment> linesAdded = DiffHighlightService.TestAccessor.GetBlockOfLines(diffLines, DiffLineType.Plus, ref index, found: true);
            if (linesAdded.Count == 0)
            {
                continue;
            }

            sections.Add((removed: linesRemoved, linesAdded));
        }

        await Verify(sections);
    }

    private static StyledSpan CreateAnchorMarker(int offset, bool isAdded)
    {
        Color color = (isAdded ? AppColor.AnsiTerminalRedForeBold : AppColor.AnsiTerminalGreenForeBold).GetThemeColor();
        return new StyledSpan(offset, Length: 0, Foreground: null, Background: color);
    }

    private static StyledSpan CreateDimmedMarker(int offset, int length, bool isAdded)
    {
        Color color = (isAdded ? AppColor.AnsiTerminalGreenBackNormal : AppColor.AnsiTerminalRedBackNormal).GetThemeColor();
        Color dimmedColor = color.DimColor().DimColor();
        return new StyledSpan(offset, length, Foreground: dimmedColor.GetTextColor(), Background: dimmedColor);
    }

    private sealed class MarkerComparer : IComparer<StyledSpan>
    {
        public int Compare(StyledSpan left, StyledSpan right)
            => left.Offset < right.Offset ? -1
                : left.Offset > right.Offset ? 1
                : left.Length == 0 ? -1
                : right.Length == 0 ? 1
                : throw new InvalidOperationException("markers should not overlap");
    }
}
