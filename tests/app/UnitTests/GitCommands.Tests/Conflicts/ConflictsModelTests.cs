using GitCommands.Conflicts;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.Conflicts;

public sealed class ConflictsModelTests
{
    private static readonly ObjectId Id = ObjectId.Parse("aaaa111111111111111111111111111111111111");

    private static ConflictData Conflict(string? baseName, string? localName, string? remoteName)
        => new(
            new ConflictedFileData(Id, baseName!),
            new ConflictedFileData(Id, localName!),
            new ConflictedFileData(Id, remoteName!));

    [TestCase("f", "f", "f", ConflictKind.ChangedBothSides)]
    [TestCase(null, "f", "f", ConflictKind.AddedBothSides)]
    [TestCase("f", null, "f", ConflictKind.DeletedLocallyModifiedRemotely)]
    [TestCase("f", "f", null, ConflictKind.ModifiedLocallyDeletedRemotely)]
    [TestCase("f", null, null, ConflictKind.Unknown)]
    public void Classification_follows_side_existence(string? baseName, string? localName, string? remoteName, ConflictKind expected)
    {
        ConflictClassifier.Classify(Conflict(baseName, localName, remoteName)).Should().Be(expected);
    }

    [Test]
    public void Buckets_split_by_deletion_side_and_reverse_to_grid_order()
    {
        ConflictData deletedLocally1 = Conflict("f1", null, "f1");
        ConflictData deletedLocally2 = Conflict("f2", null, "f2");
        ConflictData deletedRemotely = Conflict("g", "g", null);
        ConflictData plain = Conflict("h", "h", "h");

        ConflictSelectionBuckets buckets = ConflictClassifier.Bucket([deletedLocally1, deletedLocally2, deletedRemotely, plain]);

        buckets.DeletedLocallyModifiedRemotely.Should().Equal(deletedLocally2, deletedLocally1);
        buckets.ModifiedLocallyDeletedRemotely.Should().Equal(deletedRemotely);
        buckets.Remaining.Should().Equal(plain);
    }

    [Test]
    public void Rebase_swaps_the_side_labels()
    {
        ConflictSideLabels.Resolve(inTheMiddleOfRebase: false, "ours", "theirs")
            .Should().Be(new ConflictSideLabels("ours", "theirs"));
        ConflictSideLabels.Resolve(inTheMiddleOfRebase: true, "ours", "theirs")
            .Should().Be(new ConflictSideLabels("theirs", "ours"));
    }

    [Test]
    public void Completion_fires_only_when_conflicts_existed_and_are_gone()
    {
        ConflictCompletionDecision.Evaluate(stillConflicted: false, thereWereConflicts: true, inTheMiddleOfPatch: false, inTheMiddleOfRebase: false, offerCommit: true)
            .Should().Be(new ConflictCompletionDecision(ShouldUpdateSubmodules: true, ShouldOfferCommit: true, ShouldClose: true));
        ConflictCompletionDecision.Evaluate(stillConflicted: false, thereWereConflicts: true, inTheMiddleOfPatch: false, inTheMiddleOfRebase: true, offerCommit: true)
            .ShouldOfferCommit.Should().BeFalse();
        ConflictCompletionDecision.Evaluate(stillConflicted: true, thereWereConflicts: true, inTheMiddleOfPatch: false, inTheMiddleOfRebase: false, offerCommit: true)
            .ShouldClose.Should().BeFalse();
        ConflictCompletionDecision.Evaluate(stillConflicted: false, thereWereConflicts: false, inTheMiddleOfPatch: false, inTheMiddleOfRebase: false, offerCommit: true)
            .ShouldClose.Should().BeFalse();
    }

    [Test]
    public void Mergetool_resolution_prefers_guitool_and_applies_kdiff3_defaults()
    {
        MergeToolConfiguration config = MergeToolConfiguration.Resolve(
            key => key switch
            {
                "merge.guitool" => "kdiff3",
                _ => null,
            },
            supportsGuiMergeTool: true,
            isWindows: false);

        config.Tool.Should().Be("kdiff3");
        config.Path.Should().Be("kdiff3");
        config.Command.Should().Be("\"$BASE\" \"$LOCAL\" \"$REMOTE\" -o \"$MERGED\"");
        config.SupportsDirectLaunch.Should().BeTrue();

        MergeToolConfiguration.Resolve(_ => null, supportsGuiMergeTool: true, isWindows: false).Tool.Should().BeNull();
        MergeToolConfiguration.Resolve(
            key => key == "merge.tool" ? "meld" : null, supportsGuiMergeTool: false, isWindows: false).Tool.Should().Be("meld");
    }

    [Test]
    public void Windows_exe_split_separates_path_from_arguments()
    {
        MergeToolConfiguration config = MergeToolConfiguration.Resolve(
            key => key switch
            {
                "merge.tool" => "custom",
                "mergetool.custom.cmd" => "\"C:\\tools\\merge.exe\" \"$LOCAL\" \"$REMOTE\"",
                _ => null,
            },
            supportsGuiMergeTool: false,
            isWindows: true);

        config.Path.Should().Be("C:\\tools\\merge.exe");

        // The historical split leaves the space after the closing quote on the command side.
        config.Command.Should().Be(" \"$LOCAL\" \"$REMOTE\"");
    }

    [Test]
    public void Argument_substitution_and_two_way_rewriting()
    {
        MergeToolArguments.Substitute("\"$BASE\" \"$LOCAL\" \"$REMOTE\" -o \"$MERGED\"", "b", "l", "r", "m")
            .Should().Be("\"b\" \"l\" \"r\" -o \"m\"");

        MergeToolArguments.To2Way("kdiff3", "\"$BASE\" \"$LOCAL\"").Should().Be(" \"$LOCAL\"");
        MergeToolArguments.To2Way("tortoisemerge", "-base:\"$BASE\" mine:\"$LOCAL\"").Should().Be(" base:\"$LOCAL\"");
        MergeToolArguments.To2Way("unknown", "\"$BASE\"").Should().Be("\"$BASE\"");
    }

    [TestCase(0, true, true, true, false)]
    [TestCase(1, false, true, false, true)]
    [TestCase(0, true, false, false, true)]
    [TestCase(2, false, false, false, false)]
    public void Mergetool_result_matrix(int exitCode, bool exitedSuccessfully, bool fileChanged, bool expectStage, bool expectAsk)
    {
        MergeToolResultDecision decision = MergeToolResultDecision.Evaluate(exitCode, exitedSuccessfully, fileChanged);

        decision.ShouldStage.Should().Be(expectStage);
        decision.ShouldAskUser.Should().Be(expectAsk);
    }

    [Test]
    public void Resolution_choices_per_kind()
    {
        ConflictResolutionChoices.For(ConflictKind.AddedBothSides)
            .Should().Equal(ConflictOutcome.TakeLocal, ConflictOutcome.TakeRemote, ConflictOutcome.DeleteFile);
        ConflictResolutionChoices.For(ConflictKind.DeletedLocallyModifiedRemotely)
            .Should().Equal(ConflictOutcome.DeleteFile, ConflictOutcome.TakeRemote, ConflictOutcome.TakeBase);
        ConflictResolutionChoices.For(ConflictKind.ModifiedLocallyDeletedRemotely)
            .Should().Equal(ConflictOutcome.TakeLocal, ConflictOutcome.DeleteFile, ConflictOutcome.TakeBase);
        ConflictResolutionChoices.For(ConflictKind.ChangedBothSides)
            .Should().Equal(ConflictOutcome.TakeLocal, ConflictOutcome.TakeRemote, ConflictOutcome.TakeBase);
    }
}
