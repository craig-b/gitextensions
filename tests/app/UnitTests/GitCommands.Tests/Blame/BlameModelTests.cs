using System.Globalization;
using System.Text;
using GitCommands.Blame;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommandsTests.Blame;

public sealed class BlameModelTests
{
    private static readonly ObjectId IdA = ObjectId.Parse("aaaa111111111111111111111111111111111111");
    private static readonly ObjectId IdB = ObjectId.Parse("bbbb222222222222222222222222222222222222");

    private static GitBlameCommit Commit(ObjectId id, string author = "Ann Author", string fileName = "file.cs", DateTime? authorTime = null)
        => new(
            id,
            author,
            "ann@example.com",
            authorTime ?? new DateTime(2024, 6, 1, 12, 0, 0),
            "tz",
            "Committer",
            "committer@example.com",
            authorTime ?? new DateTime(2024, 6, 1, 12, 0, 0),
            "tz",
            "summary",
            fileName);

    private static GitBlameLine Line(GitBlameCommit commit, int lineNumber, string text = "code")
        => new(commit, lineNumber, lineNumber, text);

    [Test]
    public void Gutter_repeats_of_a_commit_are_blank_and_marked()
    {
        GitBlameCommit commitA = Commit(IdA);
        GitBlameCommit commitB = Commit(IdB, author: "Bob");
        GitBlame blame = new([Line(commitA, 1), Line(commitA, 2), Line(commitB, 3)]);

        BlameContents contents = BlameGutterModel.Build(blame, "file.cs", DefaultOptions, CultureInfo.InvariantCulture);

        contents.IsNewCommitLine.Should().Equal(true, false, true);
        string[] gutterLines = contents.Gutter.Split('\n');
        gutterLines[0].Should().Contain("Ann Author");
        gutterLines[1].Trim('\r').Trim().Should().BeEmpty();
        gutterLines[2].Should().Contain("Bob");
        contents.Body.Should().Be($"code{Environment.NewLine}code{Environment.NewLine}code{Environment.NewLine}");
    }

    [Test]
    public void Empty_blame_yields_empty_contents()
    {
        BlameGutterModel.Build(new GitBlame([]), "file.cs", DefaultOptions, CultureInfo.InvariantCulture)
            .Should().BeSameAs(BlameContents.Empty);
    }

    [Test]
    public void Caption_orders_author_and_date_per_options()
    {
        GitBlameLine line = Line(Commit(IdA), 1);
        string dateText = new DateTime(2024, 6, 1, 12, 0, 0).ToString(CultureInfo.InvariantCulture.DateTimeFormat.ShortDatePattern);

        Caption(DefaultOptions).Should().StartWith($"{dateText} - Ann Author");
        Caption(DefaultOptions with { DisplayAuthorFirst = true }).Should().StartWith($"Ann Author - {dateText}");
        Caption(DefaultOptions with { ShowAuthor = false }).Should().StartWith(dateText).And.NotContain("Ann");
        Caption(DefaultOptions with { ShowAuthorDate = false }).Should().StartWith("Ann Author").And.NotContain(dateText);

        string Caption(BlameDisplayOptions options)
            => BlameGutterModel.BuildCaption(line, new StringBuilder(), 80, options.DateTimeFormat(CultureInfo.InvariantCulture), "file.cs", options);
    }

    [Test]
    public void Caption_appends_the_original_path_only_when_it_differs()
    {
        GitBlameLine line = Line(Commit(IdA, fileName: "old/file.cs"), 1);

        BlameGutterModel.BuildCaption(line, new StringBuilder(), 80, "d", "file.cs", DefaultOptions)
            .Should().Contain(" - old/file.cs");
        BlameGutterModel.BuildCaption(line, new StringBuilder(), 80, "d", "old/file.cs", DefaultOptions)
            .Should().NotContain(" - old/file.cs");
        BlameGutterModel.BuildCaption(line, new StringBuilder(), 80, "d", "file.cs", DefaultOptions with { ShowOriginalFilePath = false })
            .Should().NotContain("old/file.cs");
    }

    [Test]
    public void Captions_pad_to_the_line_length()
    {
        GitBlameLine line = Line(Commit(IdA), 1);
        string caption = BlameGutterModel.BuildCaption(line, new StringBuilder(), 60, "d", "file.cs", DefaultOptions);

        caption.TrimEnd('\r', '\n').Length.Should().Be(60);
    }

    [Test]
    public void Line_length_estimate_has_a_floor_and_counts_differing_filenames()
    {
        GitBlame blame = new([Line(Commit(IdA), 1)]);
        BlameGutterModel.EstimateLineLength(blame, "file.cs").Should().Be(80);

        GitBlame withLongPath = new([Line(Commit(IdA, author: new string('a', 40), fileName: new string('p', 40)), 1)]);
        BlameGutterModel.EstimateLineLength(withLongPath, "other.cs").Should().Be(25 + 40 + 40);
    }

    [Test]
    public void Age_buckets_span_seven_and_clamp_old_dates_into_the_first()
    {
        DateTime now = new(2026, 8, 20, 12, 0, 0);
        GitBlameCommit ancient = Commit(IdA, authorTime: now.AddYears(-10));
        GitBlameCommit fresh = Commit(IdB, authorTime: now);
        GitBlameCommit unset = Commit(IdA, authorTime: DateTime.MinValue);

        IReadOnlyList<int> buckets = BlameAgeBuckets.Compute([Line(ancient, 1), Line(fresh, 2), Line(unset, 3)], now);

        buckets[0].Should().Be(0);
        buckets[1].Should().Be(BlameAgeBuckets.BucketCount - 1);
        buckets[2].Should().Be(0);
    }

    [Test]
    public void Line_runs_split_on_gaps_and_use_reference_equality()
    {
        GitBlameCommit commitA = Commit(IdA);
        GitBlameCommit commitB = Commit(IdB);
        IReadOnlyList<GitBlameLine> lines =
            [Line(commitA, 1), Line(commitA, 2), Line(commitB, 3), Line(commitA, 4)];

        BlameLineRuns.ForCommit(lines, commitA).Should().Equal((0, 1), (3, 3));
        BlameLineRuns.ForCommit(lines, commitB).Should().Equal((2, 2));
        BlameLineRuns.ForCommit(lines, Commit(IdA)).Should().BeEmpty();
    }

    [Test]
    public void Target_line_prefers_clicked_then_initial_then_position()
    {
        BlameTargetLine.Resolve(clickedOriginLineNumber: 7, initialLine: 3, sameFile: true, currentFileLine: 12).Should().Be(7);
        BlameTargetLine.Resolve(clickedOriginLineNumber: null, initialLine: 3, sameFile: true, currentFileLine: 12).Should().Be(3);
        BlameTargetLine.Resolve(clickedOriginLineNumber: null, initialLine: null, sameFile: true, currentFileLine: 12).Should().Be(12);
        BlameTargetLine.Resolve(clickedOriginLineNumber: null, initialLine: null, sameFile: false, currentFileLine: 12).Should().Be(1);
        BlameTargetLine.Clamp(99, lineCount: 40).Should().Be(40);
    }

    [Test]
    public void Blame_previous_prefers_the_actual_parent_in_grid()
    {
        GitRevision visible = RevisionWithParent(IdA);
        GitRevision actual = RevisionWithParent(IdB);

        BlamePreviousRevisionModel.Resolve(visible, actual, parentId => parentId?.Equals(IdB) is true)
            .Should().Be(BlamePreviousTarget.ActualParent);
        BlamePreviousRevisionModel.Resolve(visible, actual, parentId => parentId?.Equals(IdA) is true)
            .Should().Be(BlamePreviousTarget.VisibleParent);
        BlamePreviousRevisionModel.Resolve(visible, actual, _ => false)
            .Should().Be(BlamePreviousTarget.Disabled);
        BlamePreviousRevisionModel.Resolve(null, actual, _ => true)
            .Should().Be(BlamePreviousTarget.Disabled);
    }

    private static BlameDisplayOptions DefaultOptions => new(
        ShowAuthor: true,
        ShowAuthorDate: true,
        ShowAuthorTime: false,
        DisplayAuthorFirst: false,
        ShowOriginalFilePath: true);

    private static GitRevision RevisionWithParent(ObjectId parentId)
        => new(ObjectId.Parse("cccc333333333333333333333333333333333333")) { ParentIds = [parentId] };
}
